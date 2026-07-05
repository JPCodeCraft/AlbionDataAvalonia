using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Legendary.Models;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class LegendaryViewModel : ViewModelBase
{
    private readonly LegendaryItemTrackerService tracker;
    private readonly LegendaryDefinitionsService definitions;
    private readonly LegendarySaleService saleService;
    private readonly AuthService authService;
    private readonly PlayerState playerState;
    private readonly SettingsManager settingsManager;
    private List<LegendaryItemRowViewModel> unfilteredItems = new();
    private IReadOnlyDictionary<(int ServerId, Guid SoulId), LegendarySaleListing> saleListings =
        new Dictionary<(int ServerId, Guid SoulId), LegendarySaleListing>();
    private bool loaded;
    private int refreshQueued;
    private string? currentUserId;

    public ObservableCollection<LegendaryItemRowViewModel> Items { get; } = new();
    public IReadOnlyList<string> Servers { get; } = ["Any", .. AlbionServers.GetAll().Select(server => server.Name)];
    public IReadOnlyList<string> SaleStatuses { get; } = ["All", "Active", "Sold", "Canceled", "Not listed"];

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private string selectedServer = "Any";

    [ObservableProperty]
    private bool onlyAttunedToMe;

    [ObservableProperty]
    private string selectedSaleStatus = "All";

    [ObservableProperty]
    private LegendaryItemRowViewModel? selectedItem;

    [ObservableProperty]
    private string saleStatus = "Select a complete awakened item to list it for sale.";

    [ObservableProperty]
    private bool isPosting;

    [ObservableProperty]
    private bool isAwakeningItemsTrackerDisabled;

    public bool CanListSelectedItem =>
        SelectedItem?.CanPost == true
        && IsSignedIn
        && !IsPosting;

    public bool CanCancelSelectedListing =>
        SelectedItem?.IsActiveListing == true
        && IsSignedIn
        && !IsPosting;

    public bool HasSelectedItem => SelectedItem is not null;

    public bool CanFilterAttunedToMe => IsDefinedPlayerName(playerState?.PlayerName);

    public bool IsSignedIn => !string.IsNullOrWhiteSpace(authService?.FirebaseUserId);

    public LegendaryViewModel()
    {
        tracker = null!;
        definitions = null!;
        saleService = null!;
        authService = null!;
        playerState = null!;
        settingsManager = null!;
    }

    public LegendaryViewModel(
        LegendaryItemTrackerService tracker,
        LegendaryDefinitionsService definitions,
        LegendarySaleService saleService,
        AuthService authService,
        PlayerState playerState,
        SettingsManager settingsManager)
    {
        this.tracker = tracker;
        this.definitions = definitions;
        this.saleService = saleService;
        this.authService = authService;
        this.playerState = playerState;
        this.settingsManager = settingsManager;
        isAwakeningItemsTrackerDisabled = settingsManager.UserSettings.DisableAwakeningItemsTracker;
        currentUserId = authService.FirebaseUserId;
        tracker.ItemsChanged += HandleItemsChanged;
        settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        authService.FirebaseUserChanged += user => Dispatcher.UIThread.Post(async () => await HandleAuthChangedAsync(user?.LocalId));
        playerState.OnPlayerStateChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(CanFilterAttunedToMe));
                if (!CanFilterAttunedToMe)
                {
                    OnlyAttunedToMe = false;
                }
                else if (OnlyAttunedToMe)
                {
                    ApplyFilter();
                }
            });
        };
    }

    public void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }
        loaded = true;
        _ = InitializeAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadSaleListingsAsync();
        await LoadItemsAsync();
    }

    public async Task<int> DeleteSelectedItemsAsync(
        IEnumerable<LegendaryItemRowViewModel> selected,
        CancellationToken cancellationToken = default)
    {
        var selectedItems = selected.ToList();
        if (selectedItems.Count == 0)
        {
            return 0;
        }

        var deletedCount = await tracker.DeleteItemsAsync(
            selectedItems.Select(item => item.Source.Id),
            cancellationToken);
        await LoadItemsAsync();
        SaleStatus = $"Deleted {deletedCount:N0} awakened item{(deletedCount == 1 ? string.Empty : "s")}.";
        return deletedCount;
    }

    public string DefaultInGameName => !string.IsNullOrWhiteSpace(playerState?.PlayerName)
        ? playerState.PlayerName
        : SelectedItem?.Source.SeenByPlayerName ?? string.Empty;

    public async Task<LegendarySaleOperationResult> CreateSellOrderAsync(string priceSilver, string inGameName)
    {
        var selectedItem = SelectedItem;
        if (selectedItem is null)
        {
            return LegendarySaleOperationResult.Failed("Select an awakened item first.");
        }
        if (!IsSignedIn)
        {
            return LegendarySaleOperationResult.Failed("Sign in to AFM before listing an awakened item.");
        }
        if (!selectedItem.CanPost)
        {
            return LegendarySaleOperationResult.Failed("This item is missing data required for a sale listing.");
        }

        IsPosting = true;
        SaleStatus = "Listing awakened item for sale...";
        try
        {
            var result = await saleService.CreateSellOrderAsync(selectedItem.Source, priceSilver, inGameName);
            SaleStatus = result.RetryAfterSeconds is { } retry
                ? $"{result.Message} Discord can be tried again in {TimeSpan.FromSeconds(retry):g}."
                : result.Message;
            if (result.Listing is not null)
            {
                StoreListing(result.Listing);
                await LoadItemsAsync();
            }
            return result;
        }
        finally
        {
            IsPosting = false;
        }
    }

    public async Task<LegendarySaleOperationResult> SetSelectedSoldAsync(bool sold)
    {
        var selected = SelectedItem;
        if (selected?.Source.SoulId is not { } soulId || !selected.HasListing)
        {
            return LegendarySaleOperationResult.Failed("This item does not have a sale listing.");
        }

        IsPosting = true;
        SaleStatus = sold ? "Marking awakened item as sold..." : "Marking awakened item as available...";
        try
        {
            var result = await saleService.SetSoldAsync(selected.Source.AlbionServerId, soulId, sold);
            SaleStatus = result.Message;
            if (result.Listing is not null)
            {
                StoreListing(result.Listing);
                await LoadItemsAsync();
            }
            return result;
        }
        finally
        {
            IsPosting = false;
        }
    }

    public async Task<LegendarySaleOperationResult> CancelSelectedListingAsync()
    {
        var selected = SelectedItem;
        if (selected?.Source.SoulId is not { } soulId || !selected.IsActiveListing)
        {
            return LegendarySaleOperationResult.Failed("This item does not have an active sale listing.");
        }

        IsPosting = true;
        SaleStatus = "Canceling awakened sale listing...";
        try
        {
            var result = await saleService.CancelAsync(selected.Source.AlbionServerId, soulId);
            SaleStatus = result.Message;
            if (result.Listing is not null)
            {
                StoreListing(result.Listing);
                await LoadItemsAsync();
            }
            return result;
        }
        finally
        {
            IsPosting = false;
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSelectedServerChanged(string value) => ApplyFilter();
    partial void OnOnlyAttunedToMeChanged(bool value) => ApplyFilter();
    partial void OnSelectedSaleStatusChanged(string value) => ApplyFilter();
    partial void OnSelectedItemChanged(LegendaryItemRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedItem));
        OnPropertyChanged(nameof(CanListSelectedItem));
        OnPropertyChanged(nameof(CanCancelSelectedListing));
        UpdateSaleStatus();
    }
    partial void OnIsPostingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanListSelectedItem));
        OnPropertyChanged(nameof(CanCancelSelectedListing));
    }

    private async Task InitializeAsync()
    {
        await definitions.InitializeAsync();
        await LoadSaleListingsAsync();
        await LoadItemsAsync();
    }

    private async Task HandleAuthChangedAsync(string? userId)
    {
        if (string.Equals(currentUserId, userId, StringComparison.Ordinal))
        {
            return;
        }
        currentUserId = userId;
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(CanListSelectedItem));
        OnPropertyChanged(nameof(CanCancelSelectedListing));
        if (loaded)
        {
            await LoadSaleListingsAsync();
            await LoadItemsAsync();
        }
        UpdateSaleStatus();
    }

    private async Task LoadSaleListingsAsync()
    {
        var listings = new Dictionary<(int ServerId, Guid SoulId), LegendarySaleListing>();
        if (IsSignedIn)
        {
            foreach (var listing in await saleService.GetListingsAsync())
            {
                if (Guid.TryParse(listing.SoulId, out var soulId) && soulId != Guid.Empty)
                {
                    listings[(listing.ServerId, soulId)] = listing;
                }
            }
        }
        saleListings = listings;
    }

    private void StoreListing(LegendarySaleListing listing)
    {
        if (!Guid.TryParse(listing.SoulId, out var soulId) || soulId == Guid.Empty)
        {
            return;
        }
        saleListings = new Dictionary<(int ServerId, Guid SoulId), LegendarySaleListing>(saleListings)
        {
            [(listing.ServerId, soulId)] = listing
        };
    }

    private async Task LoadItemsAsync()
    {
        try
        {
            var items = await tracker.GetItemsAsync().ConfigureAwait(false);
            var listings = saleListings;
            var rows = items.Select(item => new LegendaryItemRowViewModel(
                item,
                definitions,
                item.SoulId is { } soulId
                    && listings.TryGetValue((item.AlbionServerId, soulId), out var listing)
                    ? listing
                    : null)).ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var selectedId = SelectedItem?.Source.Id;
                unfilteredItems = rows;
                ApplyFilter();
                SelectedItem = Items.FirstOrDefault(row => row.Source.Id == selectedId) ?? Items.FirstOrDefault();
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load legendary items");
        }
    }

    private void UpdateSaleStatus()
    {
        SaleStatus = SelectedItem switch
        {
            null => "Select a complete awakened item to list it for sale.",
            { CanPost: false } => "This item is missing data required for a sale listing.",
            _ when !IsSignedIn => "Sign in to AFM to list awakened items.",
            { HasListing: true, IsSold: true } => "This item's AFM sale listing is marked as sold.",
            { HasListing: true, IsCanceled: true } => "This item's AFM sale listing is canceled.",
            { HasListing: true } => "This item is currently listed for sale on AFM.",
            _ => "This item is ready to list for sale on AFM."
        };
    }

    private void HandleItemsChanged()
    {
        if (Interlocked.Exchange(ref refreshQueued, 1) != 0)
        {
            return;
        }
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(150);
            Interlocked.Exchange(ref refreshQueued, 0);
            await LoadItemsAsync();
        });
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettings.DisableAwakeningItemsTracker))
        {
            Dispatcher.UIThread.Post(() =>
                IsAwakeningItemsTrackerDisabled = settingsManager.UserSettings.DisableAwakeningItemsTracker);
        }
    }

    private void ApplyFilter()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyFilter);
            return;
        }
        var filter = FilterText.Trim();
        var playerName = playerState.PlayerName;
        var rows = unfilteredItems.Where(row =>
            (SelectedServer == "Any" || string.Equals(row.ServerName, SelectedServer, StringComparison.OrdinalIgnoreCase))
            && (!OnlyAttunedToMe
                || (IsDefinedPlayerName(playerName)
                    && string.Equals(row.Source.AttunedToPlayerName, playerName, StringComparison.OrdinalIgnoreCase)))
            && MatchesSaleStatus(row, SelectedSaleStatus)
            && (string.IsNullOrWhiteSpace(filter) || row.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }
    }

    private static bool IsDefinedPlayerName(string? playerName)
    {
        return !string.IsNullOrWhiteSpace(playerName)
            && !string.Equals(playerName, "Not set", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSaleStatus(LegendaryItemRowViewModel row, string status)
    {
        return status switch
        {
            "Active" => row.IsActiveListing,
            "Sold" => row.IsSold,
            "Canceled" => row.IsCanceled,
            "Not listed" => !row.HasListing,
            _ => true
        };
    }

}

public sealed class LegendaryItemRowViewModel
{
    public LegendaryItemRowViewModel(
        LegendaryItem source,
        LegendaryDefinitionsService definitions,
        LegendarySaleListing? listing)
    {
        Source = source;
        Listing = listing;
        SellOrders = listing?.SellOrders.Select(order => new LegendarySellOrderRowViewModel(order)).ToList()
            ?? [];
        var itemNameFallback = string.IsNullOrWhiteSpace(source.ItemName) ? source.ItemUniqueName : source.ItemName;
        ItemName = ItemNameFormatter.FormatUsName(
            source.ItemUniqueName,
            definitions.FindItemUsName(source.ItemUniqueName, itemNameFallback));
        var awakenedItemPowerBonus = definitions.CalculateAwakenedItemPowerBonus(source.Traits);
        Traits = source.Traits
            .OrderBy(trait => trait.Position)
            .Select(trait => new LegendaryTraitRowViewModel(
                trait,
                definitions.FindTrait(trait.TraitId),
                definitions.CalculateTraitValue(
                    source.ItemUniqueName,
                    source.Quality,
                    trait.TraitId,
                    trait.Value,
                    CultureInfo.CurrentCulture,
                    awakenedItemPowerBonus)))
            .ToList();
        LegendaryRatingValue = definitions.CalculateLegendaryRating(
            source.ItemUniqueName,
            source.Traits.Select(trait => trait.Value));
        CalculatorUrl = AwakenedCalculatorUrlBuilder.Build(source);
        SearchText = string.Join(' ', new[]
        {
            ItemName,
            Quality,
            source.AttunedToPlayerName,
            string.Join(' ', Traits.Select(trait => trait.Name))
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public LegendaryItem Source { get; }
    public LegendarySaleListing? Listing { get; }
    public IReadOnlyList<LegendaryTraitRowViewModel> Traits { get; }
    public IReadOnlyList<LegendarySellOrderRowViewModel> SellOrders { get; }
    public string SearchText { get; }
    public string ItemName { get; }
    public string SoulName => Source.SoulName ?? string.Empty;
    public bool HasSoulName => !string.IsNullOrWhiteSpace(Source.SoulName);
    public string ItemUniqueName => Source.ItemUniqueName;
    public string? CalculatorUrl { get; }
    public bool CanViewInCalculator => CalculatorUrl is not null;
    public string ServerName => AlbionServers.Get(Source.AlbionServerId)?.Name ?? "Unknown";
    public string Quality => ItemQuality.Format(Source.Quality);
    public int ImageQuality => Source.Quality <= 0 ? 1 : Source.Quality;
    public string AttunedTo => string.IsNullOrWhiteSpace(Source.AttunedToPlayerName)
        ? "Unknown"
        : Source.AttunedToPlayerName;
    public string Attunement => Source.Attunement?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";
    public long? LegendaryRatingValue { get; }
    public string LegendaryRating => LegendaryRatingValue?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";
    public string Strain => Source.Strain?.ToString("N2", CultureInfo.CurrentCulture) ?? "Unknown";
    public string PvPFameGained => Source.PvPFameGained?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";
    public string AttunementSpent => Source.AttunementSpent?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";
    public string TraitsSummary => Traits.Count == 0 ? "No traits" : string.Join("; ", Traits.Select(trait => $"{trait.Value} {trait.Name}"));
    public bool HasTraits => Traits.Count > 0;
    public string Location => string.IsNullOrWhiteSpace(Source.LocationName) ? "Unknown" : Source.LocationName;
    public bool HasListing => Listing is not null;
    public bool IsSold => Listing?.Sold == true;
    public bool IsCanceled => Listing?.Canceled == true;
    public bool IsActiveListing => HasListing && !IsSold && !IsCanceled;
    public bool CanChangeSold => HasListing && !IsCanceled;
    public string SaleState => Listing is null ? "-" : IsSold ? "Sold" : IsCanceled ? "Canceled" : "Active";
    public string LatestPrice => Listing is null ? "-" : FormatPrice(Listing.LatestPriceSilver);
    public string LastListed => Listing is { } listing
        ? FormatUtc(listing.LastListedAt)
        : "-";
    public string LastSeen => FormatUtc(Source.LastSeenAtUtc);
    public string FirstSeen => FormatUtc(Source.FirstSeenAtUtc);
    public bool CanPost => Source.HasItemDetails
        && Source.HasLegendaryDetails
        && Source.AlbionServerId is >= 1 and <= 3
        && Source.SoulId is { } soulId
        && soulId != Guid.Empty
        && Source.Era is not null
        && Source.PvPFameGained is not null
        && Source.AttunementSpent is not null
        && LegendaryRatingValue is not null
        && !string.IsNullOrWhiteSpace(Source.ItemUniqueName);

    private static string FormatUtc(DateTime value)
    {
        return $"{DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("g", CultureInfo.CurrentCulture)} UTC";
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return $"{value.UtcDateTime.ToString("g", CultureInfo.CurrentCulture)} UTC";
    }

    private static string FormatPrice(string value)
    {
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var price)
            ? $"{price.ToString("N0", CultureInfo.CurrentCulture)} silver"
            : $"{value} silver";
    }
}

public sealed class LegendarySellOrderRowViewModel
{
    public LegendarySellOrderRowViewModel(LegendarySellOrder source)
    {
        Source = source;
    }

    public LegendarySellOrder Source { get; }
    public string ListedAt => $"{Source.CreatedAt.UtcDateTime.ToString("g", CultureInfo.CurrentCulture)} UTC";
    public string Price => long.TryParse(Source.PriceSilver, NumberStyles.None, CultureInfo.InvariantCulture, out var price)
        ? $"{price.ToString("N0", CultureInfo.CurrentCulture)} silver"
        : $"{Source.PriceSilver} silver";
    public string Contact => string.IsNullOrWhiteSpace(Source.DiscordUsername)
        ? Source.InGameName
        : $"{Source.InGameName} · Discord: {Source.DiscordUsername}";
    public string DiscordStatus => Source.Discord.Status switch
    {
        "posted" => "Posted to Discord",
        "not_linked" => "Discord not linked",
        "not_member" => "Not in AFM Discord",
        "rate_limited" => "Discord rate-limited",
        "failed" => "Discord post failed",
        "pending" => "Discord post pending",
        _ => "Discord unavailable"
    };
    public string? MessageUrl => Source.Discord.MessageUrl;
    public bool HasDiscordPost => !string.IsNullOrWhiteSpace(MessageUrl);
}

public sealed class LegendaryTraitRowViewModel
{
    public LegendaryTraitRowViewModel(
        LegendaryItemTrait source,
        LegendaryTraitDefinition? definition,
        LegendaryCalculatedValue calculatedValue)
    {
        Id = source.TraitId;
        Name = string.IsNullOrWhiteSpace(definition?.UsName) ? source.TraitId : definition.UsName;
        Value = FormatDisplayValue(calculatedValue.FormattedText);
        RollPercentage = calculatedValue.RollPercentage;
        Rarity = definition?.Rarity.ToLowerInvariant() switch
        {
            "common" => "Common",
            "uncommon" => "Uncommon",
            _ => string.Empty
        };
    }

    public string Id { get; }
    public string Name { get; }
    public string Value { get; }
    public double RollPercentage { get; }
    public string Rarity { get; }

    private static string FormatDisplayValue(string value)
    {
        var suffix = value.EndsWith('%') ? "%" : string.Empty;
        var numericText = suffix.Length == 0 ? value : value[..^1];
        return double.TryParse(numericText, NumberStyles.Number, CultureInfo.CurrentCulture, out var numericValue)
            ? $"{numericValue.ToString("+0.00;-0.00;+0.00", CultureInfo.CurrentCulture)}{suffix}"
            : value;
    }
}

using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Legendary.Models;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.State;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class LegendaryViewModel : ViewModelBase
{
    private static readonly TimeSpan EligibilityCacheDuration = TimeSpan.FromMinutes(1);
    private readonly LegendaryItemTrackerService tracker;
    private readonly LegendaryDefinitionsService definitions;
    private readonly LegendaryDiscordSaleService discordSaleService;
    private readonly PlayerState playerState;
    private List<LegendaryItemRowViewModel> unfilteredItems = new();
    private IReadOnlyDictionary<(int ServerId, Guid SoulId), DateTimeOffset> discordPostDates =
        new Dictionary<(int ServerId, Guid SoulId), DateTimeOffset>();
    private readonly Dictionary<int, CachedSaleEligibility> eligibilityCache = new();
    private readonly Dictionary<(long Generation, int ServerId), Task<LegendarySaleEligibility>> eligibilityRequests = new();
    private bool loaded;
    private int refreshQueued;
    private long eligibilityVersion;
    private long eligibilityCacheGeneration;
    private string? eligibilityUserId;

    public ObservableCollection<LegendaryItemRowViewModel> Items { get; } = new();
    public IReadOnlyList<string> Servers { get; } = ["Any", .. AlbionServers.GetAll().Select(server => server.Name)];

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private string selectedServer = "Any";

    [ObservableProperty]
    private LegendaryItemRowViewModel? selectedItem;

    [ObservableProperty]
    private string saleStatus = "Select a complete legendary item to check Discord posting eligibility.";

    [ObservableProperty]
    private bool isPosting;

    private LegendarySaleEligibility? saleEligibility;

    public bool CanSellSelectedItem =>
        SelectedItem?.CanPost == true
        && saleEligibility?.CanPost == true
        && !IsPosting;

    public bool HasSelectedItem => SelectedItem is not null;

    public LegendaryViewModel()
    {
        tracker = null!;
        definitions = null!;
        discordSaleService = null!;
        playerState = null!;
    }

    public LegendaryViewModel(
        LegendaryItemTrackerService tracker,
        LegendaryDefinitionsService definitions,
        LegendaryDiscordSaleService discordSaleService,
        AuthService authService,
        PlayerState playerState)
    {
        this.tracker = tracker;
        this.definitions = definitions;
        this.discordSaleService = discordSaleService;
        this.playerState = playerState;
        eligibilityUserId = authService.FirebaseUserId;
        tracker.ItemsChanged += HandleItemsChanged;
        authService.FirebaseUserChanged += user => Dispatcher.UIThread.Post(() => HandleAuthChanged(user?.LocalId));
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
        InvalidateEligibilityCache();
        await LoadDiscordPostsAsync();
        await LoadItemsAsync();
        await RefreshEligibilityAsync();
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
        SaleStatus = $"Deleted {deletedCount:N0} legendary item{(deletedCount == 1 ? string.Empty : "s")}.";
        return deletedCount;
    }

    public async Task<LegendarySaleEligibility> RefreshEligibilityAsync()
    {
        var version = Interlocked.Increment(ref eligibilityVersion);
        var item = SelectedItem;
        if (item is null || !item.CanPost)
        {
            saleEligibility = null;
            SaleStatus = item is null
                ? "Select a complete legendary item to check Discord posting eligibility."
                : "This item needs a known server, item identity, and legendary details before it can be posted.";
            OnPropertyChanged(nameof(CanSellSelectedItem));
            return new LegendarySaleEligibility(false, "invalid_server", null, null);
        }

        SaleStatus = "Checking linked Discord account...";
        OnPropertyChanged(nameof(CanSellSelectedItem));
        var eligibility = await GetEligibilityAsync(item.Source.AlbionServerId);
        if (version != Interlocked.Read(ref eligibilityVersion) || !ReferenceEquals(item, SelectedItem))
        {
            return eligibility;
        }
        saleEligibility = eligibility;
        SaleStatus = eligibility.Description;
        OnPropertyChanged(nameof(CanSellSelectedItem));
        return eligibility;
    }

    public string DefaultInGameName => !string.IsNullOrWhiteSpace(playerState?.PlayerName)
        ? playerState.PlayerName
        : SelectedItem?.Source.SeenByPlayerName ?? string.Empty;

    public async Task<LegendarySalePostResult> PostToDiscordAsync(string priceSilver, string inGameName)
    {
        var selectedItem = SelectedItem;
        if (selectedItem is null)
        {
            return LegendarySalePostResult.Failed("Select a legendary item first.");
        }
        if (!CanSellSelectedItem)
        {
            var eligibility = await RefreshEligibilityAsync();
            if (!eligibility.CanPost)
            {
                return LegendarySalePostResult.Failed(eligibility.Description, inviteUrl: eligibility.InviteUrl);
            }
        }

        IsPosting = true;
        SaleStatus = "Posting legendary item to AFM Discord...";
        try
        {
            var result = await discordSaleService.PostAsync(selectedItem.Source, priceSilver, inGameName);
            SaleStatus = result.RetryAfterSeconds is { } retry
                ? $"{result.Message} Try again in {TimeSpan.FromSeconds(retry):g}."
                : result.Message;
            if (result.PostedAt is { } postedAt)
            {
                discordPostDates = new Dictionary<(int ServerId, Guid SoulId), DateTimeOffset>(discordPostDates)
                {
                    [(selectedItem.Source.AlbionServerId, selectedItem.Source.SoulId!.Value)] = postedAt
                };
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
    partial void OnSelectedItemChanged(LegendaryItemRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedItem));
        _ = RefreshEligibilityAsync();
    }
    partial void OnIsPostingChanged(bool value) => OnPropertyChanged(nameof(CanSellSelectedItem));

    private async Task InitializeAsync()
    {
        await definitions.InitializeAsync();
        await LoadDiscordPostsAsync();
        await LoadItemsAsync();
    }

    private async Task<LegendarySaleEligibility> GetEligibilityAsync(int serverId)
    {
        var now = DateTimeOffset.UtcNow;
        if (eligibilityCache.TryGetValue(serverId, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Eligibility;
        }

        var generation = eligibilityCacheGeneration;
        var requestKey = (generation, serverId);
        if (!eligibilityRequests.TryGetValue(requestKey, out var request))
        {
            request = discordSaleService.GetEligibilityAsync(serverId);
            eligibilityRequests[requestKey] = request;
        }

        try
        {
            var eligibility = await request;
            if (generation == eligibilityCacheGeneration)
            {
                eligibilityCache[serverId] = new CachedSaleEligibility(
                    eligibility,
                    DateTimeOffset.UtcNow.Add(EligibilityCacheDuration));
            }
            return eligibility;
        }
        finally
        {
            if (eligibilityRequests.TryGetValue(requestKey, out var currentRequest)
                && ReferenceEquals(request, currentRequest))
            {
                eligibilityRequests.Remove(requestKey);
            }
        }
    }

    private void HandleAuthChanged(string? userId)
    {
        if (!string.Equals(eligibilityUserId, userId, StringComparison.Ordinal))
        {
            eligibilityUserId = userId;
            InvalidateEligibilityCache();
        }
        _ = RefreshEligibilityAsync();
    }

    private void InvalidateEligibilityCache()
    {
        eligibilityCache.Clear();
        eligibilityCacheGeneration++;
    }

    private async Task LoadDiscordPostsAsync()
    {
        var latestPosts = new Dictionary<(int ServerId, Guid SoulId), DateTimeOffset>();
        foreach (var post in await discordSaleService.GetPostsAsync())
        {
            if (!Guid.TryParse(post.SoulId, out var soulId) || soulId == Guid.Empty)
            {
                continue;
            }
            var key = (post.ServerId, soulId);
            if (!latestPosts.TryGetValue(key, out var existing) || post.CreatedAt > existing)
            {
                latestPosts[key] = post.CreatedAt;
            }
        }
        discordPostDates = latestPosts;
    }

    private async Task LoadItemsAsync()
    {
        try
        {
            var items = await tracker.GetItemsAsync().ConfigureAwait(false);
            var postDates = discordPostDates;
            var rows = items.Select(item => new LegendaryItemRowViewModel(
                item,
                definitions,
                item.SoulId is { } soulId
                    && postDates.TryGetValue((item.AlbionServerId, soulId), out var postedAt)
                    ? postedAt
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

    private void ApplyFilter()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyFilter);
            return;
        }
        var filter = FilterText.Trim();
        var rows = unfilteredItems.Where(row =>
            (SelectedServer == "Any" || string.Equals(row.ServerName, SelectedServer, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(filter) || row.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }
    }

    private sealed record CachedSaleEligibility(
        LegendarySaleEligibility Eligibility,
        DateTimeOffset ExpiresAt);
}

public sealed class LegendaryItemRowViewModel
{
    public LegendaryItemRowViewModel(
        LegendaryItem source,
        LegendaryDefinitionsService definitions,
        DateTimeOffset? discordPostedAt)
    {
        Source = source;
        DiscordPostedAt = discordPostedAt;
        var itemNameFallback = string.IsNullOrWhiteSpace(source.ItemName) ? source.ItemUniqueName : source.ItemName;
        ItemName = ItemNameFormatter.FormatUsName(
            source.ItemUniqueName,
            definitions.FindItemUsName(source.ItemUniqueName, itemNameFallback));
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
                    CultureInfo.CurrentCulture)))
            .ToList();
        LegendaryRatingValue = definitions.CalculateLegendaryRating(
            source.ItemUniqueName,
            source.Traits.Select(trait => trait.Value));
        SearchText = string.Join(' ', new[]
        {
            ItemName,
            source.SoulName,
            source.ItemUniqueName,
            source.CrafterName,
            source.AttunedToPlayerName,
            source.LocationName,
            source.ContainerName,
            source.SeenByPlayerName,
            LegendaryRating,
            string.Join(' ', Traits.Select(trait => $"{trait.Id} {trait.Name} {trait.Value}"))
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public LegendaryItem Source { get; }
    public IReadOnlyList<LegendaryTraitRowViewModel> Traits { get; }
    public string SearchText { get; }
    public string ItemName { get; }
    public string SoulName => Source.SoulName ?? string.Empty;
    public bool HasSoulName => !string.IsNullOrWhiteSpace(Source.SoulName);
    public string ItemUniqueName => Source.ItemUniqueName;
    public string ServerName => AlbionServers.Get(Source.AlbionServerId)?.Name ?? "Unknown";
    public string Quality => ItemQuality.Format(Source.Quality);
    public int ImageQuality => Source.Quality <= 0 ? 1 : Source.Quality;
    public string AttunedTo => string.IsNullOrWhiteSpace(Source.AttunedToPlayerName)
        ? "Unknown"
        : Source.AttunedToPlayerName;
    public string Attunement => Source.Attunement?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";
    public long? LegendaryRatingValue { get; }
    public string LegendaryRating => LegendaryRatingValue?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";
    public string Strain => Source.Strain?.ToString("N4", CultureInfo.CurrentCulture) ?? "Unknown";
    public string PvPFameGained => Source.PvPFameGained?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";
    public string AttunementSpent => Source.AttunementSpent?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";
    public string TraitsSummary => Traits.Count == 0 ? "No traits" : string.Join("; ", Traits.Select(trait => $"{trait.Value} {trait.Name}"));
    public bool HasTraits => Traits.Count > 0;
    public string Location => string.IsNullOrWhiteSpace(Source.LocationName) ? "Unknown" : Source.LocationName;
    public DateTimeOffset? DiscordPostedAt { get; }
    public string DiscordPost => DiscordPostedAt is { } postedAt
        ? $"{postedAt.ToUniversalTime().ToString("g", CultureInfo.CurrentCulture)} UTC"
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
        Value = calculatedValue.FormattedText;
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
}

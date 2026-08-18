using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Loot.Models;
using AlbionDataAvalonia.Network.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class LootViewModel : ViewModelBase, IDisposable
{
    public const string AllPlayers = "All players";
    private static readonly LootAffiliationFilterOption AllAlliances = new(
        LootAffiliationFilterKind.All,
        "All alliances");
    private static readonly LootAffiliationFilterOption NoAlliance = new(
        LootAffiliationFilterKind.Missing,
        "No alliance");
    private static readonly LootAffiliationFilterOption AllGuilds = new(
        LootAffiliationFilterKind.All,
        "All guilds");
    private static readonly LootAffiliationFilterOption NoGuild = new(
        LootAffiliationFilterKind.Missing,
        "No guild");

    private readonly LootTrackerService? lootTracker;
    private readonly CsvExportService? csvExportService;
    private readonly LatestUiValueDispatcher<LootTrackerSnapshot> snapshotDispatcher;
    private readonly TimeSpan filterDebounceInterval = TimeSpan.FromMilliseconds(250);
    private readonly TimeSpan snapshotRefreshInterval = TimeSpan.FromMilliseconds(100);
    private IDisposable? pendingFilterRefreshRegistration;
    private IReadOnlyList<LootRecord> allRecords = Array.Empty<LootRecord>();
    private List<LootRecord> filteredRecords = new();
    private string appliedFilterText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<LootRowViewModel> loot = new();

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> playerOptions = new([AllPlayers]);

    [ObservableProperty]
    private string? selectedPlayer = AllPlayers;

    [ObservableProperty]
    private ObservableCollection<LootAffiliationFilterOption> allianceOptions = new([AllAlliances]);

    [ObservableProperty]
    private LootAffiliationFilterOption? selectedAlliance = AllAlliances;

    [ObservableProperty]
    private ObservableCollection<LootAffiliationFilterOption> guildOptions = new([AllGuilds]);

    [ObservableProperty]
    private LootAffiliationFilterOption? selectedGuild = AllGuilds;

    [ObservableProperty]
    private bool partyMembersOnly;

    [ObservableProperty]
    private bool isDisabled;

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool showMissingPlayerWarning = true;

    [ObservableProperty]
    private bool isExporting;

    [ObservableProperty]
    private int exportProgress;

    [ObservableProperty]
    private int visiblePickupCount;

    [ObservableProperty]
    private long visibleItemCount;

    [ObservableProperty]
    private long visibleEstimatedMarketValue;

    [ObservableProperty]
    private int visibleMissingEstimatedMarketValueCount;

    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    public LootViewModel()
    {
        snapshotDispatcher = new LatestUiValueDispatcher<LootTrackerSnapshot>(
            ApplySnapshot,
            snapshotRefreshInterval);
    }

    public LootViewModel(LootTrackerService lootTracker, CsvExportService csvExportService)
    {
        snapshotDispatcher = new LatestUiValueDispatcher<LootTrackerSnapshot>(
            ApplySnapshot,
            snapshotRefreshInterval);
        this.lootTracker = lootTracker;
        this.csvExportService = csvExportService;
        lootTracker.SnapshotChanged += OnSnapshotChanged;
        ApplySnapshot(lootTracker.CurrentSnapshot);
    }

    public void Dispose()
    {
        if (lootTracker is not null)
        {
            lootTracker.SnapshotChanged -= OnSnapshotChanged;
        }

        snapshotDispatcher.Dispose();
        CancelPendingFilterRefresh();
    }

    partial void OnFilterTextChanged(string? oldValue, string newValue)
    {
        ScheduleFilterLoot();
    }

    partial void OnSelectedPlayerChanged(string? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyFilter();
    }

    partial void OnSelectedAllianceChanged(LootAffiliationFilterOption? value)
    {
        if (value is null)
        {
            return;
        }

        RefreshGuildOptions();
        ApplyFilter();
    }

    partial void OnSelectedGuildChanged(LootAffiliationFilterOption? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyFilter();
    }

    partial void OnPartyMembersOnlyChanged(bool value)
    {
        ApplyFilter();
    }

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseButtonText));
    }

    [RelayCommand]
    private void TogglePause()
    {
        lootTracker?.SetPaused(!IsPaused);
    }

    public void Clear()
    {
        lootTracker?.Clear();
    }

    public async Task ExportToCsvAsync(
        Stream stream,
        CsvExportOptions options,
        CancellationToken cancellationToken = default)
    {
        if (csvExportService is null)
        {
            return;
        }

        IsExporting = true;
        ExportProgress = 0;
        try
        {
            var exportRecords = filteredRecords.ToArray();
            var progress = new Progress<int>(value => ExportProgress = value);
            await csvExportService.ExportLootToCsvAsync(
                stream,
                exportRecords,
                options,
                progress,
                cancellationToken);
        }
        finally
        {
            IsExporting = false;
        }
    }

    public async Task ExportToViewerAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (csvExportService is null)
        {
            return;
        }

        IsExporting = true;
        ExportProgress = 0;
        try
        {
            var exportRecords = filteredRecords.ToArray();
            var progress = new Progress<int>(value => ExportProgress = value);
            await csvExportService.ExportLootToViewerAsync(
                stream,
                exportRecords,
                progress,
                cancellationToken);
        }
        finally
        {
            IsExporting = false;
        }
    }

    private void OnSnapshotChanged(LootTrackerSnapshot snapshot)
    {
        snapshotDispatcher.Post(snapshot);
    }

    private void ApplySnapshot(LootTrackerSnapshot snapshot)
    {
        IsDisabled = snapshot.IsDisabled;
        IsPaused = snapshot.IsPaused;
        ShowMissingPlayerWarning = !snapshot.HasLocalPlayer;

        allRecords = snapshot.Records;
        RefreshPlayerOptions();
        RefreshAllianceOptions();
        RefreshGuildOptions();

        ApplyFilter();
    }

    private void RefreshPlayerOptions()
    {
        var players = allRecords
            .Select(record => record.PlayerName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Prepend(AllPlayers)
            .ToArray();

        if (PlayerOptions.SequenceEqual(players, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        PlayerOptions = new ObservableCollection<string>(players);
        if (SelectedPlayer is null
            || !players.Contains(SelectedPlayer, StringComparer.OrdinalIgnoreCase))
        {
            SelectedPlayer = AllPlayers;
        }
    }

    private void RefreshAllianceOptions()
    {
        var options = allRecords
            .Select(record => record.PlayerAllianceName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new LootAffiliationFilterOption(
                LootAffiliationFilterKind.Named,
                name,
                name))
            .Prepend(NoAlliance)
            .Prepend(AllAlliances)
            .ToArray();

        if (!allRecords.Any(record => string.IsNullOrWhiteSpace(record.PlayerAllianceName)))
        {
            options = options.Where(option => option.Kind != LootAffiliationFilterKind.Missing).ToArray();
        }

        options = options
            .Select(option => AllianceOptions.FirstOrDefault(existing => existing == option) ?? option)
            .ToArray();

        if (!AllianceOptions.SequenceEqual(options))
        {
            AllianceOptions = new ObservableCollection<LootAffiliationFilterOption>(options);
        }

        if (SelectedAlliance is null || !options.Contains(SelectedAlliance))
        {
            SelectedAlliance = AllAlliances;
        }
    }

    private void RefreshGuildOptions()
    {
        var recordsForAlliance = allRecords.Where(record =>
            MatchesAffiliation(record.PlayerAllianceName, SelectedAlliance));
        var records = recordsForAlliance.ToArray();
        var options = records
            .Select(record => record.PlayerGuildName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new LootAffiliationFilterOption(
                LootAffiliationFilterKind.Named,
                name,
                name))
            .Prepend(NoGuild)
            .Prepend(AllGuilds)
            .ToArray();

        if (!records.Any(record => string.IsNullOrWhiteSpace(record.PlayerGuildName)))
        {
            options = options.Where(option => option.Kind != LootAffiliationFilterKind.Missing).ToArray();
        }

        options = options
            .Select(option => GuildOptions.FirstOrDefault(existing => existing == option) ?? option)
            .ToArray();

        if (!GuildOptions.SequenceEqual(options))
        {
            GuildOptions = new ObservableCollection<LootAffiliationFilterOption>(options);
        }

        if (SelectedGuild is null || !options.Contains(SelectedGuild))
        {
            SelectedGuild = AllGuilds;
        }
    }

    private void ApplyFilter()
    {
        appliedFilterText = FilterText ?? string.Empty;
        IEnumerable<LootRecord> query = allRecords;
        if (!string.IsNullOrWhiteSpace(SelectedPlayer)
            && !string.Equals(SelectedPlayer, AllPlayers, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(record =>
                string.Equals(record.PlayerName, SelectedPlayer, StringComparison.OrdinalIgnoreCase));
        }

        query = query.Where(record =>
            MatchesAffiliation(record.PlayerAllianceName, SelectedAlliance)
            && MatchesAffiliation(record.PlayerGuildName, SelectedGuild));

        if (PartyMembersOnly)
        {
            query = query.Where(record => record.WasPartyMemberAtPickup == true);
        }

        var normalizedFilterText = NormalizeItemSearchText(appliedFilterText);
        if (!string.IsNullOrEmpty(normalizedFilterText))
        {
            query = query.Where(record =>
                NormalizeItemSearchText(record.ItemName)
                    .Contains(normalizedFilterText, StringComparison.OrdinalIgnoreCase));
        }

        filteredRecords = query
            .OrderByDescending(record => record.PickedUpAtUtc)
            .ToList();
        Loot = new ObservableCollection<LootRowViewModel>(
            filteredRecords.Select(record => new LootRowViewModel(record)));
        VisiblePickupCount = filteredRecords.Count;
        VisibleItemCount = filteredRecords.Sum(record => (long)record.Amount);
        VisibleEstimatedMarketValue = filteredRecords.Sum(record => record.TotalEstimatedMarketValue ?? 0);
        VisibleMissingEstimatedMarketValueCount = filteredRecords.Count(record =>
            record.TotalEstimatedMarketValue is null);
    }

    private void ScheduleFilterLoot()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ScheduleFilterLoot);
            return;
        }

        CancelPendingFilterRefresh();
        pendingFilterRefreshRegistration = DispatcherTimer.RunOnce(() =>
        {
            pendingFilterRefreshRegistration = null;
            ApplyFilter();
        }, filterDebounceInterval);
    }

    private void CancelPendingFilterRefresh()
    {
        pendingFilterRefreshRegistration?.Dispose();
        pendingFilterRefreshRegistration = null;
    }

    private static string NormalizeItemSearchText(string? value)
    {
        return (value ?? string.Empty).Replace(" ", string.Empty);
    }

    private static bool MatchesAffiliation(
        string affiliationName,
        LootAffiliationFilterOption? option)
    {
        if (option is null)
        {
            return true;
        }

        return option.Kind switch
        {
            LootAffiliationFilterKind.All => true,
            LootAffiliationFilterKind.Missing => string.IsNullOrWhiteSpace(affiliationName),
            LootAffiliationFilterKind.Named => string.Equals(
                affiliationName,
                option.Value,
                StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}

public sealed class LootRowViewModel
{
    public LootRowViewModel(LootRecord source)
    {
        Source = source;
    }

    public LootRecord Source { get; }
    public DateTime PickedUpAt => Source.PickedUpAtUtc.ToLocalTime();
    public string PlayerName => Source.PlayerName;
    public string PlayerAllianceName => FormatAffiliation(Source.PlayerAllianceName);
    public string PlayerGuildName => FormatAffiliation(Source.PlayerGuildName);
    public string PartyText => Source.WasPartyMemberAtPickup switch
    {
        true => "Yes",
        false => "No",
        _ => "Unknown"
    };
    public string SourceKind => Source.SourceKind.ToString();
    public string SourceName => Source.SourceName;
    public string LocationName => Source.LocationName;
    public string ItemUniqueName => Source.ItemUniqueName;
    public string ItemName => Source.ItemName;
    public int ImageQuality => Source.Quality ?? 1;
    public string QualityText => ItemQuality.Format(Source.Quality);
    public long Amount => Source.Amount;
    public long? EstimatedMarketValue => Source.EstimatedMarketValue;
    public long? TotalEstimatedMarketValue => Source.TotalEstimatedMarketValue;

    private static string FormatAffiliation(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }
}

public enum LootAffiliationFilterKind
{
    All,
    Missing,
    Named
}

public sealed record LootAffiliationFilterOption(
    LootAffiliationFilterKind Kind,
    string DisplayName,
    string? Value = null)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

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

    private readonly LootTrackerService? lootTracker;
    private readonly CsvExportService? csvExportService;
    private IReadOnlyList<LootRecord> allRecords = Array.Empty<LootRecord>();
    private IReadOnlyList<LootRecord> filteredRecords = Array.Empty<LootRecord>();

    [ObservableProperty]
    private ObservableCollection<LootRowViewModel> loot = new();

    [ObservableProperty]
    private ObservableCollection<string> playerOptions = new([AllPlayers]);

    [ObservableProperty]
    private string selectedPlayer = AllPlayers;

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
    }

    public LootViewModel(LootTrackerService lootTracker, CsvExportService csvExportService)
    {
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
    }

    partial void OnSelectedPlayerChanged(string value)
    {
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

    private void OnSnapshotChanged(LootTrackerSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(LootTrackerSnapshot snapshot)
    {
        IsDisabled = snapshot.IsDisabled;
        IsPaused = snapshot.IsPaused;
        ShowMissingPlayerWarning = !snapshot.HasLocalPlayer;
        allRecords = snapshot.Records;
        RefreshPlayerOptions();
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
        if (!players.Contains(SelectedPlayer, StringComparer.OrdinalIgnoreCase))
        {
            SelectedPlayer = AllPlayers;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<LootRecord> query = allRecords;
        if (!string.Equals(SelectedPlayer, AllPlayers, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(record =>
                string.Equals(record.PlayerName, SelectedPlayer, StringComparison.OrdinalIgnoreCase));
        }

        if (PartyMembersOnly)
        {
            query = query.Where(record => record.WasPartyMemberAtPickup);
        }

        filteredRecords = query
            .OrderByDescending(record => record.PickedUpAtUtc)
            .ToArray();
        Loot = new ObservableCollection<LootRowViewModel>(
            filteredRecords.Select(record => new LootRowViewModel(record)));
        VisiblePickupCount = filteredRecords.Count;
        VisibleItemCount = filteredRecords.Sum(record => (long)record.Amount);
        VisibleEstimatedMarketValue = filteredRecords.Sum(record => record.TotalEstimatedMarketValue ?? 0);
        VisibleMissingEstimatedMarketValueCount = filteredRecords.Count(record =>
            record.TotalEstimatedMarketValue is null);
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
    public bool WasPartyMemberAtPickup => Source.WasPartyMemberAtPickup;
    public string SourceKind => Source.SourceKind.ToString();
    public string SourceName => Source.SourceName;
    public string ItemUniqueName => Source.ItemUniqueName;
    public string ItemName => Source.ItemName;
    public int Quality => Source.Quality;
    public int Amount => Source.Amount;
    public long? EstimatedMarketValue => Source.EstimatedMarketValue;
    public long? TotalEstimatedMarketValue => Source.TotalEstimatedMarketValue;
}

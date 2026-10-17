using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Ui;
using AlbionDataAvalonia.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public sealed partial class DataCleanupRowViewModel(DataCleanupType type, string label) : ObservableObject
{
    public DataCleanupType Type { get; } = type;
    public string Label { get; } = label;
    public bool IsActivity => Type == DataCleanupType.Activity;
    public long Bytes { get; set; }
    public long Rows { get; set; }

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string sizeText = UiText.None;
    [ObservableProperty] private string countText = "";
}

/// <summary>A legend entry; a null kind is the rest of the database.</summary>
public sealed record StorageLegendItem(string Label, string SizeText, DataCleanupType? Kind);

public partial class SettingsViewModel
{
    public const int DataTabIndex = 4;

    private readonly DataCleanupService? dataCleanup;
    private CancellationTokenSource cleanupPreviewCancellation = new();
    private DataStorageSummary? storage;

    public IReadOnlyList<DataCleanupRowViewModel> CleanupRows { get; } =
    [
        new(DataCleanupType.Activity, "Activity"),
        new(DataCleanupType.Trades, "Trades"),
        new(DataCleanupType.Mails, "Mail records"),
        new(DataCleanupType.AwakenedItems, "Awakened items")
    ];

    // Zero days means everything; the open activity session is still kept.
    public IReadOnlyList<Option<int>> CleanupAgeOptions { get; } =
        [new(30, "30 days"), new(90, "90 days"), new(180, "6 months"), new(365, "1 year"), new(0, "Everything")];

    [ObservableProperty] private int selectedCleanupAgeIndex = 1;
    [ObservableProperty] private bool removeOutsidePartyLoot;
    [ObservableProperty] private bool compactAfterCleanup = true;
    [ObservableProperty] private bool isMeasuringStorage;
    [ObservableProperty] private string databaseSizeText = "Measuring…";
    [ObservableProperty] private string storageNoteText = "";
    [ObservableProperty] private IReadOnlyList<StorageSegment> storageSegments = [];
    [ObservableProperty] private IReadOnlyList<StorageLegendItem> storageLegend = [];
    [ObservableProperty] private string cleanupPreviewText = "Select what to remove.";
    [ObservableProperty] private string cleanupStatusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteData))]
    private DataCleanupPreview? cleanupPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteData))]
    private bool isCleanupBusy;

    public bool CanDeleteData => CleanupPreview?.HasData == true && !IsCleanupBusy;
    public bool ShowPartyLootOption => CleanupRows[0].IsSelected;

    private void InitializeCleanup()
    {
        foreach (var row in CleanupRows)
        {
            row.PropertyChanged += OnCleanupRowPropertyChanged;
        }
    }

    private void OnCleanupRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DataCleanupRowViewModel.IsSelected)) return;
        OnPropertyChanged(nameof(ShowPartyLootOption));
        _ = UpdateCleanupPreviewAsync();
    }

    partial void OnSelectedCleanupAgeIndexChanged(int value) => _ = UpdateCleanupPreviewAsync();
    partial void OnRemoveOutsidePartyLootChanged(bool value) => _ = UpdateCleanupPreviewAsync();
    partial void OnCompactAfterCleanupChanged(bool value) => UpdateCleanupPreviewText();

    [RelayCommand]
    private Task RefreshStorage() => RefreshStorageAsync();

    public async Task RefreshStorageAsync()
    {
        if (dataCleanup is null || IsMeasuringStorage) return;
        IsMeasuringStorage = true;
        try
        {
            storage = await dataCleanup.GetStorageAsync();
            foreach (var row in CleanupRows)
            {
                var measured = storage.Rows.First(item => item.Type == row.Type);
                row.Bytes = measured.Bytes;
                row.Rows = measured.Rows;
                row.SizeText = UiFormat.Bytes(measured.Bytes);
                row.CountText = measured.Type switch
                {
                    DataCleanupType.Activity => $"{UiFormat.Count(measured.Sessions ?? 0, "session")} · {UiFormat.Count(measured.Rows, "event")}",
                    DataCleanupType.Trades => UiFormat.Count(measured.Rows, "trade"),
                    DataCleanupType.Mails => UiFormat.Count(measured.Rows, "mail"),
                    _ => UiFormat.Count(measured.Rows, "item")
                };
            }

            DatabaseSizeText = $"Database {UiFormat.Bytes(storage.DatabaseBytes)}";
            StorageNoteText = $"Backups {UiFormat.Bytes(storage.BackupBytes)}" + (storage.IsEstimated ? " · sizes are estimates" : "");
            StorageSegments = CleanupRows.Select(row => new StorageSegment(row.Bytes, row.Type))
                .Append(new StorageSegment(storage.OtherBytes, null)).ToArray();
            StorageLegend = CleanupRows.Select(row => new StorageLegendItem(row.Label, row.SizeText, row.Type))
                .Append(new StorageLegendItem("Other", UiFormat.Bytes(storage.OtherBytes), null)).ToArray();
        }
        catch (Exception error)
        {
            Log.Warning(error, "Could not measure stored data.");
            DatabaseSizeText = "Could not measure the database";
        }
        finally
        {
            IsMeasuringStorage = false;
        }

        await UpdateCleanupPreviewAsync();
    }

    private async Task UpdateCleanupPreviewAsync()
    {
        cleanupPreviewCancellation.Cancel();
        cleanupPreviewCancellation = new CancellationTokenSource();
        var token = cleanupPreviewCancellation.Token;
        CleanupPreview = null;
        CleanupStatusText = "";

        var types = CleanupRows.Where(row => row.IsSelected).Select(row => row.Type).ToHashSet();
        if (dataCleanup is null || types.Count == 0)
        {
            CleanupPreviewText = "Select what to remove.";
            return;
        }

        CleanupPreviewText = "Checking…";
        var days = CleanupAgeOptions[Math.Clamp(SelectedCleanupAgeIndex, 0, CleanupAgeOptions.Count - 1)].Value;
        var cutoff = days == 0 ? DateTime.UtcNow : DateTime.UtcNow.AddDays(-days);
        try
        {
            var preview = await dataCleanup.PreviewAsync(new DataCleanupRequest(cutoff, types, RemoveOutsidePartyLoot), token);
            if (token.IsCancellationRequested) return;
            CleanupPreview = preview;
            UpdateCleanupPreviewText();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Log.Warning(error, "Could not preview data cleanup.");
            CleanupPreviewText = "Could not check stored data. Try again.";
        }
    }

    private void UpdateCleanupPreviewText()
    {
        if (CleanupPreview is not { } preview) return;
        var age = CleanupAgeOptions[Math.Clamp(SelectedCleanupAgeIndex, 0, CleanupAgeOptions.Count - 1)];
        if (!preview.HasData)
        {
            CleanupPreviewText = age.Value == 0 ? "Nothing to remove." : $"Nothing older than {age.Label} to remove.";
            return;
        }

        var scope = age.Value == 0 ? "Removes all records from" : $"Removes records older than {age.Label} from";
        var types = string.Join(", ", CleanupRows.Where(row => row.IsSelected).Select(row => row.Label.ToLowerInvariant()));
        var freed = EstimateFreedBytes(preview);
        CleanupPreviewText = freed > 0
            ? $"{scope} {types}. Frees about {UiFormat.Bytes(freed)}{(CompactAfterCleanup ? "" : " once the database is compacted")}."
            : $"{scope} {types}.";
    }

    private long EstimateFreedBytes(DataCleanupPreview preview)
    {
        double Share(DataCleanupType type, long removed)
        {
            var row = CleanupRows.First(item => item.Type == type);
            return row.Rows > 0 ? row.Bytes * Math.Min(1d, removed / (double)row.Rows) : 0;
        }

        return (long)(Share(DataCleanupType.Activity, preview.Activity?.Events ?? 0)
            + Share(DataCleanupType.Trades, preview.Trades)
            + Share(DataCleanupType.Mails, preview.Mails)
            + Share(DataCleanupType.AwakenedItems, preview.AwakenedItems));
    }

    public string CleanupConfirmationText()
    {
        if (CleanupPreview is not { } preview) return "";
        var parts = new List<string>();
        if (preview.Activity?.HasData == true) parts.Add($"{UiFormat.Count(preview.Activity.Sessions, "activity session")} and {UiFormat.Count(preview.Activity.Events, "event")}");
        if (preview.Trades > 0) parts.Add(UiFormat.Count(preview.Trades, "trade"));
        if (preview.Mails > 0) parts.Add(UiFormat.Count(preview.Mails, "mail record"));
        if (preview.AwakenedItems > 0) parts.Add(UiFormat.Count(preview.AwakenedItems, "awakened item"));
        var kept = preview.Activity is not null ? " The current activity session is kept." : "";
        var list = parts.Count > 1 ? $"{string.Join(", ", parts[..^1])} and {parts[^1]}" : parts.FirstOrDefault() ?? "";
        return $"Permanently delete {list} across all characters and servers?{kept} This cannot be undone.";
    }

    [RelayCommand]
    private async Task DeleteData()
    {
        try
        {
            if (dialogs is null || !CanDeleteData) return;
            if (await dialogs.ConfirmAsync("Delete stored data?", CleanupConfirmationText(), "Delete")) await ApplyCleanupAsync();
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not delete the selected stored data.");
        }
    }

    public async Task ApplyCleanupAsync()
    {
        if (dataCleanup is null || CleanupPreview is not { HasData: true } preview || IsCleanupBusy) return;
        IsCleanupBusy = true;
        CleanupStatusText = CompactAfterCleanup ? "Deleting and compacting. This can take a minute…" : "Deleting…";
        try
        {
            var deleted = await dataCleanup.CleanupAsync(preview, CompactAfterCleanup);
            var removed = (deleted.Activity?.Events ?? 0) + deleted.Trades + deleted.Mails + deleted.AwakenedItems;
            CleanupStatusText = $"Deleted {UiFormat.Count(removed, "record")}{(CompactAfterCleanup ? " and compacted the database" : "")}.";
        }
        catch (Exception error)
        {
            Log.Error(error, "Data cleanup failed.");
            CleanupStatusText = "Cleanup failed partway. Check what is left below, then try again.";
        }
        finally
        {
            IsCleanupBusy = false;
        }

        var status = CleanupStatusText;
        foreach (var row in CleanupRows) row.IsSelected = false;
        await RefreshStorageAsync();
        CleanupStatusText = status;
    }

}

using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Views;
using Avalonia.Media;
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

public sealed partial class DataCleanupRowViewModel(DataCleanupType type, string label, IBrush brush) : ObservableObject
{
    public DataCleanupType Type { get; } = type;
    public string Label { get; } = label;
    public IBrush Brush { get; } = brush;
    public bool IsActivity => Type == DataCleanupType.Activity;
    public long Bytes { get; set; }
    public long Rows { get; set; }

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string sizeText = "—";
    [ObservableProperty] private string countText = "";
}

public sealed record StorageLegendItem(string Label, string SizeText, IBrush Brush);

public partial class SettingsViewModel
{
    public const int DataTabIndex = 4;
    private static readonly IBrush OtherStorageBrush = new SolidColorBrush(Color.Parse("#40FFFFFF"));

    private readonly DataCleanupService? dataCleanup;
    private CancellationTokenSource cleanupPreviewCancellation = new();
    private DataStorageSummary? storage;

    public IReadOnlyList<DataCleanupRowViewModel> CleanupRows { get; } =
    [
        new(DataCleanupType.Activity, "Activity", new SolidColorBrush(Color.Parse("#FFA726"))),
        new(DataCleanupType.Trades, "Trades", new SolidColorBrush(Color.Parse("#64B5F6"))),
        new(DataCleanupType.Mails, "Mail records", new SolidColorBrush(Color.Parse("#BA68C8"))),
        new(DataCleanupType.AwakenedItems, "Awakened items", new SolidColorBrush(Color.Parse("#81C784")))
    ];

    // Zero days means everything; the open activity session is still kept.
    public IReadOnlyList<ActivityOption<int>> CleanupAgeOptions { get; } =
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
                row.SizeText = FormatBytes(measured.Bytes);
                row.CountText = measured.CountText;
            }

            DatabaseSizeText = $"Database {FormatBytes(storage.DatabaseBytes)}";
            StorageNoteText = $"Backups {FormatBytes(storage.BackupBytes)}" + (storage.IsEstimated ? " · sizes are estimates" : "");
            StorageSegments = CleanupRows.Select(row => new StorageSegment(row.Bytes, row.Brush))
                .Append(new StorageSegment(storage.OtherBytes, OtherStorageBrush)).ToArray();
            StorageLegend = CleanupRows.Select(row => new StorageLegendItem(row.Label, row.SizeText, row.Brush))
                .Append(new StorageLegendItem("Other", FormatBytes(storage.OtherBytes), OtherStorageBrush)).ToArray();
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
            ? $"{scope} {types}. Frees about {FormatBytes(freed)}{(CompactAfterCleanup ? "" : " once the database is compacted")}."
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
        if (preview.Activity?.HasData == true) parts.Add($"{preview.Activity.Sessions:N0} activity sessions and {preview.Activity.Events:N0} events");
        if (preview.Trades > 0) parts.Add($"{preview.Trades:N0} trades");
        if (preview.Mails > 0) parts.Add($"{preview.Mails:N0} mail records");
        if (preview.AwakenedItems > 0) parts.Add($"{preview.AwakenedItems:N0} awakened items");
        var kept = preview.Activity is not null ? " The current activity session is kept." : "";
        var list = parts.Count > 1 ? $"{string.Join(", ", parts[..^1])} and {parts[^1]}" : parts.FirstOrDefault() ?? "";
        return $"Permanently delete {list} across all characters and servers?{kept} This cannot be undone.";
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
            CleanupStatusText = $"Deleted {removed:N0} records{(CompactAfterCleanup ? " and compacted the database" : "")}.";
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

    private static string FormatBytes(long bytes) => bytes < 1024 ? $"{bytes:N0} B" : bytes < 1024L * 1024 ?
        $"{bytes / 1024d:N1} KB" : bytes < 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024):N1} MB" : $"{bytes / (1024d * 1024 * 1024):N1} GB";
}

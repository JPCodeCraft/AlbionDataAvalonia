using AlbionDataAvalonia.Activity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    private CancellationTokenSource cleanupQueryCancellation = new();
    private long cleanupReadVersion = -1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewCleanup))]
    private bool cleanupDeleteOld = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewCleanup))]
    private int cleanupOlderThanDays = 30;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewCleanup))]
    private bool cleanupDeleteOutsidePartyLoot;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewCleanup))]
    [NotifyPropertyChangedFor(nameof(CanApplyCleanup))]
    private bool isCleanupBusy;
    [ObservableProperty] private string cleanupStatusText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CleanupActivitySizeText))]
    [NotifyPropertyChangedFor(nameof(CleanupDatabaseSizeText))]
    [NotifyPropertyChangedFor(nameof(CleanupSessionsText))]
    [NotifyPropertyChangedFor(nameof(CleanupEncountersText))]
    [NotifyPropertyChangedFor(nameof(CleanupEventsText))]
    [NotifyPropertyChangedFor(nameof(CleanupItemsText))]
    private ActivityStorageSummary? cleanupStorage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCleanupPreview))]
    [NotifyPropertyChangedFor(nameof(CanApplyCleanup))]
    [NotifyPropertyChangedFor(nameof(CleanupPreviewText))]
    private ActivityCleanupPreview? cleanupPreview;

    public bool CanPreviewCleanup => !isDisposed && persistence is not null && !IsCleanupBusy &&
        (CleanupDeleteOld || CleanupDeleteOutsidePartyLoot) && (!CleanupDeleteOld || CleanupOlderThanDays is >= 1 and <= 36500);
    public bool CanApplyCleanup => CanPreviewCleanup && CleanupPreview is { HasData: true };
    public bool HasCleanupPreview => CleanupPreview is not null;
    public string CleanupActivitySizeText => CleanupStorage is { } value ?
        (value.IsActivitySizeEstimated ? "About " : "") + StorageSize(value.ActivityBytes) : "—";
    public string CleanupDatabaseSizeText => CleanupStorage is { } value ? StorageSize(value.DatabaseBytes) : "—";
    public string CleanupSessionsText => CleanupStorage?.Sessions.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    public string CleanupEncountersText => CleanupStorage?.Encounters.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    public string CleanupEventsText => CleanupStorage?.Events.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    public string CleanupItemsText => CleanupStorage?.ItemPickups.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    public string CleanupPreviewText => CleanupPreview is not { } value ? "" : value.HasData ?
        $"{value.Sessions:N0} sessions · {value.Encounters:N0} encounters · {value.Events:N0} events · {value.ItemPickups:N0} item pickups" :
        "Nothing matches these options.";

    public async Task RefreshCleanupAsync(bool force = false)
    {
        if (persistence is null || isDisposed || IsCleanupBusy || !force && cleanupReadVersion >= 0) return;
        var token = BeginRead(ref cleanupQueryCancellation);
        IsCleanupBusy = true;
        try
        {
            var version = persistence.CurrentReadVersion;
            var storage = await persistence.GetStorageSummaryAsync(token);
            token.ThrowIfCancellationRequested();
            if (isDisposed) return;
            CleanupStorage = storage;
            cleanupReadVersion = version;
        }
        finally { if (!isDisposed) IsCleanupBusy = false; }
    }

    public async Task<ActivityCleanupPreview?> PrepareCleanupAsync()
    {
        if (!CanPreviewCleanup || persistence is null) return null;
        var days = CleanupOlderThanDays;
        var old = CleanupDeleteOld;
        var outsideParty = CleanupDeleteOutsidePartyLoot;
        var protectedId = tracker?.CurrentSessionId;
        var request = new ActivityCleanupRequest(old ? DateTime.UtcNow.AddDays(-days) : null, outsideParty, protectedId);
        var token = BeginRead(ref cleanupQueryCancellation);
        IsCleanupBusy = true;
        CleanupPreview = null;
        CleanupStatusText = "";
        try
        {
            var preview = await persistence.PreviewCleanupAsync(request, token);
            token.ThrowIfCancellationRequested();
            if (isDisposed || CleanupOlderThanDays != days || CleanupDeleteOld != old || CleanupDeleteOutsidePartyLoot != outsideParty) return null;
            CleanupPreview = preview;
            return preview;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception error)
        {
            Log.Error(error, "Failed to preview activity cleanup.");
            if (!isDisposed) CleanupStatusText = "Could not check activity. Please try again.";
            return null;
        }
        finally { if (!isDisposed) IsCleanupBusy = false; }
    }

    public async Task ApplyCleanupAsync(ActivityCleanupPreview preview)
    {
        if (!CanApplyCleanup || persistence is null || !ReferenceEquals(preview, CleanupPreview)) return;
        IsCleanupBusy = true;
        try
        {
            var result = await persistence.CleanupAsync(preview.Request);
            if (isDisposed) return;
            if (SelectedSession is { } selected && result.DeletedSessionIds.Contains(selected.Id)) SelectedSession = null;
            if (SelectedPlaySession is { } playSession && playSession.Runs.All(run => result.DeletedSessionIds.Contains(run.Id))) SelectedPlaySession = null;
            ++historyChangeVersion; ++detailChangeVersion; ++itemChangeVersion;
            CleanupPreview = null;
            CleanupStatusText = $"Deleted {result.Deleted.Sessions:N0} sessions and {result.Deleted.Events:N0} events.";
            cleanupReadVersion = -1;
        }
        catch (Exception error)
        {
            Log.Error(error, "Failed to clean up activity.");
            if (!isDisposed) CleanupStatusText = "Cleanup failed. Please try again.";
            return;
        }
        finally { if (!isDisposed) IsCleanupBusy = false; }
        await RefreshCleanupAsync(true);
    }

    public static string CleanupConfirmationText(ActivityCleanupPreview preview) =>
        $"Permanently delete {preview.Sessions:N0} sessions and {preview.Events:N0} events across all characters and servers? " +
        "Current sessions are kept. This cannot be undone.";

    [RelayCommand] private Task RefreshCleanup() => RunSafelyAsync(() => RefreshCleanupAsync(true));
    [RelayCommand] private Task PreviewCleanup() => RunSafelyAsync(async () => { await PrepareCleanupAsync(); });
    partial void OnCleanupDeleteOldChanged(bool value) => CleanupOptionsChanged();
    partial void OnCleanupOlderThanDaysChanged(int value) => CleanupOptionsChanged();
    partial void OnCleanupDeleteOutsidePartyLootChanged(bool value) => CleanupOptionsChanged();
    private void CleanupOptionsChanged() { cleanupQueryCancellation.Cancel(); CleanupPreview = null; CleanupStatusText = ""; }
    private void DisposeCleanup() { cleanupQueryCancellation.Cancel(); cleanupQueryCancellation.Dispose(); }

    private static string StorageSize(long bytes) => bytes < 1024 ? $"{bytes:N0} B" : bytes < 1024L * 1024 ?
        $"{bytes / 1024d:N1} KB" : bytes < 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024):N1} MB" : $"{bytes / (1024d * 1024 * 1024):N1} GB";
}

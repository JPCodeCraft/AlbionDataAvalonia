using AlbionDataAvalonia.Activity;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;
using static AlbionDataAvalonia.ViewModels.ActivityReads;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityCurrentViewModel
{
    private const string LiveTimelineReadError = "Could not update the chart. Retrying…";
    private CancellationTokenSource liveTimelineCancellation = new();
    private bool liveTimelineRefreshing;
    private bool liveTimelineReadFailed;

    [ObservableProperty] private ActivityLiveTimelineSnapshot? currentLiveTimeline;
    [ObservableProperty] private int liveTimelineMinutes = 5;

    partial void OnLiveTimelineMinutesChanged(int value)
    {
        if (value is not (5 or 15 or 30 or 60 or 120)) { LiveTimelineMinutes = 5; return; }
        liveTimelineCancellation.Cancel();
        CurrentLiveTimeline = null;
        if (page.IsShowing(ActivityTab.Current)) _ = RefreshLiveTimelineSafelyAsync();
    }

    private async Task RefreshLiveTimelineSafelyAsync()
    {
        if (Tracker is not { } tracker || !page.IsShowing(ActivityTab.Current) || liveTimelineRefreshing) return;
        var key = GetCurrentPeriodKey();
        var categories = Filters.VisibleCategories;
        var windowMinutes = LiveTimelineMinutes;
        var query = CurrentPeriodQuery(DateTime.UtcNow, true);
        if (key is null || query is null)
        {
            CurrentLiveTimeline = null;
            return;
        }

        liveTimelineRefreshing = true;
        var cancellationToken = BeginRead(ref liveTimelineCancellation);
        try
        {
            var snapshot = await tracker.GetLiveTimelineAsync(query, DateTime.UtcNow, cancellationToken, windowMinutes * 60);
            cancellationToken.ThrowIfCancellationRequested();
            if (!page.IsShowing(ActivityTab.Current) || GetCurrentPeriodKey() != key || Filters.VisibleCategories != categories || LiveTimelineMinutes != windowMinutes) return;
            CurrentLiveTimeline = snapshot with { ContextKey = $"{snapshot.ContextKey}|{key.Mode}|{key.Hours}|{windowMinutes}" };
            if (liveTimelineReadFailed && page.ErrorText == LiveTimelineReadError) page.ErrorText = "";
            liveTimelineReadFailed = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!liveTimelineReadFailed) Log.Warning(exception, "Unable to refresh the live activity timeline");
            liveTimelineReadFailed = true;
            if (!page.IsDisposed) page.ErrorText = LiveTimelineReadError;
        }
        finally { liveTimelineRefreshing = false; }
    }
}

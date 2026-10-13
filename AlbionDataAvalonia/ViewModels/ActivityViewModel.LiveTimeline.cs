using AlbionDataAvalonia.Activity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    private const string LiveTimelineReadError = "Could not update the chart. Retrying…";
    private CancellationTokenSource liveTimelineCancellation = new();
    private bool liveTimelineRefreshing;
    private bool liveTimelineReadFailed;

    [ObservableProperty] private ActivityLiveTimelineSnapshot? currentLiveTimeline;
    [ObservableProperty] private ActivityLiveTimelineSnapshot? historyLiveTimeline;
    [ObservableProperty] private int liveTimelineMinutes = 5;

    partial void OnLiveTimelineMinutesChanged(int value)
    {
        if (value is not (5 or 10 or 15)) { LiveTimelineMinutes = 5; return; }
        liveTimelineCancellation.Cancel();
        CurrentLiveTimeline = null;
        if (isActive && SelectedTabIndex == 0) _ = RefreshLiveTimelineSafelyAsync();
    }

    private async Task RefreshLiveTimelineSafelyAsync()
    {
        if (tracker is null || !isActive || SelectedTabIndex != 0 || liveTimelineRefreshing || isDisposed) return;
        var key = GetCurrentPeriodKey();
        var categories = VisibleCategories;
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
            if (isDisposed || !isActive || SelectedTabIndex != 0 || GetCurrentPeriodKey() != key || VisibleCategories != categories || LiveTimelineMinutes != windowMinutes) return;
            CurrentLiveTimeline = snapshot with { ContextKey = $"{snapshot.ContextKey}|{key.Mode}|{key.Hours}|{windowMinutes}" };
            if (liveTimelineReadFailed && ErrorText == LiveTimelineReadError) ErrorText = "";
            liveTimelineReadFailed = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!liveTimelineReadFailed) Log.Warning(exception, "Unable to refresh the live activity timeline");
            liveTimelineReadFailed = true;
            if (!isDisposed) ErrorText = LiveTimelineReadError;
        }
        finally { liveTimelineRefreshing = false; }
    }

    [RelayCommand]
    private async Task OpenTimelineEncounterAsync(Guid encounterId)
    {
        if (persistence is null) return;
        await RunSafelyAsync(async () =>
        {
            if (await persistence.GetEncounterAsync(encounterId) is { } encounter)
                await OpenSessionDetailsAsync(encounter.SessionId, encounter.Id);
        });
    }
}

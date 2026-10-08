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
    private const string LiveTimelineReadError = "Could not refresh the live timeline. Recording continues; the view will retry.";
    private CancellationTokenSource liveTimelineCancellation = new();
    private bool liveTimelineRefreshing;
    private bool liveTimelineReadFailed;

    [ObservableProperty] private ActivityLiveTimelineSnapshot? currentLiveTimeline;

    private async Task RefreshLiveTimelineSafelyAsync()
    {
        if (tracker is null || !isActive || SelectedTabIndex != 0 || liveTimelineRefreshing || isDisposed) return;
        var sessionId = tracker.CurrentSessionId;
        var scope = SelectedScope.Value;
        var categories = VisibleCategories;
        if (sessionId is null)
        {
            CurrentLiveTimeline = null;
            return;
        }

        liveTimelineRefreshing = true;
        var cancellationToken = BeginRead(ref liveTimelineCancellation);
        try
        {
            var snapshot = await tracker.GetLiveTimelineAsync(sessionId, scope, DateTime.UtcNow, cancellationToken, categories);
            cancellationToken.ThrowIfCancellationRequested();
            if (isDisposed || SelectedTabIndex != 0 || tracker.CurrentSessionId != sessionId || SelectedScope.Value != scope || VisibleCategories != categories) return;
            CurrentLiveTimeline = snapshot;
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

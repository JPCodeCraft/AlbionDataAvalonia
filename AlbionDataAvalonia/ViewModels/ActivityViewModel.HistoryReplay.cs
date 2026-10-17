using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    private const string ReplayReadError = "Could not load replay. Try again.";
    private CancellationTokenSource historyReplayCancellation = new();
    private long historyReplayReadVersion;

    [ObservableProperty] private ActivityLiveTimelineSnapshot? historyReplayTimeline;

    private static string HistoryTimelineContextKey(ActivityQuery query, ActivityCategories categories) =>
        $"history:{query.PlaySessionId}:{query.SessionId}:{query.EncounterId}:{query.Scope}:{query.ActorKey}:{categories}";

    partial void OnHistoryLiveTimelineChanged(ActivityLiveTimelineSnapshot? oldValue, ActivityLiveTimelineSnapshot? newValue)
    {
        if (oldValue?.ContextKey == newValue?.ContextKey) return;
        CancelHistoryReplayRead();
        HistoryReplayTimeline = null;
    }

    [RelayCommand]
    private async Task LoadHistoryReplayAsync(ActivityTimelineReplayRequest request)
    {
        if (request is null || persistence is null || isDisposed || !isActive ||
            SelectedTab != ActivityTab.History || SelectedDetailTab != ActivityDetailTab.Charts || HistoryLiveTimeline is not { HasContext: true } overview) return;
        var categories = VisibleCategories;
        var query = DetailQuery() with { ShowCategories = categories };
        var contextKey = HistoryTimelineContextKey(query, categories);
        var span = (request.WindowEndUtc - request.WindowStartUtc).TotalSeconds;
        if (request.ContextKey != overview.ContextKey || request.ContextKey != contextKey ||
            request.WindowEndUtc < overview.WindowStartUtc || request.WindowEndUtc > overview.WindowEndUtc ||
            span <= 0 || span > 7200 || request.WindowEndUtc.Ticks % TimeSpan.TicksPerSecond != 0) return;

        var version = ++historyReplayReadVersion;
        var cancellationToken = BeginRead(ref historyReplayCancellation);
        try
        {
            // Use the same bounded projection as Current, with an exclusive cutoff at the
            // playback cursor. The overview's outward rounding must not reveal future awards.
            var snapshot = await persistence.GetLiveTimelineAsync(query with
            {
                FromUtc = request.WindowStartUtc,
                ToUtc = request.WindowEndUtc
            }, request.WindowEndUtc, null, null, cancellationToken, (int)span);
            cancellationToken.ThrowIfCancellationRequested();
            if (isDisposed || !isActive || SelectedTab != ActivityTab.History || SelectedDetailTab != ActivityDetailTab.Charts ||
                version != historyReplayReadVersion || HistoryLiveTimeline?.ContextKey != contextKey ||
                HistoryTimelineContextKey(DetailQuery(), VisibleCategories) != contextKey) return;
            HistoryReplayTimeline = snapshot with { ContextKey = contextKey };
            if (ErrorText == ReplayReadError) ErrorText = "";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            Log.Warning(error, "Unable to load activity replay");
            if (!isDisposed && version == historyReplayReadVersion) ErrorText = ReplayReadError;
        }
    }

    private void CancelHistoryReplayRead()
    {
        ++historyReplayReadVersion;
        historyReplayCancellation.Cancel();
    }

    private void DisposeHistoryReplay()
    {
        CancelHistoryReplayRead();
        historyReplayCancellation.Dispose();
    }
}

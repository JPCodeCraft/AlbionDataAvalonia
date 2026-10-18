using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;
using static AlbionDataAvalonia.ViewModels.ActivityReads;

namespace AlbionDataAvalonia.ViewModels;

// The Charts tab: an overview of the whole selection, and a replay window the chart asks for while playing.
public partial class ActivityHistoryViewModel
{
    private const string ReplayReadError = "Could not load replay. Try again.";
    private CancellationTokenSource historyReplayCancellation = new();
    private long historyReplayReadVersion;

    [ObservableProperty] private ActivityLiveTimelineSnapshot? historyLiveTimeline;
    [ObservableProperty] private ActivityLiveTimelineSnapshot? historyReplayTimeline;

    private static string HistoryTimelineContextKey(ActivityQuery query, ActivityCategories categories) =>
        $"history:{query.PlaySessionId}:{query.SessionId}:{query.EncounterId}:{query.Scope}:{query.ActorKey}:{categories}";

    private async Task LoadChartAsync(long version, ActivityQuery query)
    {
        if (page.Persistence is not { } persistence || query.PlaySessionId is not { } playSessionId || cachedDetailHeaders is not [var header] ||
            header.PlaySession.Id != playSessionId) return;
        var categories = Filters.VisibleCategories;
        var selectedEncounter = SelectedEncounter?.Source;
        var run = SelectedSession?.Source;
        var fromUtc = selectedEncounter?.StartedAtUtc ?? run?.StartedAtUtc ?? ActivityPlaySessionRow.StartOf(header);
        var toUtc = selectedEncounter is not null ? selectedEncounter.ClosedAtUtc ?? selectedEncounter.LastObservedAtUtc :
            run is not null ? run.ClosedAtUtc ?? run.LastObservedAtUtc : ActivityPlaySessionRow.EndOf(header);
        var snapshot = await persistence.GetTimelineAsync(query with { ShowCategories = categories },
            fromUtc, toUtc, detailQueryCancellation.Token);
        if (!IsCurrentDetail(version, playSessionId) || Filters.VisibleCategories != categories || DetailQuery() != query) return;
        HistoryLiveTimeline = snapshot with { ContextKey = HistoryTimelineContextKey(query, categories) };
    }

    partial void OnHistoryLiveTimelineChanged(ActivityLiveTimelineSnapshot? oldValue, ActivityLiveTimelineSnapshot? newValue)
    {
        if (oldValue?.ContextKey == newValue?.ContextKey) return;
        CancelHistoryReplayRead();
        HistoryReplayTimeline = null;
    }

    [RelayCommand]
    private async Task LoadHistoryReplayAsync(ActivityTimelineReplayRequest request)
    {
        if (request is null || page.Persistence is not { } persistence || !page.IsShowing(ActivityTab.History) ||
            SelectedDetailTab != ActivityDetailTab.Charts || HistoryLiveTimeline is not { HasContext: true } overview) return;
        var categories = Filters.VisibleCategories;
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
            if (!page.IsShowing(ActivityTab.History) || SelectedDetailTab != ActivityDetailTab.Charts ||
                version != historyReplayReadVersion || HistoryLiveTimeline?.ContextKey != contextKey ||
                HistoryTimelineContextKey(DetailQuery(), Filters.VisibleCategories) != contextKey) return;
            HistoryReplayTimeline = snapshot with { ContextKey = contextKey };
            if (page.ErrorText == ReplayReadError) page.ErrorText = "";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            Log.Warning(error, "Unable to load activity replay");
            if (!page.IsDisposed && version == historyReplayReadVersion) page.ErrorText = ReplayReadError;
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

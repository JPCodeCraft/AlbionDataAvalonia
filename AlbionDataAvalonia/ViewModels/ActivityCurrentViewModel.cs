using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AlbionDataAvalonia.ViewModels.ActivityReads;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>
/// The Activity page's Current tab: totals for the open play session, the current run or the last hours,
/// with the session's runs, its loot, the live chart and the party.
/// </summary>
public partial class ActivityCurrentViewModel : ObservableObject, IDisposable
{
    private readonly ActivityViewModel page;
    private CancellationTokenSource currentQueryCancellation = new();
    private ActivitySession? currentSession;
    private ActivityQuery? currentCombatQuery;
    private long currentCombatChangeVersion;
    private long currentCombatReadVersion = -1;
    private double currentCombatSeconds;
    private ActivityQuery? currentResourcesQuery;
    private long currentResourcesReadVersion;
    private Guid? currentRunsPlaySessionId;
    private long currentRunsReadVersion = -1;
    private DateTime currentRunsReadAtUtc;
    private DateTime? currentPlayFromUtc;

    [ObservableProperty] private string currentTitle = "No active session";
    [ObservableProperty] private string currentDescription = "Start playing to record activity.";
    [ObservableProperty] private string currentDurationText = "Session time: 0:00";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentCombatDurationText), nameof(CurrentTagsText))]
    private ActivitySummaryPresentation currentSummary = ActivitySummaryPresentation.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCurrentRuns))]
    private ObservableCollection<ActivitySessionRow> currentRuns = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentRunTime))]
    private string currentRunTimeText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentState))]
    private string currentStateText = "";
    [ObservableProperty] private bool hasCurrentPlaySession;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentResources))]
    private ObservableCollection<ActivityResourceRow> currentResources = new();

    internal ActivityCurrentViewModel(ActivityViewModel page) => this.page = page;

    public ActivityFilterState Filters => page.Filters;
    public ActivityLootExport Export => page.Export;
    public string CurrentCombatDurationText => $"Combat time: {UiFormat.Duration(CurrentSummary.Rates.CombatDurationSeconds)}";
    public string CurrentTagsText => ActivityDisplay.Tags(CurrentSummary.Summary.Tags);
    public bool ShowCurrentRuns => IsPlaySessionPeriod && CurrentRuns.Count > 0;
    public bool HasCurrentRunTime => !string.IsNullOrEmpty(CurrentRunTimeText);
    public bool HasCurrentState => !string.IsNullOrEmpty(CurrentStateText);
    public bool HasCurrentResources => CurrentResources.Count > 0;

    private ActivityTrackerService? Tracker => page.Tracker;
    private ActivityPersistence? Persistence => page.Persistence;

    internal async Task RefreshAsync()
    {
        await RefreshCurrentAsync();
        await RefreshLiveTimelineSafelyAsync();
    }

    // Clocks move every second, between the reads.
    internal void Tick(DateTime nowUtc)
    {
        if (Tracker is not null) UpdateCurrentPlayState(Tracker.GetCurrentContext(), nowUtc);
        if (currentSession is not null && SelectedPeriod.Value == ActivityPeriodMode.CurrentRun)
            CurrentDurationText = $"Run time: {UiFormat.Duration(ActivityProjection.GetDurationSeconds(currentSession, nowUtc))}";
        else if (SelectedPeriod.Value != ActivityPeriodMode.CurrentRun)
            ApplyCurrentPeriodPresentation(nowUtc);
    }

    internal void OnDataChanged(HashSet<Guid> changedIds)
    {
        if (Tracker?.CurrentSessionId is { } currentId && changedIds.Contains(currentId) ||
            currentCombatQuery?.SessionId is { } cachedId && changedIds.Contains(cachedId)) ++currentCombatChangeVersion;
    }

    internal void OnScopeChanged()
    {
        currentQueryCancellation.Cancel();
        liveTimelineCancellation.Cancel();
        currentResourcesQuery = null;
        ClearCurrentPeriod();
        CurrentLiveTimeline = null;
    }

    internal void OnCategoriesChanged()
    {
        liveTimelineCancellation.Cancel();
        if (page.IsShowing(ActivityTab.Current)) _ = RefreshLiveTimelineSafelyAsync();
    }

    internal void CancelReads()
    {
        currentQueryCancellation.Cancel();
        liveTimelineCancellation.Cancel();
        CancelCurrentLootRead();
        CancelCurrentMobsRead();
    }

    // A new play session starts empty.
    internal void ResetPlaySession()
    {
        currentRunsPlaySessionId = null;
        if (CurrentRuns.Count > 0) CurrentRuns = new();
        ClearCurrentPeriod();
        CurrentLiveTimeline = null;
    }

    private async Task RefreshCurrentAsync()
    {
        if (Persistence is not { } persistence || Tracker is not { } tracker) return;
        var context = tracker.GetCurrentContext();
        var sessionId = context.Session?.Id;
        var scope = Filters.Scope;
        var period = SelectedPeriod.Value;
        var hours = LastHours;
        var cancellationToken = BeginRead(ref currentQueryCancellation);
        var sessionTask = period == ActivityPeriodMode.CurrentRun && sessionId is { } currentSessionId
            ? persistence.GetSessionAsync(currentSessionId, cancellationToken) : Task.FromResult<ActivitySession?>(null);
        var combatQuery = period == ActivityPeriodMode.CurrentRun && sessionId is { } combatSessionId
            ? new ActivityQuery { SessionId = combatSessionId, Scope = scope } : null;
        var combatVersion = currentCombatChangeVersion;
        var combatTask = combatQuery is null ? Task.FromResult(0d) : combatQuery == currentCombatQuery && combatVersion == currentCombatReadVersion
            ? Task.FromResult(currentCombatSeconds) : persistence.GetCombatDurationSecondsAsync(combatQuery, cancellationToken);
        await Task.WhenAll(sessionTask, combatTask);
        cancellationToken.ThrowIfCancellationRequested();
        if (!page.IsShowing(ActivityTab.Current) || Filters.Scope != scope ||
            SelectedPeriod.Value != period || LastHours != hours || tracker.GetCurrentContext().Owner != context.Owner ||
            tracker.CurrentSessionId != sessionId) return;
        currentCombatQuery = combatQuery;
        currentCombatReadVersion = combatVersion;
        currentCombatSeconds = combatTask.Result;
        currentSession = context.Session;
        // The tracker owns live boundaries; only the journal writer updates cached totals.
        if (currentSession is { } liveSession && sessionTask.Result is { } savedSession)
            currentSession = liveSession with
            {
                PersonalSummaryJson = savedSession.PersonalSummaryJson,
                PartySummaryJson = savedSession.PartySummaryJson,
                ObservedSummaryJson = savedSession.ObservedSummaryJson
            };
        CurrentTitle = currentSession is { } currentTitle ? ActivityDisplay.SessionTitle(currentTitle) : "No active session";
        CurrentDescription = currentSession is { } session ? $"{session.CharacterName} · {ActivityDisplay.Server(session.Server)}{(session.IsPartial ? " · incomplete recording" : "")}"
            : tracker.IsAwaitingContext ? "" : "Start playing to record activity.";
        if (period == ActivityPeriodMode.PlaySession)
        {
            await RefreshCurrentRunsAsync(context, cancellationToken);
            ApplyCurrentPlayHeader(context);
        }
        UpdateCurrentPlayState(tracker.GetCurrentContext(), DateTime.UtcNow);
        if (period == ActivityPeriodMode.CurrentRun && context.Owner is not null && currentSession is { } current)
        {
            var summary = ActivityProjection.GetSummary(current, scope);
            CurrentSummary = new(summary, ActivityProjection.GetRates(summary, current, combatTask.Result, DateTime.UtcNow), true);
            CurrentDurationText = $"Run time: {UiFormat.Duration(ActivityProjection.GetDurationSeconds(current, DateTime.UtcNow))}";
            CurrentRunTimeText = "";
            CurrentPeriodDescription = $"Since {UiFormat.LocalDateTime(current.StartedAtUtc)}";
        }
        else if (period == ActivityPeriodMode.CurrentRun) ClearCurrentPeriod();
        if (period == ActivityPeriodMode.CurrentRun)
        {
            CurrentPeriodWarning = "";
            await RefreshCurrentResourcesAsync(context.Owner is null ? null : sessionId, scope, cancellationToken);
        }
        else await RefreshCurrentPeriodAsync(cancellationToken);
        await RefreshCurrentLootAsync();
        await RefreshCurrentMobsAsync();
    }

    private async Task RefreshCurrentResourcesAsync(Guid? sessionId, ActivityScope scope, CancellationToken cancellationToken)
    {
        if (Persistence is not { } persistence) return;
        if (sessionId is not { } id)
        {
            if (CurrentResources.Count > 0) CurrentResources = new();
            currentResourcesQuery = null;
            return;
        }

        var query = new ActivityQuery { SessionId = id, Scope = scope };
        var dataVersion = page.ItemChangeVersion;
        if (query == currentResourcesQuery && dataVersion == currentResourcesReadVersion) return;
        if (query != currentResourcesQuery)
        {
            if (CurrentResources.Count > 0) CurrentResources = new();
            currentResourcesQuery = null;
        }
        var rows = await persistence.GetResourceTotalsAsync(query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!page.IsShowing(ActivityTab.Current) || Tracker?.CurrentSessionId != id || Filters.Scope != scope) return;
        CurrentResources = new(rows.Select(row => new ActivityResourceRow(row)));
        currentResourcesQuery = query;
        currentResourcesReadVersion = dataVersion;
    }

    // The open play session's runs, with the open run's live boundaries.
    private async Task RefreshCurrentRunsAsync(ActivityCurrentContext context, CancellationToken cancellationToken)
    {
        if (Persistence is not { } persistence || Tracker is not { } tracker) return;
        if (context.PlaySession is not { } playSession)
        {
            currentRunsPlaySessionId = null;
            currentPlayFromUtc = null;
            if (CurrentRuns.Count > 0) CurrentRuns = new();
            return;
        }
        var version = tracker.CurrentReadVersion;
        var nowUtc = DateTime.UtcNow;
        if (currentRunsPlaySessionId == playSession.Id && (currentRunsReadVersion == version || nowUtc - currentRunsReadAtUtc < TimeSpan.FromSeconds(2)))
        {
            foreach (var row in CurrentRuns.Where(row => row.IsLive)) row.RefreshDuration();
            return;
        }
        var header = await persistence.GetPlaySessionAsync(playSession.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (page.IsDisposed || tracker.CurrentPlaySessionId != playSession.Id) return;
        var runs = (header?.Runs ?? []).ToList();
        // The tracker owns the open run's boundaries; the journal writer owns its saved totals.
        if (tracker.GetCurrentContext().Session is { } live && live.PlaySessionId == playSession.Id)
        {
            var index = runs.FindIndex(run => run.Id == live.Id);
            var merged = index < 0 ? live : live with
            {
                PersonalSummaryJson = runs[index].PersonalSummaryJson,
                PartySummaryJson = runs[index].PartySummaryJson,
                ObservedSummaryJson = runs[index].ObservedSummaryJson
            };
            if (index < 0) runs.Add(merged); else runs[index] = merged;
        }
        var scope = Filters.Scope;
        var liveId = tracker.CurrentSessionId;
        var hadRuns = CurrentRuns.Count > 0;
        ApplyHeaderRows(CurrentRuns, runs.OrderBy(run => run.StartedAtUtc).ToArray(), row => row.Id, source => source.Id,
            source => new ActivitySessionRow(source, scope), (row, source) => row.Update(source, scope), false);
        foreach (var row in CurrentRuns) row.IsLive = row.Id == liveId;
        if (hadRuns != CurrentRuns.Count > 0) OnPropertyChanged(nameof(ShowCurrentRuns));
        currentRunsPlaySessionId = playSession.Id;
        currentRunsReadVersion = version;
        currentRunsReadAtUtc = nowUtc;
        var start = runs.Count == 0 ? playSession.StartedAtUtc : runs.Min(run => run.StartedAtUtc);
        currentPlayFromUtc = start < playSession.StartedAtUtc ? start : playSession.StartedAtUtc;
    }

    // What the play session is doing now, beside its time.
    private void UpdateCurrentPlayState(ActivityCurrentContext context, DateTime nowUtc)
    {
        HasCurrentPlaySession = context.PlaySession is not null;
        if (!IsPlaySessionPeriod || context.Owner is null) { CurrentStateText = ""; return; }
        if (context.PlaySession is not { } playSession) { CurrentStateText = "Waiting for your first activity"; return; }
        // A session started with Start session has no run until the first activity.
        if (context.Session is null && CurrentRuns.Count == 0) { CurrentStateText = "Started · waiting for your first activity"; return; }
        if (ActivityPlaySessionProjection.IsIdle(playSession, nowUtc))
            CurrentStateText = $"Idle {UiFormat.Duration((nowUtc - playSession.LastActivityAtUtc).TotalSeconds)} · time is paused";
        else CurrentStateText = context.Session is null ? "Between runs" : "";
    }

    private void ApplyCurrentPlayHeader(ActivityCurrentContext context)
    {
        if (context.Owner is not { } owner) return;
        CurrentTitle = context.PlaySession is null ? "New session"
            : CurrentRuns.Count > 0 ? ActivityDisplay.PlaySessionTitle(CurrentRuns.Select(row => row.Source).ToArray()) : "Current session";
        CurrentDescription = $"{owner.CharacterName} · {ActivityDisplay.Server(owner.Server)}";
    }

    [RelayCommand]
    private async Task OpenCurrentSessionAsync()
    {
        if (Tracker?.CurrentPlaySessionId is { } id) await page.RunSafelyAsync(() => page.History.OpenPlaySessionDetailsAsync(id));
    }

    [RelayCommand]
    private void OpenRunRow(ActivitySessionRow? run)
    {
        if (run is not null) page.History.OpenRun(run.Id);
    }

    [RelayCommand] private Task OpenTimelineEncounterAsync(Guid encounterId) => page.History.OpenEncounterAsync(encounterId);

    public void Dispose()
    {
        currentQueryCancellation.Cancel(); currentQueryCancellation.Dispose();
        CancelCurrentLootRead(); currentLootCancellation.Dispose();
        CancelCurrentMobsRead(); currentMobsCancellation.Dispose();
        liveTimelineCancellation.Cancel(); liveTimelineCancellation.Dispose();
        DisposeParty();
    }
}

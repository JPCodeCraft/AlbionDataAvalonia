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

namespace AlbionDataAvalonia.ViewModels;

// History lists play sessions; a run chip narrows the details to that run (SelectedSession).
// The Current tab totals the open play session and shows its runs.
public partial class ActivityViewModel
{
    private ActivityPlaySessionHeader? cachedDetailHeader;
    private Guid? currentRunsPlaySessionId;
    private long currentRunsReadVersion = -1;
    private DateTime currentRunsReadAtUtc;
    private DateTime? currentPlayFromUtc;

    [ObservableProperty] private ObservableCollection<ActivityPlaySessionRow> playSessions = new();
    [ObservableProperty] private ActivityPlaySessionRow? selectedPlaySession;
    [ObservableProperty] private ObservableCollection<ActivitySessionRow> currentRuns = new();
    [ObservableProperty] private string currentRunTimeText = "";
    [ObservableProperty] private string currentStateText = "";
    [ObservableProperty] private bool hasCurrentPlaySession;

    public bool HasSelectedPlaySession => SelectedPlaySession is not null;
    public bool HasMultipleRuns => Sessions.Count > 1;
    public bool ShowCurrentRuns => IsPlaySessionPeriod && CurrentRuns.Count > 0;
    public bool HasCurrentRunTime => !string.IsNullOrEmpty(CurrentRunTimeText);
    public bool HasCurrentState => !string.IsNullOrEmpty(CurrentStateText);
    public string DeleteButtonText => SelectedSession is not null ? "Delete run…" : "Delete session…";
    public string DeleteTargetText => SelectedSession is { } run ? $"run “{run.Title}”" : $"session “{SelectedPlaySession?.Title}”";
    public string ClearSelectionText => IsShowingMap ? "All maps" : "All encounters";

    partial void OnCurrentRunsChanged(ObservableCollection<ActivitySessionRow> value) => OnPropertyChanged(nameof(ShowCurrentRuns));
    partial void OnCurrentRunTimeTextChanged(string value) => OnPropertyChanged(nameof(HasCurrentRunTime));
    partial void OnCurrentStateTextChanged(string value) => OnPropertyChanged(nameof(HasCurrentState));

    private async Task LoadPlaySessionsAsync(bool append, bool refresh = false)
    {
        if (persistence is null) return;
        var version = ++sessionListVersion;
        var dataVersion = historyChangeVersion;
        var cancellationToken = BeginRead(ref historyQueryCancellation);
        var query = HistoryQuery();
        var page = append ? sessionPage + 1 : refresh ? sessionPage : 0;
        var result = await ReadPagesAsync(index => persistence.GetPlaySessionsAsync(query, index, cancellationToken), page, append);
        if (isDisposed || version != sessionListVersion || HistoryQuery() != query) return;
        var selected = SelectedPlaySession;
        applyingSelection = true;
        try
        {
            ApplyHeaderRows(PlaySessions, result.Items, row => row.Id, source => source.PlaySession.Id,
                source => new ActivityPlaySessionRow(source, query.Scope), (row, source) => row.Update(source, query.Scope), append);
            // A session opened from elsewhere may sit beyond the loaded pages; keep showing it.
            var kept = selected is null ? null : PlaySessions.FirstOrDefault(row => row.Id == selected.Id);
            if (selected is not null && kept is null) { PlaySessions.Insert(0, selected); kept = selected; }
            SelectedPlaySession = kept;
            sessionPage = page;
            HasMoreSessions = result.HasMore;
            OnPropertyChanged(nameof(HistoryCountText));
            NotifyDetailProperties();
            if (!append) historyReadVersion = dataVersion;
        }
        finally { applyingSelection = false; }
        await LoadPlaySessionPartiesAsync(cancellationToken);
    }

    // Party members come from each run's recorded events.
    private async Task LoadPlaySessionPartiesAsync(CancellationToken cancellationToken)
    {
        if (persistence is null) return;
        var rows = new List<ActivityPlaySessionRow>();
        var runIds = new List<Guid>();
        foreach (var row in PlaySessions.Where(row => row.NeedsParty))
        {
            if (runIds.Count + row.Runs.Count > 1000) break;
            rows.Add(row);
            runIds.AddRange(row.Runs.Select(run => run.Id));
        }
        if (rows.Count == 0) return;
        var members = await persistence.GetSessionPartyMembersAsync(runIds, cancellationToken);
        if (isDisposed) return;
        foreach (var row in rows)
            row.SetParty(row.Header, row.Runs.SelectMany(run => members.TryGetValue(run.Id, out var names) ? names : [])
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private void ApplyRunRows(ActivityPlaySessionHeader header)
    {
        var scope = SelectedScope.Value;
        ApplyHeaderRows(Sessions, header.Runs, row => row.Id, source => source.Id,
            source => new ActivitySessionRow(source, scope), (row, source) => row.Update(source, scope), false);
        OnPropertyChanged(nameof(HasMultipleRuns));
    }

    partial void OnSelectedPlaySessionChanged(ActivityPlaySessionRow? value)
    {
        NotifyDetailProperties();
        if (applyingSelection) return;
        ++detailChangeVersion;
        applyingSelection = true;
        try
        {
            SelectedSession = null;
            SelectedEncounter = null;
            SelectedPlayer = null;
            ResetMaps();
            Sessions.Clear();
            if (IsImportedDetail) SelectedDetailTabIndex = 4;
        }
        finally { applyingSelection = false; }
        ResetDetailRows();
        if (value is not null) _ = RunSafelyAsync(RefreshDetailsAsync);
    }

    private void ResetDetailRows()
    {
        Encounters.Clear(); Players.Clear(); Mobs.Clear(); SessionLoot.Clear(); Timeline.Clear(); HistoryLiveTimeline = null;
        encounterPage = timelinePage = sessionLootPage = 0;
        HasMoreEncounters = HasMoreSessionLoot = HasMoreTimeline = false;
        DetailSummary = ActivitySummaryPresentation.Empty;
        SessionResources = new();
        LegacyImportInfo = null;
        cachedDetailQuery = null;
        OnPropertyChanged(nameof(HasMultipleRuns));
        ++detailVersion;
    }

    [RelayCommand] private void ClearRun() => SelectedSession = null;

    private async Task OpenPlaySessionDetailsAsync(Guid playSessionId, Guid? runId = null, Guid? encounterId = null)
    {
        if (persistence is null) return;
        var header = await persistence.GetPlaySessionAsync(playSessionId);
        if (isDisposed || header is null) return;
        applyingSelection = true;
        try { SelectedTabIndex = 1; }
        finally { applyingSelection = false; }
        await LoadPlaySessionsAsync(false);
        var row = PlaySessions.FirstOrDefault(existing => existing.Id == playSessionId);
        if (row is null) { row = new(header, SelectedScope.Value); PlaySessions.Insert(0, row); }
        var encounter = encounterId is { } selectedEncounterId ? await persistence.GetEncounterAsync(selectedEncounterId) : null;
        if (isDisposed) return;
        applyingSelection = true;
        try
        {
            SelectedPlaySession = row;
            ApplyRunRows(header);
            SelectedSession = runId is { } id ? Sessions.FirstOrDefault(run => run.Id == id) : null;
            SelectedPlayer = null;
            ResetMaps();
            ResetDetailRows();
            SelectedEncounter = encounter is null ? null : new ActivityEncounterRow(encounter, SelectedScope.Value);
            if (SelectedEncounter is not null) Encounters.Add(SelectedEncounter);
            if (SelectedEncounter is not null && SelectedDetailTabIndex == MapsDetailTabIndex) SelectedDetailTabIndex = 0;
            if (IsImportedDetail) SelectedDetailTabIndex = 4;
            ++detailChangeVersion;
        }
        finally { applyingSelection = false; }
        NotifyDetailProperties();
        await RefreshDetailsAsync();
    }

    /// <summary>Opens a run in History, inside its play session.</summary>
    public void OpenRun(Guid runId) => _ = RunSafelyAsync(() => OpenSessionDetailsAsync(runId));

    // The open play session's runs for the Current tab, with the open run's live boundaries.
    private async Task RefreshCurrentRunsAsync(ActivityCurrentContext context, CancellationToken cancellationToken)
    {
        if (persistence is null || tracker is null) return;
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
        if (isDisposed || tracker.CurrentPlaySessionId != playSession.Id) return;
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
        var scope = SelectedScope.Value;
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

    public void StartNewSession(Guid? expectedPlaySessionId = null)
    {
        tracker?.StartNewSession(expectedPlaySessionId);
        UpdateStatus();
        currentRunsPlaySessionId = null;
        if (CurrentRuns.Count > 0) CurrentRuns = new();
        ClearCurrentPeriod();
        CurrentLiveTimeline = null;
        if (isActive && SelectedTabIndex == 0) _ = RunSafelyAsync(RefreshVisibleAsync);
    }
}

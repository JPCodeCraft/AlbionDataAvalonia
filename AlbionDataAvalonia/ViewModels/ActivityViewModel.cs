using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Network.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel : ViewModelBase, IDisposable
{
    private readonly ActivityTrackerService? tracker;
    private readonly ActivityPersistence? persistence;
    private readonly ActivityItemValuationService? valuations;
    private readonly DispatcherTimer refreshTimer;
    private volatile bool isActive;
    private volatile bool isDisposed;
    private bool refreshing;
    private bool applyingSelection;
    private int loadingCount;
    private int sessionPage;
    private int encounterPage;
    private int timelinePage;
    private int sessionLootPage;
    private int globalLootPage;
    private ActivityQuery? displayedGlobalLootQuery;
    private long detailVersion;
    private long sessionListVersion;
    private long globalLootVersion;
    private long historyChangeVersion = 1;
    private long historyReadVersion;
    private long detailChangeVersion = 1;
    private long detailReadVersion;
    private long itemChangeVersion = 1;
    private long itemReadVersion;
    private int statusUpdatePending;
    private Guid? statusSessionId;
    private bool statusLegacyImportWarning;
    private readonly HashSet<Guid> changedHistorySessions = [];
    private ActivityQuery? loadedHistoryQuery;
    private long detailDataVersion;
    private ActivityQuery? cachedDetailQuery;
    private long cachedDetailDataVersion = -1;
    private ActivitySession? cachedDetailSession;
    private ActivityEncounter? cachedDetailEncounter;
    private ActivitySummary? cachedDetailSummary;
    private double cachedDetailCombatSeconds;
    private ActivityLegacyImportInfo? cachedDetailLegacyInfo;
    private ActivityQuery? currentCombatQuery;
    private long currentCombatChangeVersion;
    private long currentCombatReadVersion = -1;
    private double currentCombatSeconds;
    private CancellationTokenSource historyQueryCancellation = new();
    private CancellationTokenSource detailQueryCancellation = new();
    private CancellationTokenSource itemsQueryCancellation = new();
    private CancellationTokenSource currentQueryCancellation = new();
    private ActivitySession? currentSession;

    public IReadOnlyList<ActivityOption<ActivityScope>> ScopeOptions { get; } =
        [new(ActivityScope.Personal, "Personal"), new(ActivityScope.Party, "Party")];
    public IReadOnlyList<ActivityOption<ActivityScope>> LootScopeOptions { get; } =
        [new(ActivityScope.Personal, "Personal"), new(ActivityScope.Party, "Party"), new(ActivityScope.AllObserved, "Everyone")];
    [ObservableProperty] private ActivityOption<ActivityScope> selectedScope = new(ActivityScope.Personal, "Personal");
    [ObservableProperty] private ActivityOption<ActivityScope> selectedLootScope = new(ActivityScope.Personal, "Personal");
    [ObservableProperty] private ActivityOption<ActivityScope> selectedSessionLootScope = new(ActivityScope.Personal, "Personal");
    [ObservableProperty] private int selectedTabIndex;
    [ObservableProperty] private int selectedDetailTabIndex;
    [ObservableProperty] private string lootSearch = "";
    [ObservableProperty] private string statusText = "Waiting for character";
    [ObservableProperty] private string errorText = "";
    [ObservableProperty] private bool isRecordingEnabled = true;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string currentTitle = "No active session";
    [ObservableProperty] private string currentDescription = "Start playing to record activity.";
    [ObservableProperty] private string currentDurationText = "Session time: 0:00";
    [ObservableProperty] private bool hasCurrentSession;
    [ObservableProperty] private bool hasCurrentEncounter;
    [ObservableProperty] private ActivitySummaryPresentation currentSummary = ActivitySummaryPresentation.Empty;
    [ObservableProperty] private ActivitySummaryPresentation detailSummary = ActivitySummaryPresentation.Empty;
    [ObservableProperty] private ActivitySummaryPresentation globalLootSummary = ActivitySummaryPresentation.Empty;
    [ObservableProperty] private ObservableCollection<ActivitySessionRow> sessions = new();
    [ObservableProperty] private ObservableCollection<ActivityEncounterRow> encounters = new();
    [ObservableProperty] private ObservableCollection<ActivityPlayerRow> players = new();
    [ObservableProperty] private ObservableCollection<ActivityMobRow> mobs = new();
    [ObservableProperty] private ObservableCollection<ActivityLootRow> sessionLoot = new();
    [ObservableProperty] private ObservableCollection<ActivityLootRow> globalLoot = new();
    [ObservableProperty] private ObservableCollection<ActivityEventRow> timeline = new();
    [ObservableProperty] private ActivitySessionRow? selectedSession;
    [ObservableProperty] private ActivityEncounterRow? selectedEncounter;
    [ObservableProperty] private ActivityPlayerRow? selectedPlayer;
    [ObservableProperty] private bool hasMoreSessions;
    [ObservableProperty] private bool hasMoreEncounters;
    [ObservableProperty] private bool hasMoreTimeline;
    [ObservableProperty] private bool hasMoreSessionLoot;
    [ObservableProperty] private bool hasMoreGlobalLoot;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool ShowActivityScope => SelectedTabIndex is 0 or 1;
    public Guid? CurrentSessionId => tracker?.CurrentSessionId;
    public string RecordingButtonText => IsRecordingEnabled ? "Disable recording" : "Enable recording";
    public bool HasSelectedSession => SelectedSession is not null;
    public bool HasSelectedEncounter => SelectedEncounter is not null;
    public bool HasSelectedPlayer => SelectedPlayer is not null;
    public bool CanDeleteSelectedSession => SelectedSession is { Source.State: not ActivityState.Open } && SelectedSession.Id != tracker?.CurrentSessionId;
    public string CurrentCombatDurationText => $"Combat time: {ActivityDisplay.Duration(CurrentSummary.Rates.CombatDurationSeconds)}";
    public string DetailTitle => IsShowingMap ? SelectedMap!.Name : SelectedEncounter is { } encounter ? $"Encounter {encounter.Number} · {encounter.Classification}" : SelectedSession?.Title ?? "Select a session";
    public string DetailDurationText => IsShowingMap ? $"Recorded time: {SelectedMap!.DurationText} · Combat time: {ActivityDisplay.Duration(DetailSummary.Rates.CombatDurationSeconds)}"
        : IsImportedDetail ? $"Duration: {SelectedSession?.DurationText}" : SelectedEncounter is { } encounter ? $"Combat time: {encounter.DurationText}"
        : $"Session time: {SelectedSession?.DurationText ?? "0:00"} · Combat time: {ActivityDisplay.Duration(DetailSummary.Rates.CombatDurationSeconds)}";
    public string DetailScopeText => $"{SelectedPlayer?.Name ?? ""}{(SelectedSession?.Source.IsPartial == true ? $"{(SelectedPlayer is null ? "" : " · ")}Incomplete recording" : "")}";
    public string DetailEarningsText => SelectedEncounter is not null ? "Items picked up while this encounter was open" : "";
    public string HistoryCountText => $"{Sessions.Count:N0} sessions";
    public string LootCountText => $"{GlobalLoot.Count:N0} pickups";
    public string GlobalLootTotalsText => string.Join(" · ", new[]
    {
        ShowGlobalLootPanel ? $"Loot {GlobalLootSummary.Summary.LootItems:N0} items · value {GlobalLootSummary.LootValue}" : null,
        SelectedItemSource.Value != ActivityItemSources.Loot ? $"Gathered {GlobalLootSummary.ResourceQuantityText} · EMV {GlobalLootSummary.ResourceValueText}" : null
    }.Where(value => value is not null));
    public string GlobalLootDetailsText => "Estimated item values in silver";
    public string ChartDescription => SelectedEncounter is null ? "Whole session" : $"Encounter {SelectedEncounter.Number}";

    public ActivityViewModel()
    {
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refreshTimer.Tick += OnRefreshTick;
    }

    public ActivityViewModel(ActivityTrackerService tracker, ActivityPersistence persistence, CsvExportService csvExportService,
        ActivityItemValuationService? valuations = null) : this()
    {
        this.tracker = tracker;
        this.persistence = persistence;
        this.valuations = valuations;
        this.csvExportService = csvExportService;
        tracker.Changed += OnActivityChanged;
        persistence.StatusChanged += OnPersistenceStatusChanged;
        persistence.DataChanged += OnCommittedDataChanged;
        UpdateStatus();
    }

    public void SetActive(bool active)
    {
        if (isDisposed || isActive == active) return;
        isActive = active;
        if (active)
        {
            UpdateStatus();
            if (historyQueryCancellation.IsCancellationRequested) BeginRead(ref historyQueryCancellation);
            if (detailQueryCancellation.IsCancellationRequested) BeginRead(ref detailQueryCancellation);
            if (itemsQueryCancellation.IsCancellationRequested) BeginRead(ref itemsQueryCancellation);
            refreshTimer.Start();
            _ = RunSafelyAsync(RefreshVisibleAsync);
        }
        else
        {
            refreshTimer.Stop();
            historyQueryCancellation.Cancel();
            detailQueryCancellation.Cancel();
            itemsQueryCancellation.Cancel();
            currentQueryCancellation.Cancel();
            CancelCurrentLootRead();
            liveTimelineCancellation.Cancel();
            CancelHistoryReplayRead();
        }
    }

    private void OnActivityChanged(object? sender, EventArgs args)
    {
        if (isDisposed || !isActive || Interlocked.Exchange(ref statusUpdatePending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref statusUpdatePending, 0);
            if (isDisposed || !isActive) return;
            UpdateStatus();
        });
    }

    private void OnPersistenceStatusChanged(ActivityRecordingStatus status) => OnActivityChanged(persistence, EventArgs.Empty);

    private void OnCommittedDataChanged(ActivityDataChange change) => Dispatcher.UIThread.Post(() =>
    {
        if (isDisposed) return;
        var changedIds = change.SessionIds.ToHashSet();
        if (change.HistoryChanged)
        {
            ++historyChangeVersion;
            // New/imported headers can fall inside the cached date range. Retain their
            // IDs too, with a bounded fallback to canonical paging after large changes.
            changedHistorySessions.UnionWith(changedIds);
            if (changedHistorySessions.Count > 1000)
            {
                changedHistorySessions.Clear();
                loadedHistoryQuery = null;
                ++sessionListVersion;
            }
        }
        if (change.ItemsChanged) ++itemChangeVersion;
        if (SelectedSession is { } selected && changedIds.Contains(selected.Id))
        { ++detailChangeVersion; ++detailDataVersion; }
        if (tracker?.CurrentSessionId is { } currentId && changedIds.Contains(currentId) ||
            currentCombatQuery?.SessionId is { } cachedId && changedIds.Contains(cachedId)) ++currentCombatChangeVersion;
    });

    private async void OnRefreshTick(object? sender, EventArgs args)
    {
        if (isDisposed || !isActive || refreshing || loadingCount > 0) return;
        UpdateStatus();
        if (currentSession is not null && SelectedPeriod.Value == ActivityPeriodMode.CurrentSession)
            CurrentDurationText = $"Session time: {ActivityDisplay.Duration(ActivityProjection.GetDurationSeconds(currentSession, DateTime.UtcNow))}";
        else if (SelectedPeriod.Value != ActivityPeriodMode.CurrentSession)
            ApplyCurrentPeriodPresentation(DateTime.UtcNow);
        if (SelectedTabIndex == 1)
        {
            foreach (var row in Sessions.Where(x => x.Source.State == ActivityState.Open)) row.RefreshDuration();
            foreach (var row in Maps.Where(x => x.Source.ObservationStartedAtUtc is not null)) row.RefreshDuration();
            if (IsShowingMap)
                DetailSummary = new(DetailSummary.Summary, ActivityProjection.GetRates(DetailSummary.Summary,
                    SelectedMap!.DurationSeconds, DetailSummary.Rates.CombatDurationSeconds), true, SelectedMap.Source.Visits > 0);
            else if (SelectedSession is { Source.State: ActivityState.Open } open && SelectedEncounter is null)
                DetailSummary = new(DetailSummary.Summary, ActivityProjection.GetRates(DetailSummary.Summary, open.Source,
                    DetailSummary.Rates.CombatDurationSeconds, DateTime.UtcNow), true);
        }
        if (SelectedTabIndex == 1 && historyReadVersion == historyChangeVersion &&
            (SelectedSession is null || detailReadVersion == detailChangeVersion)) return;
        if (SelectedTabIndex == 2 && itemReadVersion == itemChangeVersion) return;
        await RunSafelyAsync(RefreshVisibleAsync);
    }

    private void UpdateStatus()
    {
        if (tracker is null || persistence is null) return;
        IsRecordingEnabled = tracker.IsRecordingEnabled;
        HasCurrentSession = tracker.CurrentSessionId is not null;
        HasCurrentEncounter = tracker.CurrentEncounterId is not null;
        var status = persistence.Status;
        StatusText = status.IsSuspended ? "Recording paused"
            : !IsRecordingEnabled ? "Recording off" : tracker.IsAwaitingContext ? "Waiting for character"
            : tracker.IsCaptureSuspended ? "Recording paused" : "Recording";
        if (status.Error is { Length: > 0 }) ErrorText = "Activity could not be saved. Recording will resume when storage is available.";
        if (statusSessionId != tracker.CurrentSessionId)
        {
            statusSessionId = tracker.CurrentSessionId;
            OnPropertyChanged(nameof(CurrentSessionId));
            OnPropertyChanged(nameof(CanDeleteSelectedSession));
        }
        if (statusLegacyImportWarning != HasLegacyImportWarning)
        {
            statusLegacyImportWarning = HasLegacyImportWarning;
            OnPropertyChanged(nameof(HasLegacyImportWarning));
        }
    }

    private async Task RefreshVisibleAsync()
    {
        if (persistence is null || tracker is null || refreshing || isDisposed) return;
        refreshing = true;
        try
        {
            if (SelectedTabIndex == 0)
            {
                await RefreshCurrentAsync();
                await RefreshLiveTimelineSafelyAsync();
            }
            else if (SelectedTabIndex == 1)
            {
                if (historyReadVersion != historyChangeVersion) await LoadSessionsAsync(false, true);
                if (SelectedSession is not null && detailReadVersion != detailChangeVersion) await RefreshDetailsAsync();
            }
            else if (SelectedTabIndex == 2 && itemReadVersion != itemChangeVersion) await LoadGlobalLootAsync(false, true);
            else if (SelectedTabIndex == 3) await RefreshCleanupAsync();
        }
        finally { refreshing = false; }
    }

    private async Task RefreshCurrentAsync()
    {
        if (persistence is null || tracker is null) return;
        var context = tracker.GetCurrentContext();
        var sessionId = context.Session?.Id;
        var scope = SelectedScope.Value;
        var period = SelectedPeriod.Value;
        var hours = LastHours;
        var cancellationToken = BeginRead(ref currentQueryCancellation);
        var sessionTask = period == ActivityPeriodMode.CurrentSession && sessionId is { } currentSessionId
            ? persistence.GetSessionAsync(currentSessionId, cancellationToken) : Task.FromResult<ActivitySession?>(null);
        var combatQuery = period == ActivityPeriodMode.CurrentSession && sessionId is { } combatSessionId
            ? new ActivityQuery { SessionId = combatSessionId, Scope = scope } : null;
        var combatVersion = currentCombatChangeVersion;
        var combatTask = combatQuery is null ? Task.FromResult(0d) : combatQuery == currentCombatQuery && combatVersion == currentCombatReadVersion
            ? Task.FromResult(currentCombatSeconds) : persistence.GetCombatDurationSecondsAsync(combatQuery, cancellationToken);
        await Task.WhenAll(sessionTask, combatTask);
        cancellationToken.ThrowIfCancellationRequested();
        if (isDisposed || !isActive || SelectedTabIndex != 0 || SelectedScope.Value != scope ||
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
        if (period == ActivityPeriodMode.CurrentSession && context.Owner is not null && currentSession is { } current)
        {
            var summary = ActivityProjection.GetSummary(current, scope);
            CurrentSummary = new(summary, ActivityProjection.GetRates(summary, current, combatTask.Result, DateTime.UtcNow), true);
            CurrentDurationText = $"Session time: {ActivityDisplay.Duration(ActivityProjection.GetDurationSeconds(current, DateTime.UtcNow))}";
            CurrentPeriodDescription = $"Since {ActivityDisplay.LocalDate(current.StartedAtUtc)}";
        }
        else if (period == ActivityPeriodMode.CurrentSession) ClearCurrentPeriod();
        if (period == ActivityPeriodMode.CurrentSession)
        {
            CurrentPeriodWarning = "";
            await RefreshCurrentResourcesAsync(context.Owner is null ? null : sessionId, scope, cancellationToken);
        }
        else await RefreshCurrentPeriodAsync(cancellationToken);
        await RefreshCurrentLootAsync();
    }

    private ActivityQuery HistoryQuery() => new() { Scope = SelectedScope.Value };

    private ActivityQuery DetailQuery(bool lootOnly = false) => new()
    {
        SessionId = SelectedSession?.Id,
        EncounterId = SelectedEncounter?.Id,
        ActorKey = SelectedPlayer?.ActorId,
        Scope = lootOnly ? SelectedSessionLootScope.Value : SelectedScope.Value,
        ItemsOnly = lootOnly,
        ItemSources = lootOnly ? SelectedSessionItemSource.Value : ActivityItemSources.All
    };

    private async Task LoadSessionsAsync(bool append, bool refresh = false)
    {
        if (persistence is null) return;
        var version = ++sessionListVersion;
        var dataVersion = historyChangeVersion;
        var cancellationToken = BeginRead(ref historyQueryCancellation);
        var query = HistoryQuery();
        var page = append ? sessionPage + 1 : 0;
        var changedIds = changedHistorySessions.ToArray();
        var targetedRefresh = refresh && !append && query == loadedHistoryQuery && Sessions.Count > 0;
        ActivityPage<ActivitySession> result;
        if (targetedRefresh)
        {
            var firstPageTask = persistence.GetSessionsAsync(query, 0, cancellationToken);
            var changedTask = persistence.GetSessionsByIdsAsync(query, changedIds, cancellationToken);
            var tailPage = (Sessions.Count - 1) / 100;
            var tailTask = !HasMoreSessions && Sessions.Count > 100 ? persistence.GetSessionsAsync(query, tailPage, cancellationToken) : null;
            var reads = new List<Task> { firstPageTask, changedTask };
            if (tailTask is not null) reads.Add(tailTask);
            await Task.WhenAll(reads);
            cancellationToken.ThrowIfCancellationRequested();
            var refreshed = firstPageTask.Result.Items.Concat(changedTask.Result);
            var invalidatedIds = changedIds.ToHashSet();
            var retained = Sessions.Where(row => !invalidatedIds.Contains(row.Id)).Select(row => row.Source);
            var capacity = Math.Max(Sessions.Count, 100);
            var merged = refreshed.Concat(retained).DistinctBy(source => source.Id)
                .OrderByDescending(source => source.StartedAtUtc).ThenBy(source => source.Id).ToArray();
            var hasMore = HasMoreSessions || merged.Length > capacity || Sessions.Count <= 100 && firstPageTask.Result.HasMore ||
                tailTask?.Result is { } tail && (tail.HasMore || tailPage * 100 + tail.Items.Count > capacity);
            result = new(merged.Take(capacity).ToArray(), hasMore);
        }
        else result = await persistence.GetSessionsAsync(query, page, cancellationToken);
        if (isDisposed || version != sessionListVersion || HistoryQuery() != query) return;
        var selectedId = SelectedSession?.Id;
        applyingSelection = true;
        try
        {
            ApplyHeaderRows(Sessions, result.Items, row => row.Id, source => source.Id,
                source => new ActivitySessionRow(source, query.Scope), (row, source) => row.Update(source, query.Scope), append);
            SelectedSession = selectedId is { } id ? Sessions.FirstOrDefault(row => row.Id == id) : null;
            // A targeted deletion/filter change can shorten the cached tail. Allow the
            // next offset page to overlap rather than skip rows not yet displayed.
            sessionPage = Math.Max(0, Sessions.Count / 100 - 1);
            HasMoreSessions = result.HasMore;
            loadedHistoryQuery = query;
            OnPropertyChanged(nameof(HistoryCountText));
            NotifyDetailProperties();
            if (!append)
            {
                historyReadVersion = dataVersion;
                if (historyChangeVersion == dataVersion) changedHistorySessions.ExceptWith(changedIds);
            }
        }
        finally { applyingSelection = false; }
    }

    private async Task RefreshDetailsAsync()
    {
        if (persistence is null || SelectedSession is null) return;
        var version = ++detailVersion;
        var dataVersion = detailChangeVersion;
        var cancellationToken = BeginRead(ref detailQueryCancellation);
        var sessionId = SelectedSession.Id;
        var encounterId = SelectedEncounter?.Id;
        var scope = SelectedScope.Value;
        var query = DetailQuery();
        var detailTab = SelectedDetailTabIndex;
        var isImported = IsImportedDetail;
        var cacheVersion = detailDataVersion;
        var useCache = cachedDetailQuery == query && cachedDetailDataVersion == cacheVersion && cachedDetailSummary is not null;
        var sessionTask = useCache ? Task.FromResult(cachedDetailSession) : persistence.GetSessionAsync(sessionId, cancellationToken);
        var encounterTask = useCache ? Task.FromResult(cachedDetailEncounter) : encounterId is { } id
            ? persistence.GetEncounterAsync(id, cancellationToken) : Task.FromResult<ActivityEncounter?>(null);
        await Task.WhenAll(sessionTask, encounterTask);
        if (!IsCurrentDetail(version, sessionId) || DetailQuery() != query || SelectedDetailTabIndex != detailTab || sessionTask.Result is not { } selected) return;
        var encounter = encounterTask.Result;
        applyingSelection = true;
        try
        {
            var sessionRow = Sessions.FirstOrDefault(row => row.Id == sessionId) ?? SelectedSession ?? new ActivitySessionRow(selected, scope);
            sessionRow.Update(selected, scope);
            if (!Sessions.Contains(sessionRow)) Sessions.Insert(0, sessionRow);
            SelectedSession = sessionRow;
            if (encounter is not null)
            {
                var encounterRow = Encounters.FirstOrDefault(row => row.Id == encounter.Id) ?? SelectedEncounter ?? new ActivityEncounterRow(encounter, scope);
                encounterRow.Update(encounter, scope);
                if (!Encounters.Contains(encounterRow)) Encounters.Add(encounterRow);
                SelectedEncounter = encounterRow;
            }
            else SelectedEncounter = null;
        }
        finally { applyingSelection = false; }
        if (detailTab == MapsDetailTabIndex)
        {
            await RefreshMapsAsync(version, selected, cancellationToken);
            if (IsCurrentDetail(version, sessionId) && SelectedDetailTabIndex == detailTab)
                detailReadVersion = dataVersion;
            return;
        }
        // Cached scope summaries describe all events assigned to this header. Actor,
        // time, item, and category filters must still use the event projection.
        var wholeHeader = query == new ActivityQuery { SessionId = sessionId, EncounterId = encounterId, Scope = scope };
        var summaryTask = useCache ? Task.FromResult(cachedDetailSummary!) : wholeHeader
            ? Task.FromResult(encounter is null ? ActivityProjection.GetSummary(selected, scope) : ActivityProjection.GetSummary(encounter, scope))
            : persistence.GetSummaryAsync(query, cancellationToken);
        var combatTask = useCache ? Task.FromResult(cachedDetailCombatSeconds) : encounter is null
            ? persistence.GetCombatDurationSecondsAsync(query, cancellationToken) : Task.FromResult(encounter.CombatDurationSeconds);
        var legacyTask = useCache ? Task.FromResult(cachedDetailLegacyInfo) : isImported
            ? persistence.GetLegacyImportInfoAsync(sessionId, cancellationToken) : Task.FromResult<ActivityLegacyImportInfo?>(null);
        await Task.WhenAll(summaryTask, combatTask, legacyTask);
        if (!IsCurrentDetail(version, selected.Id) || DetailQuery() != query || SelectedDetailTabIndex != detailTab) return;
        var summary = summaryTask.Result;
        cachedDetailQuery = query;
        cachedDetailDataVersion = cacheVersion;
        cachedDetailSession = selected;
        cachedDetailEncounter = encounter;
        cachedDetailSummary = summary;
        cachedDetailCombatSeconds = combatTask.Result;
        cachedDetailLegacyInfo = legacyTask.Result;
        LegacyImportInfo = legacyTask.Result;
        DetailSummary = new(summary, encounter is null ? ActivityProjection.GetRates(summary, selected, combatTask.Result, DateTime.UtcNow) : ActivityProjection.GetRates(summary, encounter), encounter is null);
        NotifyDetailProperties();
        if (detailTab == 0) await LoadEncountersAsync(false, true);
        else if (detailTab == 1) await LoadChartAsync(version, query);
        else if (detailTab == 2)
        {
            var totals = await persistence.GetPlayersAsync(query with { ActorKey = null }, cancellationToken);
            if (!IsCurrentDetail(version, selected.Id) || DetailQuery() != query || SelectedDetailTabIndex != detailTab) return;
            var actorId = SelectedPlayer?.ActorId;
            applyingSelection = true;
            try
            {
                Players = new(totals.Select(total => new ActivityPlayerRow(total)));
                SelectedPlayer = actorId is null ? null : Players.FirstOrDefault(player => player.ActorId == actorId);
            }
            finally { applyingSelection = false; }
        }
        else if (detailTab == 3)
        {
            var totals = await persistence.GetMobsAsync(query, cancellationToken);
            if (IsCurrentDetail(version, selected.Id) && DetailQuery() == query && SelectedDetailTabIndex == detailTab) Mobs = new(totals.Select(total => new ActivityMobRow(total)));
        }
        else if (detailTab == 4) await LoadSessionLootAsync(false, version, true);
        else if (detailTab == 5) await LoadTimelineAsync(false, version, true);
        if (IsCurrentDetail(version, sessionId) && DetailQuery() == query && SelectedDetailTabIndex == detailTab)
            detailReadVersion = dataVersion;
    }

    private bool IsCurrentDetail(long version, Guid sessionId) => !isDisposed && detailVersion == version && SelectedSession?.Id == sessionId;

    private async Task LoadEncountersAsync(bool append, bool refresh = false)
    {
        if (persistence is null || SelectedSession is null) return;
        var sessionId = SelectedSession.Id;
        var version = detailVersion;
        var scope = SelectedScope.Value;
        var cancellationToken = detailQueryCancellation.Token;
        var page = append ? encounterPage + 1 : refresh ? encounterPage : 0;
        var result = await ReadPagesAsync(index => persistence.GetEncountersAsync(sessionId, index, cancellationToken), page, append);
        if (!IsCurrentDetail(version, sessionId) || SelectedScope.Value != scope) return;
        var selectedEncounter = SelectedEncounter;
        var selectedId = selectedEncounter?.Id;
        applyingSelection = true;
        try
        {
            var headers = selectedEncounter is not null && !result.Items.Any(source => source.Id == selectedId)
                ? result.Items.Append(selectedEncounter.Source).ToArray() : result.Items;
            ApplyHeaderRows(Encounters, headers, row => row.Id, source => source.Id,
                source => new ActivityEncounterRow(source, scope), (row, source) => row.Update(source, scope), append);
            SelectedEncounter = selectedId is { } id ? Encounters.FirstOrDefault(row => row.Id == id) : null;
            encounterPage = page;
            HasMoreEncounters = result.HasMore;
        }
        finally { applyingSelection = false; }
    }

    private async Task LoadChartAsync(long version, ActivityQuery query)
    {
        if (persistence is null || query.SessionId is not { } sessionId) return;
        var categories = VisibleCategories;
        var selectedEncounter = SelectedEncounter?.Source;
        var selectedSession = SelectedSession?.Source;
        if (selectedSession is null) return;
        var fromUtc = selectedEncounter?.StartedAtUtc ?? selectedSession.StartedAtUtc;
        var toUtc = selectedEncounter is not null ? selectedEncounter.ClosedAtUtc ?? selectedEncounter.LastObservedAtUtc :
            selectedSession.ClosedAtUtc ?? selectedSession.LastObservedAtUtc;
        var snapshot = await persistence.GetTimelineAsync(query with { ShowCategories = categories },
            fromUtc, toUtc, detailQueryCancellation.Token);
        if (!IsCurrentDetail(version, sessionId) || VisibleCategories != categories || DetailQuery() != query) return;
        HistoryLiveTimeline = snapshot with { ContextKey = HistoryTimelineContextKey(query, categories) };
    }

    private async Task LoadSessionLootAsync(bool append, long? version = null, bool refresh = false)
    {
        if (persistence is null || SelectedSession is null) return;
        var query = DetailQuery(true);
        var cancellationToken = detailQueryCancellation.Token;
        var sessionId = SelectedSession.Id;
        var page = append ? sessionLootPage + 1 : refresh ? sessionLootPage : 0;
        var result = await ReadEventRowsAsync(query with { LootOnly = true }, page, append, cancellationToken);
        if (isDisposed || SelectedSession?.Id != sessionId || DetailQuery(true) != query || version is { } expected && detailVersion != expected) return;
        ApplyEventRows(SessionLoot, result.Items.Select(observation => new ActivityLootRow(observation)).ToArray(), row => row.SourceEvent, append);
        valuations?.RequestMissing(result.Items);
        sessionLootPage = page;
        HasMoreSessionLoot = result.HasMore;
        await RefreshSessionResourcesAsync(query, version ?? detailVersion);
    }

    private async Task LoadTimelineAsync(bool append, long? version = null, bool refresh = false)
    {
        if (persistence is null || SelectedSession is null) return;
        var sessionId = SelectedSession.Id;
        var page = append ? timelinePage + 1 : refresh ? timelinePage : 0;
        var query = DetailQuery() with { ShowCategories = VisibleCategories };
        var cancellationToken = detailQueryCancellation.Token;
        var result = await ReadEventRowsAsync(query, page, append, cancellationToken);
        if (isDisposed || SelectedSession?.Id != sessionId || (DetailQuery() with { ShowCategories = VisibleCategories }) != query || version is { } expected && detailVersion != expected) return;
        ApplyEventRows(Timeline, result.Items.Where(observation => observation.Kind is not (ActivityEventKind.OutcomeEvidence or ActivityEventKind.LegacyImportMarker))
            .Select(observation => new ActivityEventRow(observation)).ToArray(), row => row.SourceEvent, append);
        timelinePage = page;
        HasMoreTimeline = result.HasMore;
    }

    private async Task LoadGlobalLootAsync(bool append, bool refresh = false)
    {
        if (persistence is null) return;
        var version = ++globalLootVersion;
        var dataVersion = itemChangeVersion;
        var cancellationToken = BeginRead(ref itemsQueryCancellation);
        var page = append ? globalLootPage + 1 : refresh ? globalLootPage : 0;
        var query = GlobalItemsQuery();
        var result = await ReadEventRowsAsync(query with { LootOnly = true }, page, append, cancellationToken);
        var summary = append ? null : await persistence.GetSummaryAsync(query, cancellationToken);
        var resources = append ? null : await persistence.GetResourceTotalsAsync(query, cancellationToken);
        if (isDisposed || version != globalLootVersion || GlobalItemsQuery() != query) return;
        ApplyEventRows(GlobalLoot, result.Items.Select(observation => new ActivityLootRow(observation)).ToArray(), row => row.SourceEvent, append);
        displayedGlobalLootQuery = query with { LootOnly = true };
        valuations?.RequestMissing(result.Items);
        if (resources is not null) GlobalResources = new(resources.Select(row => new ActivityResourceRow(row)));
        globalLootPage = page;
        HasMoreGlobalLoot = result.HasMore;
        if (summary is not null)
        {
            GlobalLootSummary = new(summary, ActivityProjection.GetRates(summary, 0));
            OnPropertyChanged(nameof(GlobalLootTotalsText));
        }
        OnPropertyChanged(nameof(LootCountText));
        if (!append) itemReadVersion = dataVersion;
    }

    private ActivityQuery GlobalItemsQuery() => new()
    {
        Scope = SelectedLootScope.Value,
        ItemsOnly = true,
        ItemSources = SelectedItemSource.Value,
        Search = EmptyToNull(LootSearch)
    };

    private static CancellationToken BeginRead(ref CancellationTokenSource source)
    {
        source.Cancel();
        source.Dispose();
        source = new CancellationTokenSource();
        return source.Token;
    }

    private static async Task<ActivityPage<T>> ReadPagesAsync<T>(Func<int, Task<ActivityPage<T>>> readPage, int lastPage, bool append)
    {
        if (append) return await readPage(lastPage);
        var pages = await Task.WhenAll(Enumerable.Range(0, lastPage + 1).Select(readPage));
        return new(pages.SelectMany(page => page.Items).ToArray(), pages[^1].HasMore);
    }

    private Task<ActivityPage<ActivityEvent>> ReadEventRowsAsync(ActivityQuery query, int lastPage, bool append,
        CancellationToken cancellationToken) => append
        ? persistence!.GetEventsAsync(query, lastPage, cancellationToken)
        : persistence!.GetEventsThroughPageAsync(query, lastPage, cancellationToken);

    private static void ApplyHeaderRows<TRow, TSource, TKey>(ObservableCollection<TRow> rows, IReadOnlyList<TSource> sources,
        Func<TRow, TKey> rowId, Func<TSource, TKey> sourceId, Func<TSource, TRow> create, Action<TRow, TSource> update, bool append) where TKey : notnull
    {
        if (!append)
        {
            var wantedIds = sources.Select(sourceId).ToHashSet();
            for (var index = rows.Count - 1; index >= 0; index--)
                if (!wantedIds.Contains(rowId(rows[index]))) rows.RemoveAt(index);
        }
        var byId = rows.ToDictionary(rowId);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var id = sourceId(source);
            if (!append && index < rows.Count && EqualityComparer<TKey>.Default.Equals(rowId(rows[index]), id)) update(rows[index], source);
            else if (byId.TryGetValue(id, out var existing))
            {
                update(existing, source);
                if (!append) rows.Move(rows.IndexOf(existing), index);
            }
            else
            {
                var row = create(source);
                byId.Add(id, row);
                if (append) rows.Add(row);
                else rows.Insert(index, row);
            }
        }
        if (!append) while (rows.Count > sources.Count) rows.RemoveAt(rows.Count - 1);
    }

    private static void ApplyEventRows<T>(ObservableCollection<T> rows, IReadOnlyList<T> replacements, Func<T, ActivityEvent> eventOf, bool append)
    {
        if (append)
        {
            var indices = rows.Select((row, index) => (Id: eventOf(row).Id, Index: index)).ToDictionary(value => value.Id, value => value.Index);
            foreach (var replacement in replacements)
            {
                var id = eventOf(replacement).Id;
                if (indices.TryGetValue(id, out var index))
                {
                    if (eventOf(rows[index]) != eventOf(replacement)) rows[index] = replacement;
                }
                else { indices.Add(id, rows.Count); rows.Add(replacement); }
            }
            return;
        }
        for (var i = 0; i < replacements.Count; i++)
        {
            if (i >= rows.Count) rows.Add(replacements[i]);
            else if (eventOf(rows[i]) != eventOf(replacements[i])) rows[i] = replacements[i];
        }
        while (rows.Count > replacements.Count) rows.RemoveAt(rows.Count - 1);
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        if (isDisposed) return;
        loadingCount++;
        IsLoading = true;
        try
        {
            await action();
            // Chart and replay reads handle their own failures and recovery. A successful
            // surrounding refresh must not immediately dismiss their warning.
            if (persistence?.Status.Error is not { Length: > 0 } && ErrorText is
                "Could not load activity. Try reopening this tab." or
                "Activity could not be saved. Recording will resume when storage is available.")
                ErrorText = "";
        }
        catch (OperationCanceledException) { /* Superseded view requests are expected. */ }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to load activity history");
            if (!isDisposed) ErrorText = "Could not load activity. Try reopening this tab.";
        }
        finally
        {
            loadingCount--;
            if (!isDisposed) IsLoading = loadingCount > 0;
        }
    }

    private void NotifyDetailProperties()
    {
        OnPropertyChanged(nameof(HasSelectedSession));
        OnPropertyChanged(nameof(HasSelectedEncounter));
        OnPropertyChanged(nameof(HasSelectedDetailSelection));
        OnPropertyChanged(nameof(HasSelectedPlayer));
        OnPropertyChanged(nameof(CanDeleteSelectedSession));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailMapMetadataText));
        OnPropertyChanged(nameof(HasDetailMapMetadata));
        OnPropertyChanged(nameof(CanExportSessionLoot));
        OnPropertyChanged(nameof(ShowSessionLootPanel));
        OnPropertyChanged(nameof(DetailDurationText));
        OnPropertyChanged(nameof(DetailScopeText));
        OnPropertyChanged(nameof(DetailEarningsText));
        OnPropertyChanged(nameof(ChartDescription));
        OnPropertyChanged(nameof(DetailTagsText));
        OnPropertyChanged(nameof(IsImportedDetail));
        OnPropertyChanged(nameof(HasDetailedHistory));
        OnPropertyChanged(nameof(ImportedDetailText));
        OnPropertyChanged(nameof(ImportedDetailTooltipText));
    }

    partial void OnSelectedScopeChanged(ActivityOption<ActivityScope> value)
    {
        currentQueryCancellation.Cancel();
        liveTimelineCancellation.Cancel();
        currentResourcesQuery = null;
        ClearCurrentPeriod();
        CurrentLiveTimeline = null;
        ++historyChangeVersion; ++detailChangeVersion; ++itemChangeVersion;
        ++sessionListVersion; ++detailVersion; ++globalLootVersion;
        ResetEventPages();
        if (SelectedSessionLootScope.Value != ActivityScope.AllObserved) SelectedSessionLootScope = value;
        if (SelectedLootScope.Value != ActivityScope.AllObserved) SelectedLootScope = value;
        NotifyDetailProperties();
        if (isActive && !applyingSelection) _ = RunSafelyAsync(RefreshVisibleAsync);
    }
    partial void OnSelectedLootScopeChanged(ActivityOption<ActivityScope> value)
    { ++itemChangeVersion; ++globalLootVersion; globalLootPage = 0; if (isActive && SelectedTabIndex == 2) _ = RunSafelyAsync(() => LoadGlobalLootAsync(false)); }
    partial void OnSelectedSessionLootScopeChanged(ActivityOption<ActivityScope> value)
    {
        ++detailChangeVersion; ++detailVersion;
        sessionLootPage = 0;
        SessionLoot.Clear();
        if (isActive && SelectedTabIndex == 1 && SelectedDetailTabIndex == 4) _ = RunSafelyAsync(RefreshDetailsAsync);
    }
    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowActivityScope));
        if (value != 1) CancelHistoryReplayRead();
        if (value != 0) { currentQueryCancellation.Cancel(); liveTimelineCancellation.Cancel(); }
        if (value != 1) { historyQueryCancellation.Cancel(); detailQueryCancellation.Cancel(); }
        else
        {
            if (historyQueryCancellation.IsCancellationRequested) BeginRead(ref historyQueryCancellation);
            if (detailQueryCancellation.IsCancellationRequested) BeginRead(ref detailQueryCancellation);
        }
        if (value != 2) itemsQueryCancellation.Cancel();
        if (value == 3) cleanupReadVersion = -1;
        if (isActive && !applyingSelection) _ = RunSafelyAsync(RefreshVisibleAsync);
    }
    partial void OnSelectedDetailTabIndexChanged(int oldValue, int newValue)
    {
        if (newValue != 1) CancelHistoryReplayRead();
        if (oldValue == MapsDetailTabIndex || newValue == MapsDetailTabIndex)
            DetailSummary = ActivitySummaryPresentation.Empty;
        if (newValue == MapsDetailTabIndex && !applyingSelection)
        {
            applyingSelection = true;
            try { SelectedEncounter = null; SelectedPlayer = null; }
            finally { applyingSelection = false; }
        }
        NotifyDetailProperties();
        ++detailChangeVersion; ++detailVersion;
        if (isActive && !applyingSelection) _ = RunSafelyAsync(RefreshDetailsAsync);
    }
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsRecordingEnabledChanged(bool value) => OnPropertyChanged(nameof(RecordingButtonText));
    partial void OnCurrentSummaryChanged(ActivitySummaryPresentation value) { OnPropertyChanged(nameof(CurrentCombatDurationText)); OnPropertyChanged(nameof(CurrentTagsText)); }
    partial void OnDetailSummaryChanged(ActivitySummaryPresentation value) => NotifyDetailProperties();
    partial void OnGlobalLootSummaryChanged(ActivitySummaryPresentation value)
    {
        OnPropertyChanged(nameof(GlobalLootTotalsText));
        OnPropertyChanged(nameof(GlobalLootDetailsText));
    }
    partial void OnSelectedSessionChanged(ActivitySessionRow? value)
    {
        NotifyDetailProperties();
        if (applyingSelection) return;
        ++detailChangeVersion;
        applyingSelection = true;
        try
        {
            SelectedEncounter = null;
            SelectedPlayer = null;
            ResetMaps();
            if (IsImportedDetail) SelectedDetailTabIndex = 4;
        }
        finally { applyingSelection = false; }
        Encounters.Clear(); Players.Clear(); Mobs.Clear(); SessionLoot.Clear(); Timeline.Clear(); HistoryLiveTimeline = null;
        encounterPage = timelinePage = sessionLootPage = 0;
        HasMoreEncounters = HasMoreSessionLoot = HasMoreTimeline = false;
        DetailSummary = ActivitySummaryPresentation.Empty;
        SessionResources = new();
        LegacyImportInfo = null;
        ++detailVersion;
        if (value is not null) _ = RunSafelyAsync(RefreshDetailsAsync);
    }
    partial void OnSelectedEncounterChanged(ActivityEncounterRow? value)
    { NotifyDetailProperties(); if (!applyingSelection) { ++detailChangeVersion; ++detailVersion; ResetEventPages(); _ = RunSafelyAsync(RefreshDetailsAsync); } }
    partial void OnSelectedPlayerChanged(ActivityPlayerRow? value)
    { NotifyDetailProperties(); if (!applyingSelection) { ++detailChangeVersion; ++detailVersion; ResetEventPages(); _ = RunSafelyAsync(RefreshDetailsAsync); } }

    private void ResetEventPages()
    {
        timelinePage = sessionLootPage = 0;
        SessionLoot.Clear(); Timeline.Clear(); HistoryLiveTimeline = null;
        HasMoreSessionLoot = HasMoreTimeline = false;
    }

    [RelayCommand] private void ToggleRecording() { tracker?.SetRecordingEnabled(!IsRecordingEnabled); UpdateStatus(); }
    public void FinishCurrentSession(Guid? expectedSessionId = null) { tracker?.FinishSession(expectedSessionId); UpdateStatus(); }
    [RelayCommand] private void FinishSession() => FinishCurrentSession();
    [RelayCommand]
    private async Task OpenCurrentSessionAsync()
    {
        if (tracker?.CurrentSessionId is { } id) await RunSafelyAsync(() => OpenSessionDetailsAsync(id));
    }

    private async Task OpenSessionDetailsAsync(Guid id, Guid? encounterId = null)
    {
        if (persistence is null) return;
        var session = await persistence.GetSessionAsync(id);
        if (isDisposed || session is null) return;
        applyingSelection = true;
        try
        {
            SelectedTabIndex = 1;
        }
        finally { applyingSelection = false; }
        await LoadSessionsAsync(false);
        var row = Sessions.FirstOrDefault(existing => existing.Id == id);
        if (row is null) { row = new(session, SelectedScope.Value); Sessions.Insert(0, row); }
        var encounter = encounterId is { } selectedEncounterId ? await persistence.GetEncounterAsync(selectedEncounterId) : null;
        if (isDisposed) return;
        applyingSelection = true;
        try
        {
            SelectedSession = row;
            SelectedPlayer = null;
            ResetMaps();
            Encounters.Clear(); Players.Clear(); Mobs.Clear(); SessionLoot.Clear(); Timeline.Clear(); HistoryLiveTimeline = null;
            encounterPage = timelinePage = sessionLootPage = 0;
            HasMoreEncounters = HasMoreSessionLoot = HasMoreTimeline = false;
            SessionResources = new();
            LegacyImportInfo = null;
            SelectedEncounter = encounter is null ? null : new ActivityEncounterRow(encounter, SelectedScope.Value);
            if (SelectedEncounter is not null) Encounters.Add(SelectedEncounter);
            if (SelectedEncounter is not null && SelectedDetailTabIndex == MapsDetailTabIndex) SelectedDetailTabIndex = 0;
            if (IsImportedDetail) SelectedDetailTabIndex = 4;
            ++detailChangeVersion;
            ++detailVersion;
            cachedDetailQuery = null;
        }
        finally { applyingSelection = false; }
        await RefreshDetailsAsync();
    }
    [RelayCommand]
    private async Task ApplyLootFiltersAsync() => await RunSafelyAsync(() =>
    { ++itemChangeVersion; return LoadGlobalLootAsync(false); });
    [RelayCommand] private async Task LoadMoreSessionsAsync() => await RunSafelyAsync(() => LoadSessionsAsync(true));
    [RelayCommand] private async Task LoadMoreEncountersAsync() => await RunSafelyAsync(() => LoadEncountersAsync(true));
    [RelayCommand] private async Task LoadMoreTimelineAsync() => await RunSafelyAsync(() => LoadTimelineAsync(true));
    [RelayCommand] private async Task LoadMoreSessionLootAsync() => await RunSafelyAsync(() => LoadSessionLootAsync(true));
    [RelayCommand] private async Task LoadMoreGlobalLootAsync() => await RunSafelyAsync(() => LoadGlobalLootAsync(true));
    [RelayCommand]
    private void ClearEncounter()
    {
        if (IsShowingMap) SelectedMap = null;
        else SelectedEncounter = null;
    }
    [RelayCommand] private void ClearPlayer() => SelectedPlayer = null;

    public async Task DeleteSelectedSessionAsync()
    {
        if (persistence is null || SelectedSession is null || !CanDeleteSelectedSession) return;
        var id = SelectedSession.Id;
        await RunSafelyAsync(async () =>
        {
            await persistence.DeleteSessionAsync(id);
            SelectedSession = null;
            await LoadSessionsAsync(false);
        });
    }

    public async Task DeleteUnassignedLootAsync()
    {
        if (persistence is null) return;
        await RunSafelyAsync(async () => { await persistence.DeleteUnassignedLootAsync(); await LoadGlobalLootAsync(false); });
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        DisposeCleanup();
        DisposeHistoryReplay();
        lootExportCancellation?.Cancel();
        ++detailVersion;
        historyQueryCancellation.Cancel(); detailQueryCancellation.Cancel(); itemsQueryCancellation.Cancel();
        historyQueryCancellation.Dispose(); detailQueryCancellation.Dispose(); itemsQueryCancellation.Dispose();
        currentQueryCancellation.Cancel(); currentQueryCancellation.Dispose();
        CancelCurrentLootRead(); currentLootCancellation.Dispose();
        liveTimelineCancellation.Cancel();
        liveTimelineCancellation.Dispose();
        refreshTimer.Stop();
        refreshTimer.Tick -= OnRefreshTick;
        if (tracker is not null) tracker.Changed -= OnActivityChanged;
        if (persistence is not null)
        {
            persistence.StatusChanged -= OnPersistenceStatusChanged;
            persistence.DataChanged -= OnCommittedDataChanged;
        }
    }
}

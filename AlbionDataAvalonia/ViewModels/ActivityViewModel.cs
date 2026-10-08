using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Serilog;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel : ViewModelBase, IDisposable
{
    private readonly ActivityTrackerService? tracker;
    private readonly ActivityPersistence? persistence;
    private readonly DispatcherTimer refreshTimer;
    private bool isActive;
    private bool isDisposed;
    private bool refreshPending = true;
    private bool refreshing;
    private bool applyingSelection;
    private int loadingCount;
    private int sessionPage;
    private int encounterPage;
    private int timelinePage;
    private int sessionLootPage;
    private int globalLootPage;
    private long detailVersion;
    private long sessionListVersion;
    private long globalLootVersion;
    private ActivitySession? currentSession;
    private ActivityEncounter? currentEncounter;

    public IReadOnlyList<ActivityOption<ActivityScope>> ScopeOptions { get; } =
        [new(ActivityScope.Personal, "Personal"), new(ActivityScope.Party, "Party")];
    public IReadOnlyList<ActivityOption<ActivityScope>> LootScopeOptions { get; } =
        [new(ActivityScope.Personal, "Personal"), new(ActivityScope.Party, "Party"), new(ActivityScope.AllObserved, "All observed")];
    public IReadOnlyList<ActivityOption<string?>> ServerOptions { get; } =
        [new(null, "All servers"), new("1", "Americas"), new("2", "Asia"), new("3", "Europe")];
    public IReadOnlyList<ActivityOption<ActivitySessionKind?>> KindOptions { get; } =
        new[] { new ActivityOption<ActivitySessionKind?>(null, "All locations") }
            .Concat(Enum.GetValues<ActivitySessionKind>().Select(kind => new ActivityOption<ActivitySessionKind?>(kind, KindLabel(kind)))).ToArray();
    public IReadOnlyList<ActivityOption<ActivityChartMetric>> ChartMetricOptions { get; } =
        [new(ActivityChartMetric.DamageDealt, "Damage dealt"), new(ActivityChartMetric.DamageReceived, "Damage received"),
         new(ActivityChartMetric.HealingDone, "Healing done"), new(ActivityChartMetric.HealingReceived, "Healing received"),
         new(ActivityChartMetric.Fame, "Your observed fame"), new(ActivityChartMetric.Silver, "Silver earned"), new(ActivityChartMetric.LootValue, "Estimated loot value"), new(ActivityChartMetric.GatheringItems, "Harvested quantity"), new(ActivityChartMetric.FishingItems, "Fishing quantity"), new(ActivityChartMetric.ResourceValue, "Estimated gathering value")];

    [ObservableProperty] private ActivityOption<ActivityScope> selectedScope = new(ActivityScope.Personal, "Personal");
    [ObservableProperty] private ActivityOption<ActivityScope> selectedLootScope = new(ActivityScope.Personal, "Personal");
    [ObservableProperty] private ActivityOption<ActivityScope> selectedSessionLootScope = new(ActivityScope.Personal, "Personal");
    [ObservableProperty] private ActivityOption<ActivitySessionKind?> selectedKind = new(null, "All locations");
    [ObservableProperty] private ActivityOption<ActivityChartMetric> selectedChartMetric = new(ActivityChartMetric.DamageDealt, "Damage dealt");
    [ObservableProperty] private int selectedTabIndex;
    [ObservableProperty] private int selectedDetailTabIndex;
    [ObservableProperty] private DateTimeOffset? fromDate;
    [ObservableProperty] private DateTimeOffset? throughDate;
    [ObservableProperty] private ActivityOption<string?> selectedServer = new(null, "All servers");
    [ObservableProperty] private string characterFilter = "";
    [ObservableProperty] private string locationFilter = "";
    [ObservableProperty] private string lootSearch = "";
    [ObservableProperty] private string statusText = "Waiting for activity context.";
    [ObservableProperty] private string errorText = "";
    [ObservableProperty] private bool isRecordingEnabled = true;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string currentTitle = "No active session";
    [ObservableProperty] private string currentDescription = "Relevant activity starts a session automatically.";
    [ObservableProperty] private string currentDurationText = "Session time: 0:00";
    [ObservableProperty] private string currentEncounterHeading = "Last encounter";
    [ObservableProperty] private string currentEncounterText = "No encounters yet";
    [ObservableProperty] private string currentEncounterStateText = "An encounter appears when combat begins.";
    [ObservableProperty] private bool hasCurrentSession;
    [ObservableProperty] private bool hasCurrentEncounter;
    [ObservableProperty] private bool hasDisplayedEncounter;
    [ObservableProperty] private ActivitySummaryPresentation currentSummary = ActivitySummaryPresentation.Empty;
    [ObservableProperty] private ActivitySummaryPresentation currentEncounterSummary = ActivitySummaryPresentation.Empty;
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
    [ObservableProperty] private ISeries[] chartSeries = [];
    [ObservableProperty] private Axis[] chartXAxes = [];
    [ObservableProperty] private Axis[] chartYAxes = [];

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public Guid? CurrentSessionId => tracker?.CurrentSessionId;
    public string RecordingButtonText => IsRecordingEnabled ? "Disable recording" : "Enable recording";
    public bool HasSelectedSession => SelectedSession is not null;
    public bool HasSelectedEncounter => SelectedEncounter is not null;
    public bool HasSelectedPlayer => SelectedPlayer is not null;
    public bool CanDeleteSelectedSession => SelectedSession is { Source.State: not ActivityState.Open } && SelectedSession.Id != tracker?.CurrentSessionId;
    public string CurrentCombatDurationText => $"Combat time: {ActivityDisplay.Duration(CurrentSummary.Rates.CombatDurationSeconds)}";
    public string DetailTitle => SelectedEncounter is { } encounter ? $"Encounter {encounter.Number} · {encounter.Classification}" : SelectedSession?.Title ?? "Select a session";
    public string DetailDurationText => IsImportedDetail ? $"Recorded duration: {SelectedSession?.DurationText} · Imported summary" : SelectedEncounter is { } encounter ? $"Combat time: {encounter.DurationText}"
        : $"Session time: {SelectedSession?.DurationText ?? "0:00"} · Combat time: {ActivityDisplay.Duration(DetailSummary.Rates.CombatDurationSeconds)}";
    public string DetailScopeText => $"{SelectedScope.Label}{(SelectedPlayer is { } player ? $" · {player.Name}" : "")}{(SelectedSession?.Source.IsPartial == true ? " · partial capture" : "")}";
    public string DetailEarningsText => IsImportedDetail ? "Imported item totals · individual pickup times unavailable" : SelectedEncounter is not null ? "Items observed during this encounter" : "Items recorded during this session";
    public string HistoryCountText => $"{Sessions.Count:N0} sessions loaded";
    public string LootCountText => $"{GlobalLoot.Count:N0} item records loaded";
    public string GlobalLootTotalsText => $"{GlobalLootSummary.Summary.LootItems + GlobalLootSummary.Summary.ResourceItems:N0} items · {GlobalLootSummary.ItemsEstimateText.ToLowerInvariant()} {GlobalLootSummary.ItemsValue} silver{(GlobalLootSummary.HasMissingItemPrices ? $" · {GlobalLootSummary.MissingItemPrices}" : "")}";
    public string ChartDescription => SelectedEncounter is null ? "Whole session" : $"Encounter {SelectedEncounter.Number}";

    public ActivityViewModel()
    {
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refreshTimer.Tick += OnRefreshTick;
        ChartXAxes = [CreateTimeAxis()];
        ChartYAxes = [CreateAmountAxis("Amount")];
    }

    public ActivityViewModel(ActivityTrackerService tracker, ActivityPersistence persistence) : this()
    {
        this.tracker = tracker;
        this.persistence = persistence;
        tracker.Changed += OnActivityChanged;
        persistence.StatusChanged += OnPersistenceStatusChanged;
        UpdateStatus();
    }

    public void SetActive(bool active)
    {
        if (isDisposed || isActive == active) return;
        isActive = active;
        if (active)
        {
            refreshPending = true;
            refreshTimer.Start();
            _ = RunSafelyAsync(RefreshVisibleAsync);
        }
        else refreshTimer.Stop();
    }

    private void OnActivityChanged(object? sender, EventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (isDisposed) return;
            refreshPending = true;
            UpdateStatus();
        });
    }

    private void OnPersistenceStatusChanged(ActivityRecordingStatus status) => OnActivityChanged(persistence, EventArgs.Empty);

    private async void OnRefreshTick(object? sender, EventArgs args)
    {
        if (isDisposed || !isActive || refreshing || loadingCount > 0) return;
        UpdateStatus();
        if (currentSession is not null)
            CurrentDurationText = $"Session time: {ActivityDisplay.Duration(ActivityProjection.GetDurationSeconds(currentSession, DateTime.UtcNow))}";
        if (!refreshPending)
        {
            await RefreshLiveTimelineSafelyAsync();
            return;
        }
        await RunSafelyAsync(RefreshVisibleAsync);
    }

    private void UpdateStatus()
    {
        if (tracker is null || persistence is null) return;
        IsRecordingEnabled = tracker.IsRecordingEnabled;
        HasCurrentSession = tracker.CurrentSessionId is not null;
        HasCurrentEncounter = tracker.CurrentEncounterId is not null;
        var status = persistence.Status;
        StatusText = status.IsSuspended ? "Recording paused · storage unavailable"
            : !IsRecordingEnabled ? "Recording disabled" : tracker.IsAwaitingContext ? "Waiting for character and location"
            : tracker.IsCaptureSuspended ? "Capture paused" : "Recording";
        if (status.Error is { Length: > 0 }) ErrorText = status.Error;
        OnPropertyChanged(nameof(CanDeleteSelectedSession));
        OnPropertyChanged(nameof(HasLegacyImportWarning));
    }

    private async Task RefreshVisibleAsync()
    {
        if (persistence is null || tracker is null || refreshing || isDisposed) return;
        refreshing = true;
        refreshPending = false;
        try
        {
            await RefreshCurrentAsync();
            await RefreshLiveTimelineSafelyAsync();
            if (SelectedTabIndex == 1)
            {
                await LoadSessionsAsync(false, true);
                if (SelectedSession is not null) await RefreshDetailsAsync();
            }
            else if (SelectedTabIndex == 2) await LoadGlobalLootAsync(false, true);
        }
        finally { refreshing = false; }
    }

    private async Task RefreshCurrentAsync()
    {
        if (persistence is null || tracker is null) return;
        var sessionId = tracker.CurrentSessionId;
        var encounterId = tracker.CurrentEncounterId;
        var scope = SelectedScope.Value;
        var sessionTask = sessionId is { } id ? persistence.GetSessionAsync(id) : Task.FromResult<ActivitySession?>(null);
        var encounterTask = encounterId is { } fightId ? persistence.GetEncounterAsync(fightId)
            : sessionId is { } currentId ? persistence.GetLatestEncounterAsync(currentId) : Task.FromResult<ActivityEncounter?>(null);
        var combatTask = sessionId is { } combatSessionId
            ? persistence.GetCombatDurationSecondsAsync(new ActivityQuery { SessionId = combatSessionId, Scope = scope }) : Task.FromResult(0d);
        await Task.WhenAll(sessionTask, encounterTask, combatTask);
        currentSession = sessionTask.Result;
        currentEncounter = encounterTask.Result;
        if (isDisposed) return;
        CurrentTitle = currentSession is { } currentTitle ? ActivityDisplay.SessionTitle(currentTitle) : "No active session";
        CurrentDescription = currentSession is { } session ? $"{session.CharacterName} · {ActivityDisplay.Server(session.Server)} · {session.LocationName}{(session.IsPartial ? " · partial recording" : "")}"
            : tracker.IsAwaitingContext ? "Waiting for your character and location." : "Start playing to record your activity.";
        if (currentSession is { } current)
        {
            var summary = ActivityProjection.GetSummary(current, scope);
            CurrentSummary = new(summary, ActivityProjection.GetRates(summary, current, combatTask.Result, DateTime.UtcNow), true);
            CurrentDurationText = $"Session time: {ActivityDisplay.Duration(ActivityProjection.GetDurationSeconds(current, DateTime.UtcNow))}";
        }
        else { CurrentSummary = ActivitySummaryPresentation.Empty; CurrentDurationText = "Session time: 0:00"; }
        HasDisplayedEncounter = currentEncounter is not null;
        CurrentEncounterHeading = currentEncounter?.State == ActivityState.Open ? "Active encounter" : "Last encounter";
        if (currentEncounter is { } combat)
        {
            var summary = ActivityProjection.GetSummary(combat, SelectedScope.Value);
            CurrentEncounterSummary = new(summary, ActivityProjection.GetRates(summary, combat));
            CurrentEncounterText = $"Encounter {combat.Number} · {ActivityDisplay.EncounterKind(combat.Kind)} · {combat.LocationName}";
            CurrentEncounterStateText = $"{ActivityDisplay.State(combat.State, combat.EndReason)} · {ActivityDisplay.LocalTime(combat.StartedAtUtc)} · {ActivityDisplay.Duration(combat.CombatDurationSeconds)} combat time";
        }
        else { CurrentEncounterSummary = ActivitySummaryPresentation.Empty; CurrentEncounterText = "No encounters yet"; CurrentEncounterStateText = "An encounter appears when combat begins."; }
    }

    private ActivityQuery HistoryQuery() => new()
    {
        Scope = SelectedScope.Value,
        FromUtc = FromDate is { } from ? from.Date.ToUniversalTime() : null,
        ToUtc = ThroughDate is { } through ? through.Date.AddDays(1).ToUniversalTime() : null,
        Server = SelectedServer.Value,
        CharacterName = EmptyToNull(CharacterFilter),
        Location = EmptyToNull(LocationFilter),
        SessionKind = SelectedKind.Value,
        ContainsTags = HistoryTags
    };

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
        var page = append ? sessionPage + 1 : refresh ? sessionPage : 0;
        var query = HistoryQuery();
        var result = await ReadPagesAsync(index => persistence.GetSessionsAsync(query, index), page, append);
        if (isDisposed || version != sessionListVersion) return;
        var selectedId = SelectedSession?.Id;
        applyingSelection = true;
        try
        {
            ApplyHeaderRows(Sessions, result.Items, row => row.Id, source => source.Id,
                source => new ActivitySessionRow(source, SelectedScope.Value), (row, source) => row.Update(source, SelectedScope.Value), append);
            SelectedSession = selectedId is { } id ? Sessions.FirstOrDefault(row => row.Id == id) : null;
            sessionPage = page;
            HasMoreSessions = result.HasMore;
            OnPropertyChanged(nameof(HistoryCountText));
            NotifyDetailProperties();
        }
        finally { applyingSelection = false; }
    }

    private async Task RefreshDetailsAsync()
    {
        if (persistence is null || SelectedSession is null) return;
        var version = ++detailVersion;
        var sessionId = SelectedSession.Id;
        var encounterId = SelectedEncounter?.Id;
        var sessionTask = persistence.GetSessionAsync(sessionId);
        var encounterTask = encounterId is { } id ? persistence.GetEncounterAsync(id) : Task.FromResult<ActivityEncounter?>(null);
        await Task.WhenAll(sessionTask, encounterTask);
        if (!IsCurrentDetail(version, sessionId) || sessionTask.Result is not { } selected) return;
        var encounter = encounterTask.Result;
        applyingSelection = true;
        try
        {
            var sessionRow = Sessions.FirstOrDefault(row => row.Id == sessionId) ?? SelectedSession ?? new ActivitySessionRow(selected, SelectedScope.Value);
            sessionRow.Update(selected, SelectedScope.Value);
            if (!Sessions.Contains(sessionRow)) Sessions.Insert(0, sessionRow);
            SelectedSession = sessionRow;
            if (encounter is not null)
            {
                var encounterRow = Encounters.FirstOrDefault(row => row.Id == encounter.Id) ?? SelectedEncounter ?? new ActivityEncounterRow(encounter, SelectedScope.Value);
                encounterRow.Update(encounter, SelectedScope.Value);
                if (!Encounters.Contains(encounterRow)) Encounters.Add(encounterRow);
                SelectedEncounter = encounterRow;
            }
            else SelectedEncounter = null;
        }
        finally { applyingSelection = false; }
        var query = DetailQuery();
        var summaryTask = persistence.GetSummaryAsync(query);
        var combatTask = encounter is null ? persistence.GetCombatDurationSecondsAsync(query) : Task.FromResult(encounter.CombatDurationSeconds);
        var legacyTask = IsImportedDetail ? persistence.GetLegacyImportInfoAsync(sessionId) : Task.FromResult<ActivityLegacyImportInfo?>(null);
        await Task.WhenAll(summaryTask, combatTask, legacyTask);
        if (!IsCurrentDetail(version, selected.Id)) return;
        var summary = summaryTask.Result;
        LegacyImportInfo = legacyTask.Result;
        DetailSummary = new(summary, encounter is null ? ActivityProjection.GetRates(summary, selected, combatTask.Result, DateTime.UtcNow) : ActivityProjection.GetRates(summary, encounter), encounter is null);
        NotifyDetailProperties();
        if (SelectedDetailTabIndex == 0) await LoadEncountersAsync(false, true);
        else if (SelectedDetailTabIndex == 1) await LoadChartAsync(version, query);
        else if (SelectedDetailTabIndex == 2)
        {
            var totals = await persistence.GetPlayersAsync(query with { ActorKey = null });
            if (!IsCurrentDetail(version, selected.Id)) return;
            var actorId = SelectedPlayer?.ActorId;
            applyingSelection = true;
            try
            {
                Players = new(totals.Select(total => new ActivityPlayerRow(total)));
                SelectedPlayer = actorId is null ? null : Players.FirstOrDefault(player => player.ActorId == actorId);
            }
            finally { applyingSelection = false; }
        }
        else if (SelectedDetailTabIndex == 3)
        {
            var totals = await persistence.GetMobsAsync(query);
            if (IsCurrentDetail(version, selected.Id)) Mobs = new(totals.Select(total => new ActivityMobRow(total)));
        }
        else if (SelectedDetailTabIndex == 4) await LoadSessionLootAsync(false, version, true);
        else if (SelectedDetailTabIndex == 5) await LoadTimelineAsync(false, version, true);
    }

    private bool IsCurrentDetail(long version, Guid sessionId) => !isDisposed && detailVersion == version && SelectedSession?.Id == sessionId;

    private async Task LoadEncountersAsync(bool append, bool refresh = false)
    {
        if (persistence is null || SelectedSession is null) return;
        var sessionId = SelectedSession.Id;
        var page = append ? encounterPage + 1 : refresh ? encounterPage : 0;
        var result = await ReadPagesAsync(index => persistence.GetEncountersAsync(sessionId, index), page, append);
        if (isDisposed || SelectedSession?.Id != sessionId) return;
        var selectedEncounter = SelectedEncounter;
        var selectedId = selectedEncounter?.Id;
        applyingSelection = true;
        try
        {
            var headers = selectedEncounter is not null && !result.Items.Any(source => source.Id == selectedId)
                ? result.Items.Append(selectedEncounter.Source).ToArray() : result.Items;
            ApplyHeaderRows(Encounters, headers, row => row.Id, source => source.Id,
                source => new ActivityEncounterRow(source, SelectedScope.Value), (row, source) => row.Update(source, SelectedScope.Value), append);
            SelectedEncounter = selectedId is { } id ? Encounters.FirstOrDefault(row => row.Id == id) : null;
            encounterPage = page;
            HasMoreEncounters = result.HasMore;
        }
        finally { applyingSelection = false; }
    }

    private async Task LoadChartAsync(long version, ActivityQuery query)
    {
        if (persistence is null || query.SessionId is not { } sessionId) return;
        var points = await persistence.GetChartsAsync(query with { ShowCategories = VisibleCategories }, SelectedChartMetric.Value);
        if (!IsCurrentDetail(version, sessionId)) return;
        ChartSeries = [new LineSeries<DateTimePoint>
        {
            Name = SelectedChartMetric.Label,
            Values = points.Select(point => new DateTimePoint(point.CapturedAtUtc, point.Value)).ToArray(),
            Fill = null,
            Stroke = new SolidColorPaint(SKColors.DeepSkyBlue) { StrokeThickness = 2 },
            GeometryFill = null,
            GeometryStroke = null,
            GeometrySize = 0,
            LineSmoothness = 0,
            AnimationsSpeed = TimeSpan.Zero,
            XToolTipLabelFormatter = point => ActivityDisplay.LocalTime(point.Model?.DateTime ?? DateTime.MinValue),
            YToolTipLabelFormatter = point => point.Coordinate.PrimaryValue.ToString("N1")
        }];
        ChartXAxes = [CreateTimeAxis()];
        ChartYAxes = [CreateAmountAxis(SelectedChartMetric.Label)];
    }

    private static Axis CreateTimeAxis() => new()
    {
        Name = "Local time",
        TextSize = 11,
        LabelsPaint = new SolidColorPaint(SKColors.LightGray),
        NamePaint = new SolidColorPaint(SKColors.LightGray),
        SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 24)),
        Labeler = ticks => ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
            ? DateTime.SpecifyKind(new DateTime((long)ticks), DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss") : ""
    };

    private static Axis CreateAmountAxis(string name) => new()
    {
        Name = name,
        MinLimit = 0,
        Labeler = value => value.ToString("N0"),
        LabelsPaint = new SolidColorPaint(SKColors.LightGray),
        NamePaint = new SolidColorPaint(SKColors.LightGray),
        SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 24))
    };

    private async Task LoadSessionLootAsync(bool append, long? version = null, bool refresh = false)
    {
        if (persistence is null || SelectedSession is null) return;
        var query = DetailQuery(true);
        var sessionId = SelectedSession.Id;
        var page = append ? sessionLootPage + 1 : refresh ? sessionLootPage : 0;
        var result = await ReadPagesAsync(index => persistence.GetEventsAsync(query, index), page, append);
        if (isDisposed || SelectedSession?.Id != sessionId || version is { } expected && detailVersion != expected) return;
        ApplyEventRows(SessionLoot, result.Items.Select(observation => new ActivityLootRow(observation)).ToArray(), row => row.SourceEvent, append);
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
        var result = await ReadPagesAsync(index => persistence.GetEventsAsync(query, index), page, append);
        if (isDisposed || SelectedSession?.Id != sessionId || version is { } expected && detailVersion != expected) return;
        ApplyEventRows(Timeline, result.Items.Select(observation => new ActivityEventRow(observation)).ToArray(), row => row.SourceEvent, append);
        timelinePage = page;
        HasMoreTimeline = result.HasMore;
    }

    private async Task LoadGlobalLootAsync(bool append, bool refresh = false)
    {
        if (persistence is null) return;
        var version = ++globalLootVersion;
        var page = append ? globalLootPage + 1 : refresh ? globalLootPage : 0;
        var query = HistoryQuery() with { Scope = SelectedLootScope.Value, ItemsOnly = true, ItemSources = SelectedItemSource.Value, ContainsTags = ActivityTags.None, Search = EmptyToNull(LootSearch) };
        var result = await ReadPagesAsync(index => persistence.GetEventsAsync(query, index), page, append);
        var summary = append ? null : await persistence.GetSummaryAsync(query);
        var resources = append ? null : await persistence.GetResourceTotalsAsync(query);
        if (isDisposed || version != globalLootVersion) return;
        ApplyEventRows(GlobalLoot, result.Items.Select(observation => new ActivityLootRow(observation)).ToArray(), row => row.SourceEvent, append);
        if (resources is not null) GlobalResources = new(resources.Select(row => new ActivityResourceRow(row)));
        globalLootPage = page;
        HasMoreGlobalLoot = result.HasMore;
        if (summary is not null)
        {
            GlobalLootSummary = new(summary, ActivityProjection.GetRates(summary, 0));
            OnPropertyChanged(nameof(GlobalLootTotalsText));
        }
        OnPropertyChanged(nameof(LootCountText));
    }

    private static async Task<ActivityPage<T>> ReadPagesAsync<T>(Func<int, Task<ActivityPage<T>>> readPage, int lastPage, bool append)
    {
        if (append) return await readPage(lastPage);
        var pages = await Task.WhenAll(Enumerable.Range(0, lastPage + 1).Select(readPage));
        return new(pages.SelectMany(page => page.Items).ToArray(), pages[^1].HasMore);
    }

    private static void ApplyHeaderRows<TRow, TSource>(ObservableCollection<TRow> rows, IReadOnlyList<TSource> sources,
        Func<TRow, Guid> rowId, Func<TSource, Guid> sourceId, Func<TSource, TRow> create, Action<TRow, TSource> update, bool append)
    {
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var id = sourceId(source);
            var existingIndex = rows.ToList().FindIndex(row => rowId(row) == id);
            if (existingIndex >= 0)
            {
                update(rows[existingIndex], source);
                if (!append && existingIndex != index) rows.Move(existingIndex, index);
            }
            else if (append) rows.Add(create(source));
            else rows.Insert(index, create(source));
        }
        if (!append) while (rows.Count > sources.Count) rows.RemoveAt(rows.Count - 1);
    }

    private static void ApplyEventRows<T>(ObservableCollection<T> rows, IReadOnlyList<T> replacements, Func<T, ActivityEvent> eventOf, bool append)
    {
        if (append)
        {
            foreach (var replacement in replacements)
            {
                var id = eventOf(replacement).Id;
                var existing = rows.FirstOrDefault(row => eventOf(row).Id == id);
                if (existing is not null) rows[rows.IndexOf(existing)] = replacement;
                else rows.Add(replacement);
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
            if (persistence?.Status.Error is not { Length: > 0 }) ErrorText = "";
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to load activity history");
            if (!isDisposed) ErrorText = "Could not load activity history. Saved events are retained; try refreshing.";
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
        OnPropertyChanged(nameof(HasSelectedPlayer));
        OnPropertyChanged(nameof(CanDeleteSelectedSession));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailDurationText));
        OnPropertyChanged(nameof(DetailScopeText));
        OnPropertyChanged(nameof(DetailEarningsText));
        OnPropertyChanged(nameof(ChartDescription));
        OnPropertyChanged(nameof(DetailTagsText));
        OnPropertyChanged(nameof(IsImportedDetail));
        OnPropertyChanged(nameof(HasDetailedHistory));
        OnPropertyChanged(nameof(ImportedDetailText));
    }

    partial void OnSelectedScopeChanged(ActivityOption<ActivityScope> value)
    {
        refreshPending = true;
        ResetEventPages();
        if (SelectedSessionLootScope.Value != ActivityScope.AllObserved) SelectedSessionLootScope = value;
        if (SelectedLootScope.Value != ActivityScope.AllObserved) SelectedLootScope = value;
        NotifyDetailProperties();
        if (isActive) _ = RunSafelyAsync(RefreshVisibleAsync);
    }
    partial void OnSelectedLootScopeChanged(ActivityOption<ActivityScope> value) { globalLootPage = 0; if (isActive && SelectedTabIndex == 2) _ = RunSafelyAsync(() => LoadGlobalLootAsync(false)); }
    partial void OnSelectedSessionLootScopeChanged(ActivityOption<ActivityScope> value)
    {
        sessionLootPage = 0;
        SessionLoot.Clear();
        if (isActive && SelectedDetailTabIndex == 4) _ = RunSafelyAsync(RefreshDetailsAsync);
    }
    partial void OnSelectedTabIndexChanged(int value) { if (isActive) { refreshPending = true; _ = RunSafelyAsync(RefreshVisibleAsync); } }
    partial void OnSelectedDetailTabIndexChanged(int value) { if (isActive && !applyingSelection) _ = RunSafelyAsync(RefreshDetailsAsync); }
    partial void OnSelectedChartMetricChanged(ActivityOption<ActivityChartMetric> value) { if (isActive) _ = RunSafelyAsync(RefreshDetailsAsync); }
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsRecordingEnabledChanged(bool value) => OnPropertyChanged(nameof(RecordingButtonText));
    partial void OnCurrentSummaryChanged(ActivitySummaryPresentation value) { OnPropertyChanged(nameof(CurrentCombatDurationText)); OnPropertyChanged(nameof(CurrentTagsText)); }
    partial void OnDetailSummaryChanged(ActivitySummaryPresentation value) => NotifyDetailProperties();
    partial void OnGlobalLootSummaryChanged(ActivitySummaryPresentation value) => OnPropertyChanged(nameof(GlobalLootTotalsText));
    partial void OnSelectedSessionChanged(ActivitySessionRow? value)
    {
        NotifyDetailProperties();
        if (applyingSelection) return;
        applyingSelection = true;
        try { SelectedEncounter = null; SelectedPlayer = null; }
        finally { applyingSelection = false; }
        Encounters.Clear(); Players.Clear(); Mobs.Clear(); SessionLoot.Clear(); Timeline.Clear(); ChartSeries = [];
        encounterPage = timelinePage = sessionLootPage = 0;
        HasMoreEncounters = HasMoreSessionLoot = HasMoreTimeline = false;
        DetailSummary = ActivitySummaryPresentation.Empty;
        SessionResources = new();
        LegacyImportInfo = null;
        if (IsImportedDetail) SelectedDetailTabIndex = 4;
        ++detailVersion;
        if (value is not null) _ = RunSafelyAsync(RefreshDetailsAsync);
    }
    partial void OnSelectedEncounterChanged(ActivityEncounterRow? value) { NotifyDetailProperties(); if (!applyingSelection) { ResetEventPages(); _ = RunSafelyAsync(RefreshDetailsAsync); } }
    partial void OnSelectedPlayerChanged(ActivityPlayerRow? value) { NotifyDetailProperties(); if (!applyingSelection) { ResetEventPages(); _ = RunSafelyAsync(RefreshDetailsAsync); } }

    private void ResetEventPages()
    {
        timelinePage = sessionLootPage = 0;
        SessionLoot.Clear(); Timeline.Clear(); ChartSeries = [];
        HasMoreSessionLoot = HasMoreTimeline = false;
    }

    [RelayCommand] private void ToggleRecording() { tracker?.SetRecordingEnabled(!IsRecordingEnabled); UpdateStatus(); }
    public void FinishCurrentSession(Guid? expectedSessionId = null) { tracker?.FinishSession(expectedSessionId); refreshPending = true; UpdateStatus(); }
    [RelayCommand] private void FinishSession() => FinishCurrentSession();
    [RelayCommand]
    private async Task OpenCurrentSessionAsync()
    {
        if (tracker?.CurrentSessionId is { } id) await RunSafelyAsync(() => OpenSessionDetailsAsync(id));
    }

    [RelayCommand]
    private async Task OpenCurrentEncounterAsync()
    {
        if (currentEncounter is { } encounter) await RunSafelyAsync(() => OpenSessionDetailsAsync(encounter.SessionId, encounter.Id));
    }

    private async Task OpenSessionDetailsAsync(Guid id, Guid? encounterId = null)
    {
        if (persistence is null) return;
        var session = await persistence.GetSessionAsync(id);
        if (session is null) return;
        FromDate = ThroughDate = null;
        SelectedServer = ServerOptions[0];
        CharacterFilter = LocationFilter = "";
        SelectedKind = KindOptions[0];
        SelectedTabIndex = 1;
        HistoryGathering = HistoryPve = HistoryPvp = false;
        await LoadSessionsAsync(false);
        var row = Sessions.FirstOrDefault(existing => existing.Id == id);
        if (row is null) { row = new(session, SelectedScope.Value); Sessions.Insert(0, row); }
        SelectedSession = row;
        if (encounterId is { } selectedEncounterId && await persistence.GetEncounterAsync(selectedEncounterId) is { } encounter)
        {
            var encounterRow = Encounters.FirstOrDefault(existing => existing.Id == selectedEncounterId) ?? new(encounter, SelectedScope.Value);
            if (!Encounters.Contains(encounterRow)) Encounters.Add(encounterRow);
            SelectedEncounter = encounterRow;
        }
        else SelectedEncounter = null;
        await RefreshDetailsAsync();
    }
    [RelayCommand] private async Task ApplyFiltersAsync() => await RunSafelyAsync(async () => { await LoadSessionsAsync(false); if (SelectedTabIndex == 2) await LoadGlobalLootAsync(false); });
    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        FromDate = ThroughDate = null; SelectedServer = ServerOptions[0]; CharacterFilter = LocationFilter = ""; SelectedKind = KindOptions[0];
        HistoryGathering = HistoryPve = HistoryPvp = false;
        await ApplyFiltersAsync();
    }
    [RelayCommand] private async Task ApplyLootFiltersAsync() => await RunSafelyAsync(() => LoadGlobalLootAsync(false));
    [RelayCommand] private async Task LoadMoreSessionsAsync() => await RunSafelyAsync(() => LoadSessionsAsync(true));
    [RelayCommand] private async Task LoadMoreEncountersAsync() => await RunSafelyAsync(() => LoadEncountersAsync(true));
    [RelayCommand] private async Task LoadMoreTimelineAsync() => await RunSafelyAsync(() => LoadTimelineAsync(true));
    [RelayCommand] private async Task LoadMoreSessionLootAsync() => await RunSafelyAsync(() => LoadSessionLootAsync(true));
    [RelayCommand] private async Task LoadMoreGlobalLootAsync() => await RunSafelyAsync(() => LoadGlobalLootAsync(true));
    [RelayCommand] private void ClearEncounter() => SelectedEncounter = null;
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
            refreshPending = true;
        });
    }

    public async Task DeleteUnassignedLootAsync()
    {
        if (persistence is null) return;
        await RunSafelyAsync(async () => { await persistence.DeleteUnassignedLootAsync(); await LoadGlobalLootAsync(false); });
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string KindLabel(ActivitySessionKind kind) => kind switch
    {
        ActivitySessionKind.MistsDungeon => "Mists dungeon",
        ActivitySessionKind.AbyssalDepths => "Abyssal Depths",
        ActivitySessionKind.DragonArea => "Dragon area",
        ActivitySessionKind.StaticDungeon => "Static dungeon",
        _ => kind.ToString()
    };

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        ++detailVersion;
        liveTimelineCancellation.Cancel();
        liveTimelineCancellation.Dispose();
        refreshTimer.Stop();
        refreshTimer.Tick -= OnRefreshTick;
        if (tracker is not null) tracker.Changed -= OnActivityChanged;
        if (persistence is not null) persistence.StatusChanged -= OnPersistenceStatusChanged;
    }
}

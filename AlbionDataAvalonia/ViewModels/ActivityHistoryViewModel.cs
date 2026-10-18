using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AlbionDataAvalonia.ViewModels.ActivityReads;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>
/// What the History details header shows for the selected session, run, encounter, map and player. It is
/// rebuilt as one value whenever the selection or the totals change, so the view gets one notification, and
/// none when nothing it shows changed.
/// </summary>
public sealed record ActivityDetailHeader
{
    public static readonly ActivityDetailHeader Empty = new();

    public string Title { get; init; } = "Select a session";
    public string MapMetadataText { get; init; } = "";
    public string DurationText { get; init; } = "";
    public string ScopeText { get; init; } = "";
    public string TagsText { get; init; } = "";
    public bool IsImported { get; init; }
    public string ImportedTooltipText { get; init; } = "";
    public bool HasRun { get; init; }
    public bool HasMultipleRuns { get; init; }
    public bool HasEncounterOrMap { get; init; }
    public bool HasPlayer { get; init; }
    public string ClearSelectionText { get; init; } = "All encounters";
    public bool CanShowOnMap { get; init; }
    public bool CanDelete { get; init; }
    public string DeleteButtonText { get; init; } = "Delete session…";
    public bool ShowLootPanel { get; init; } = true;
    public bool CanExportLoot { get; init; }

    public bool HasMapMetadata => MapMetadataText.Length > 0;
    public bool HasDetailedHistory => !IsImported;
    public string ImportedText => IsImported ? "Imported gathering totals" : "";
}

/// <summary>
/// The Activity page's History tab. It lists play sessions; choosing one shows its totals and the detail
/// tabs, and a run chip (<see cref="SelectedSession"/>) narrows them to that run.
/// </summary>
public partial class ActivityHistoryViewModel : ObservableObject, IDisposable
{
    private readonly ActivityViewModel page;
    private bool applyingSelection;
    private int encounterPage;
    private int timelinePage;
    private int sessionLootPage;
    private long detailVersion;
    private long detailChangeVersion = 1;
    private long detailReadVersion;
    private long detailDataVersion;
    private ActivityQuery? cachedDetailQuery;
    private long cachedDetailDataVersion = -1;
    private ActivityEncounter? cachedDetailEncounter;
    private ActivitySummary? cachedDetailSummary;
    private double cachedDetailCombatSeconds;
    private ActivityLegacyImportInfo? cachedDetailLegacyInfo;
    private CancellationTokenSource detailQueryCancellation = new();

    // The tab control binds the index; code reads the tab by name.
    [ObservableProperty] private int selectedDetailTabIndex;
    [ObservableProperty] private ActivityDetailHeader detail = ActivityDetailHeader.Empty;
    [ObservableProperty] private ActivitySummaryPresentation detailSummary = ActivitySummaryPresentation.Empty;
    [ObservableProperty] private Option<ActivityScope> selectedSessionLootScope = new(ActivityScope.Personal, "Personal");
    [ObservableProperty] private Option<ActivityItemSources> selectedSessionItemSource = new(ActivityItemSources.All, "All items");
    [ObservableProperty] private ObservableCollection<ActivitySessionRow> sessions = new();
    [ObservableProperty] private ObservableCollection<ActivityEncounterRow> encounters = new();
    [ObservableProperty] private ObservableCollection<ActivityPlayerRow> players = new();
    [ObservableProperty] private ActivityPlayerColumns playerColumns = ActivityPlayerColumns.None;
    [ObservableProperty] private ObservableCollection<ActivityMobRow> mobs = new();
    [ObservableProperty] private ObservableCollection<ActivityLootRow> sessionLoot = new();
    [ObservableProperty] private ObservableCollection<ActivityEventRow> timeline = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessionResources))]
    private ObservableCollection<ActivityResourceRow> sessionResources = new();
    [ObservableProperty] private ActivitySessionRow? selectedSession;
    [ObservableProperty] private ActivityEncounterRow? selectedEncounter;
    [ObservableProperty] private ActivityPlayerRow? selectedPlayer;
    [ObservableProperty] private bool hasMoreEncounters;
    [ObservableProperty] private bool hasMoreTimeline;
    [ObservableProperty] private bool hasMoreSessionLoot;
    [ObservableProperty] private ActivityLegacyImportInfo? legacyImportInfo;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPartyHistory), nameof(HasPartyChanges))]
    private ActivityPartyHistory partyHistory = ActivityPartyHistory.Empty;

    internal ActivityHistoryViewModel(ActivityViewModel page)
    {
        this.page = page;
        page.Export.PropertyChanged += OnExportChanged;
    }

    public ActivityFilterState Filters => page.Filters;
    public ActivityLootExport Export => page.Export;

    public ActivityDetailTab SelectedDetailTab
    {
        get => (ActivityDetailTab)SelectedDetailTabIndex;
        set => SelectedDetailTabIndex = (int)value;
    }

    public bool HasSessionResources => SessionResources.Count > 0;
    public bool HasPartyHistory => PartyHistory.Members.Count > 0;
    public bool HasPartyChanges => PartyHistory.Changes.Count > 0;

    // Imported gathering summaries form their own single-run sessions.
    private ActivitySession? ImportedRun => (SelectedSession?.Source ?? (SelectedPlaySession?.Runs is { Count: 1 } runs ? runs[0] : null)) is { } run &&
        ActivityProjection.GetSummary(run, Filters.Scope).IsLegacyAggregate ? run : null;
    private bool IsImportedDetail => ImportedRun is not null;
    private bool IsShowingMap => SelectedDetailTab == ActivityDetailTab.Maps && SelectedMap is not null;
    private bool ShowsSessionLoot => (SelectedSessionItemSource.Value & ActivityItemSources.Loot) != 0;
    private bool CanExportSessionLoot => Export.CanStart && SelectedPlaySession is not null && !IsImportedDetail && ShowsSessionLoot;
    private bool CanDeleteSelection => SelectedSession is { } run
        ? run.Source.State != ActivityState.Open && run.Id != page.Tracker?.CurrentSessionId
        : SelectedPlaySession is { IsOpen: false } playSession && playSession.Id != page.Tracker?.CurrentPlaySessionId;

    // Called after any change to the selection or the totals; an unchanged header raises nothing.
    internal void UpdateDetail() => Detail = BuildDetail();

    private ActivityDetailHeader BuildDetail()
    {
        var map = IsShowingMap ? SelectedMap : null;
        var imported = IsImportedDetail;
        var rates = DetailSummary.Rates;
        var isPartial = (SelectedSession?.Source.IsPartial ?? SelectedPlaySession?.IsPartial) == true;
        return new()
        {
            Title = map is not null ? map.Name : SelectedEncounter is { } encounter ? $"Encounter {encounter.Number} · {encounter.Classification}"
                : SelectedSession?.Title ?? SelectedPlaySession?.Title ?? "Select a session",
            MapMetadataText = map?.DetailsText ?? "",
            DurationText = map is not null ? $"Recorded time: {map.DurationText} · Combat time: {UiFormat.Duration(rates.CombatDurationSeconds)}"
                : imported ? $"Duration: {UiFormat.Duration(rates.DurationSeconds)}" : SelectedEncounter is { } selected ? $"Combat time: {selected.DurationText}"
                : SelectedSession is { } run ? $"Run time: {run.DurationText} · Combat time: {UiFormat.Duration(rates.CombatDurationSeconds)}"
                : $"Session time: {UiFormat.Duration(rates.DurationSeconds)}" +
                    (DetailSummary.RunSeconds is { } runSeconds ? $" · In runs: {UiFormat.Duration(runSeconds)}" : "") +
                    $" · Combat time: {UiFormat.Duration(rates.CombatDurationSeconds)}",
            ScopeText = $"{SelectedPlayer?.Name ?? ""}{(isPartial ? $"{(SelectedPlayer is null ? "" : " · ")}Incomplete recording" : "")}",
            TagsText = map is not null ? ActivityDisplay.Tags(DetailSummary.Summary.Tags)
                : SelectedSession is { } row ? ActivityDisplay.Tags(ActivityProjection.GetSummary(row.Source, Filters.Scope).Tags)
                : SelectedPlaySession?.TagsText ?? "",
            IsImported = imported,
            ImportedTooltipText = imported
                ? "Imported totals only: no pickup times or combat." + (LegacyImportInfo?.HasMismatch == true ? $" Saved totals: {LegacyImportInfo.OriginalTotalAmount:N0} items, EMV {LegacyImportInfo.OriginalTotalValue:N0}; item rows differ." : "") : "",
            HasRun = SelectedSession is not null,
            HasMultipleRuns = Sessions.Count > 1,
            HasEncounterOrMap = SelectedEncounter is not null || map is not null,
            HasPlayer = SelectedPlayer is not null,
            ClearSelectionText = map is not null ? "All maps" : "All encounters",
            CanShowOnMap = page.MapWindow is not null && MapRunId is not null,
            CanDelete = CanDeleteSelection,
            DeleteButtonText = SelectedSession is not null ? "Delete run…" : "Delete session…",
            ShowLootPanel = ShowsSessionLoot && !imported,
            CanExportLoot = CanExportSessionLoot
        };
    }

    private void OnExportChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ActivityLootExport.CanStart)) UpdateDetail();
    }

    internal async Task RefreshAsync()
    {
        if (historyReadVersion != historyChangeVersion) await LoadPlaySessionsAsync(false, true);
        if (SelectedPlaySession is not null && detailReadVersion != detailChangeVersion) await RefreshDetailsAsync();
    }

    // Open sessions, runs and maps keep counting. Returns whether saved data changed since the last read.
    internal bool Tick()
    {
        foreach (var row in PlaySessions.Where(x => x.IsOpen)) row.RefreshDuration();
        foreach (var row in Sessions.Where(x => x.Source.State == ActivityState.Open)) row.RefreshDuration();
        foreach (var row in Maps.Where(x => x.Source.ObservationStartedAtUtc is not null)) row.RefreshDuration();
        if (IsShowingMap)
            DetailSummary = new(DetailSummary.Summary, ActivityProjection.GetRates(DetailSummary.Summary,
                SelectedMap!.DurationSeconds, DetailSummary.Rates.CombatDurationSeconds), true, SelectedMap.Source.Visits > 0);
        else if (SelectedEncounter is null && cachedDetailHeader is { } header && header.PlaySession.Id == SelectedPlaySession?.Id &&
            (SelectedSession?.Source.State ?? header.PlaySession.State) == ActivityState.Open)
            DetailSummary = DetailPresentation(DetailSummary.Summary, header, SelectedSession?.Source, null, DetailSummary.Rates.CombatDurationSeconds);
        return historyReadVersion != historyChangeVersion || SelectedPlaySession is not null && detailReadVersion != detailChangeVersion;
    }

    internal void OnDataChanged(ActivityDataChange change, HashSet<Guid> changedIds)
    {
        if (change.HistoryChanged) ++historyChangeVersion;
        if (SelectedPlaySession is { } selected && (change.PlaySessionIds?.Contains(selected.Id) == true ||
            selected.Runs.Any(run => changedIds.Contains(run.Id))))
        { ++detailChangeVersion; ++detailDataVersion; }
    }

    internal void OnScopeChanged(Option<ActivityScope> scope)
    {
        ++historyChangeVersion; ++detailChangeVersion;
        ++sessionListVersion; ++detailVersion;
        ResetEventPages();
        if (SelectedSessionLootScope.Value != ActivityScope.AllObserved) SelectedSessionLootScope = scope;
        UpdateDetail();
    }

    internal void OnCategoriesChanged()
    {
        ++detailChangeVersion; ++detailVersion;
        if (page.IsShowing(ActivityTab.History) && SelectedDetailTab is ActivityDetailTab.Charts or ActivityDetailTab.Timeline)
            _ = page.RunSafelyAsync(RefreshDetailsAsync);
    }

    // Reads that page through results without starting their own read reuse these tokens.
    internal void ResumeReads()
    {
        if (historyQueryCancellation.IsCancellationRequested) BeginRead(ref historyQueryCancellation);
        if (detailQueryCancellation.IsCancellationRequested) BeginRead(ref detailQueryCancellation);
    }

    internal void CancelReads()
    {
        CancelHistoryReplayRead();
        historyQueryCancellation.Cancel();
        detailQueryCancellation.Cancel();
    }

    private ActivityQuery DetailQuery(bool lootOnly = false) => new()
    {
        PlaySessionId = SelectedPlaySession?.Id,
        SessionId = SelectedSession?.Id,
        EncounterId = SelectedEncounter?.Id,
        ActorKey = SelectedPlayer?.ActorId,
        Scope = lootOnly ? SelectedSessionLootScope.Value : Filters.Scope,
        ItemsOnly = lootOnly,
        ItemSources = lootOnly ? SelectedSessionItemSource.Value : ActivityItemSources.All
    };

    private async Task RefreshDetailsAsync()
    {
        if (page.Persistence is not { } persistence || SelectedPlaySession is null) return;
        var version = ++detailVersion;
        var dataVersion = detailChangeVersion;
        var cancellationToken = BeginRead(ref detailQueryCancellation);
        var playSessionId = SelectedPlaySession.Id;
        var runId = SelectedSession?.Id;
        var encounterId = SelectedEncounter?.Id;
        var scope = Filters.Scope;
        var query = DetailQuery();
        var detailTab = SelectedDetailTab;
        var cacheVersion = detailDataVersion;
        var useCache = cachedDetailQuery == query && cachedDetailDataVersion == cacheVersion && cachedDetailSummary is not null && cachedDetailHeader is not null;
        var headerTask = useCache ? Task.FromResult(cachedDetailHeader) : persistence.GetPlaySessionAsync(playSessionId, cancellationToken);
        var encounterTask = useCache ? Task.FromResult(cachedDetailEncounter) : encounterId is { } id
            ? persistence.GetEncounterAsync(id, cancellationToken) : Task.FromResult<ActivityEncounter?>(null);
        await Task.WhenAll(headerTask, encounterTask);
        if (!IsCurrentDetail(version, playSessionId) || DetailQuery() != query || SelectedDetailTab != detailTab) return;
        if (headerTask.Result is not { } header)
        {
            // The session was deleted, or its runs were merged into another one.
            SelectedPlaySession = null;
            await LoadPlaySessionsAsync(false);
            return;
        }
        var encounter = encounterTask.Result;
        applyingSelection = true;
        try
        {
            SelectedPlaySession.Update(header, scope);
            ApplyRunRows(header);
            SelectedSession = runId is { } selectedRun ? Sessions.FirstOrDefault(row => row.Id == selectedRun) : null;
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
        // A run that was merged or deleted leaves the whole session on view.
        if (runId is not null && SelectedSession is null) { ++detailChangeVersion; UpdateDetail(); return; }
        var run = SelectedSession?.Source;
        cachedDetailHeader = header;
        if (detailTab == ActivityDetailTab.Maps)
        {
            await RefreshMapsAsync(version, header, run, cancellationToken);
            if (IsCurrentDetail(version, playSessionId) && SelectedDetailTab == detailTab)
                detailReadVersion = dataVersion;
            return;
        }
        // Saved scope totals describe all events of the session or run. Actor, time, item,
        // and category filters must still use the event projection.
        var wholeHeader = query == new ActivityQuery { PlaySessionId = playSessionId, SessionId = runId, EncounterId = encounterId, Scope = scope };
        var summaryTask = useCache ? Task.FromResult(cachedDetailSummary!) : wholeHeader
            ? Task.FromResult(encounter is not null ? ActivityProjection.GetSummary(encounter, scope)
                : run is not null ? ActivityProjection.GetSummary(run, scope) : ActivityPlaySessionProjection.Summary(header.Runs, scope))
            : persistence.GetSummaryAsync(query, cancellationToken);
        var combatTask = useCache ? Task.FromResult(cachedDetailCombatSeconds) : encounter is null
            ? persistence.GetCombatDurationSecondsAsync(query, cancellationToken) : Task.FromResult(encounter.CombatDurationSeconds);
        var importedRun = ImportedRun;
        var legacyTask = useCache ? Task.FromResult(cachedDetailLegacyInfo) : importedRun is not null
            ? persistence.GetLegacyImportInfoAsync(importedRun.Id, cancellationToken) : Task.FromResult<ActivityLegacyImportInfo?>(null);
        await Task.WhenAll(summaryTask, combatTask, legacyTask);
        if (!IsCurrentDetail(version, playSessionId) || DetailQuery() != query || SelectedDetailTab != detailTab) return;
        var summary = summaryTask.Result;
        cachedDetailQuery = query;
        cachedDetailDataVersion = cacheVersion;
        cachedDetailEncounter = encounter;
        cachedDetailSummary = summary;
        cachedDetailCombatSeconds = combatTask.Result;
        cachedDetailLegacyInfo = legacyTask.Result;
        LegacyImportInfo = legacyTask.Result;
        DetailSummary = DetailPresentation(summary, header, run, encounter, combatTask.Result);
        UpdateDetail();
        if (detailTab == ActivityDetailTab.Encounters) await LoadEncountersAsync(false, true);
        else if (detailTab == ActivityDetailTab.Charts) await LoadChartAsync(version, query);
        else if (detailTab == ActivityDetailTab.Players)
        {
            var totals = await persistence.GetPlayersAsync(query with { ActorKey = null }, cancellationToken);
            if (!IsCurrentDetail(version, playSessionId) || DetailQuery() != query || SelectedDetailTab != detailTab) return;
            var actorId = SelectedPlayer?.ActorId;
            applyingSelection = true;
            try
            {
                PlayerColumns = ActivityPlayerColumns.From(totals);
                Players = new(totals.Select(total => new ActivityPlayerRow(total)));
                SelectedPlayer = actorId is null ? null : Players.FirstOrDefault(player => player.ActorId == actorId);
            }
            finally { applyingSelection = false; }
        }
        else if (detailTab == ActivityDetailTab.KilledMobs)
        {
            var totals = await persistence.GetMobsAsync(query, cancellationToken);
            if (IsCurrentDetail(version, playSessionId) && DetailQuery() == query && SelectedDetailTab == detailTab) Mobs = new(totals.Select(total => new ActivityMobRow(total)));
        }
        else if (detailTab == ActivityDetailTab.Items) await LoadSessionLootAsync(false, version, true);
        else if (detailTab == ActivityDetailTab.Timeline) await LoadTimelineAsync(false, version, true);
        else if (detailTab == ActivityDetailTab.Party) await LoadPartyHistoryAsync(version, header, run, cancellationToken);
        if (IsCurrentDetail(version, playSessionId) && DetailQuery() == query && SelectedDetailTab == detailTab)
            detailReadVersion = dataVersion;
    }

    // A whole play session counts the time between its runs up to the idle limit; one run counts only itself.
    private ActivitySummaryPresentation DetailPresentation(ActivitySummary summary, ActivityPlaySessionHeader header,
        ActivitySession? run, ActivityEncounter? encounter, double combatSeconds)
    {
        var nowUtc = DateTime.UtcNow;
        if (encounter is not null) return new(summary, ActivityProjection.GetRates(summary, encounter));
        // A session started with Start session also counts the wait before its only run.
        run ??= header.Runs.Count == 1 && header.Runs[0].StartedAtUtc <= header.PlaySession.StartedAtUtc ? header.Runs[0] : null;
        var live = page.Tracker?.GetCurrentContext().Session;
        if (run is not null)
            return new(summary, ActivityProjection.GetRates(summary, live?.Id == run.Id ? live : run, combatSeconds, nowUtc), true);
        var durations = ActivityPlaySessionProjection.Durations(header.PlaySession, header.Runs,
            live?.PlaySessionId == header.PlaySession.Id ? live : null, nowUtc);
        return new(summary, ActivityProjection.GetRates(summary, durations.PlaySeconds, combatSeconds), true, true, durations.RunSeconds);
    }

    // A session started with Start session begins at the press, before its first run.
    private static DateTime PlaySessionStart(ActivityPlaySessionHeader header) =>
        header.Runs.Count == 0 || header.PlaySession.StartedAtUtc < header.Runs.Min(x => x.StartedAtUtc)
            ? header.PlaySession.StartedAtUtc : header.Runs.Min(x => x.StartedAtUtc);

    private static DateTime PlaySessionEnd(ActivityPlaySessionHeader header) => header.PlaySession.EndedAtUtc ??
        (header.Runs.Count == 0 ? header.PlaySession.LastActivityAtUtc : header.Runs.Max(x => x.ClosedAtUtc ?? x.LastObservedAtUtc));

    private bool IsCurrentDetail(long version, Guid playSessionId) => !page.IsDisposed && detailVersion == version && SelectedPlaySession?.Id == playSessionId;

    private async Task LoadEncountersAsync(bool append, bool refresh = false)
    {
        if (page.Persistence is not { } persistence || SelectedPlaySession is null) return;
        var playSessionId = SelectedPlaySession.Id;
        var runId = SelectedSession?.Id;
        var version = detailVersion;
        var scope = Filters.Scope;
        var cancellationToken = detailQueryCancellation.Token;
        var pageIndex = append ? encounterPage + 1 : refresh ? encounterPage : 0;
        var result = await ReadPagesAsync(index => runId is { } run ? persistence.GetEncountersAsync(run, index, cancellationToken)
            : persistence.GetPlaySessionEncountersAsync(playSessionId, index, cancellationToken), pageIndex, append);
        if (!IsCurrentDetail(version, playSessionId) || SelectedSession?.Id != runId || Filters.Scope != scope) return;
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
            encounterPage = pageIndex;
            HasMoreEncounters = result.HasMore;
        }
        finally { applyingSelection = false; }
    }

    private async Task LoadSessionLootAsync(bool append, long? version = null, bool refresh = false)
    {
        if (page.Persistence is not { } persistence || SelectedPlaySession is null) return;
        var query = DetailQuery(true);
        var cancellationToken = detailQueryCancellation.Token;
        var playSessionId = SelectedPlaySession.Id;
        var pageIndex = append ? sessionLootPage + 1 : refresh ? sessionLootPage : 0;
        var result = await ReadEventRowsAsync(persistence, query with { LootOnly = true }, pageIndex, append, cancellationToken);
        if (page.IsDisposed || SelectedPlaySession?.Id != playSessionId || DetailQuery(true) != query || version is { } expected && detailVersion != expected) return;
        ApplyEventRows(SessionLoot, result.Items.Select(observation => new ActivityLootRow(observation)).ToArray(), row => row.SourceEvent, append);
        page.Valuations?.RequestMissing(result.Items);
        sessionLootPage = pageIndex;
        HasMoreSessionLoot = result.HasMore;
        await RefreshSessionResourcesAsync(query, version ?? detailVersion);
    }

    private async Task RefreshSessionResourcesAsync(ActivityQuery query, long version)
    {
        if (page.Persistence is not { } persistence || SelectedPlaySession is not { } playSession) return;
        var rows = await persistence.GetResourceTotalsAsync(query, detailQueryCancellation.Token);
        if (IsCurrentDetail(version, playSession.Id) && DetailQuery(true) == query) SessionResources = new(rows.Select(row => new ActivityResourceRow(row)));
    }

    private async Task LoadTimelineAsync(bool append, long? version = null, bool refresh = false)
    {
        if (page.Persistence is not { } persistence || SelectedPlaySession is null) return;
        var playSessionId = SelectedPlaySession.Id;
        var pageIndex = append ? timelinePage + 1 : refresh ? timelinePage : 0;
        var query = DetailQuery() with { ShowCategories = Filters.VisibleCategories };
        var cancellationToken = detailQueryCancellation.Token;
        var result = await ReadEventRowsAsync(persistence, query, pageIndex, append, cancellationToken);
        if (page.IsDisposed || SelectedPlaySession?.Id != playSessionId || (DetailQuery() with { ShowCategories = Filters.VisibleCategories }) != query || version is { } expected && detailVersion != expected) return;
        ApplyEventRows(Timeline, result.Items.Where(observation => observation.Kind is not (ActivityEventKind.OutcomeEvidence or ActivityEventKind.LegacyImportMarker))
            .Select(observation => new ActivityEventRow(observation)).ToArray(), row => row.SourceEvent, append);
        timelinePage = pageIndex;
        HasMoreTimeline = result.HasMore;
    }

    // Party members are derived from the recorded events, so they stay accurate for history
    // without storing a roster. A whole play session spans all of its runs.
    private async Task LoadPartyHistoryAsync(long version, ActivityPlaySessionHeader header, ActivitySession? run,
        CancellationToken cancellationToken)
    {
        if (page.Persistence is not { } persistence) return;
        var playSessionId = header.PlaySession.Id;
        var runs = run is null ? header.Runs : [run];
        var events = await persistence.GetPartyEventsAsync(new ActivityQuery { PlaySessionId = playSessionId, SessionId = run?.Id }, cancellationToken);
        var members = await persistence.GetSessionPartyMembersAsync(runs.Select(x => x.Id), cancellationToken);
        if (!IsCurrentDetail(version, playSessionId) || SelectedDetailTab != ActivityDetailTab.Party) return;
        var names = runs.SelectMany(x => members.TryGetValue(x.Id, out var found) ? found : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var span = run ?? new ActivitySession
        {
            StartedAtUtc = PlaySessionStart(header),
            ClosedAtUtc = header.PlaySession.State == ActivityState.Open ? null : PlaySessionEnd(header),
            LastObservedAtUtc = PlaySessionEnd(header),
            State = header.PlaySession.State
        };
        PartyHistory = ActivityPartyHistory.Build(events, span, names, DateTime.UtcNow);
    }

    partial void OnSelectedSessionLootScopeChanged(Option<ActivityScope> value)
    {
        ++detailChangeVersion; ++detailVersion;
        sessionLootPage = 0;
        SessionLoot.Clear();
        if (page.IsShowing(ActivityTab.History) && SelectedDetailTab == ActivityDetailTab.Items) _ = page.RunSafelyAsync(RefreshDetailsAsync);
    }

    partial void OnSelectedSessionItemSourceChanged(Option<ActivityItemSources> value)
    {
        UpdateDetail();
        ++detailChangeVersion; ++detailVersion;
        sessionLootPage = 0;
        if (page.IsShowing(ActivityTab.History) && SelectedDetailTab == ActivityDetailTab.Items) _ = page.RunSafelyAsync(RefreshDetailsAsync);
    }

    partial void OnSelectedDetailTabIndexChanged(int oldValue, int newValue)
    {
        var (previous, tab) = ((ActivityDetailTab)oldValue, (ActivityDetailTab)newValue);
        OnPropertyChanged(nameof(SelectedDetailTab));
        if (tab != ActivityDetailTab.Charts) CancelHistoryReplayRead();
        if (previous == ActivityDetailTab.Maps || tab == ActivityDetailTab.Maps)
            DetailSummary = ActivitySummaryPresentation.Empty;
        if (tab == ActivityDetailTab.Maps && !applyingSelection)
        {
            applyingSelection = true;
            try { SelectedEncounter = null; SelectedPlayer = null; }
            finally { applyingSelection = false; }
        }
        UpdateDetail();
        ++detailChangeVersion; ++detailVersion;
        if (page.IsActive && !applyingSelection) _ = page.RunSafelyAsync(RefreshDetailsAsync);
    }

    partial void OnDetailSummaryChanged(ActivitySummaryPresentation value) => UpdateDetail();
    partial void OnLegacyImportInfoChanged(ActivityLegacyImportInfo? value) => UpdateDetail();

    partial void OnSelectedSessionChanged(ActivitySessionRow? value)
    {
        UpdateDetail();
        if (applyingSelection) return;
        ++detailChangeVersion;
        applyingSelection = true;
        try
        {
            SelectedEncounter = null;
            SelectedPlayer = null;
            ResetMaps();
            if (IsImportedDetail) SelectedDetailTab = ActivityDetailTab.Items;
        }
        finally { applyingSelection = false; }
        Encounters.Clear(); Players.Clear(); Mobs.Clear(); SessionLoot.Clear(); Timeline.Clear(); HistoryLiveTimeline = null;
        encounterPage = timelinePage = sessionLootPage = 0;
        HasMoreEncounters = HasMoreSessionLoot = HasMoreTimeline = false;
        DetailSummary = ActivitySummaryPresentation.Empty;
        SessionResources = new();
        LegacyImportInfo = null;
        cachedDetailQuery = null;
        ++detailVersion;
        if (SelectedPlaySession is not null) _ = page.RunSafelyAsync(RefreshDetailsAsync);
    }

    partial void OnSelectedEncounterChanged(ActivityEncounterRow? value) => OnFilterSelectionChanged();
    partial void OnSelectedPlayerChanged(ActivityPlayerRow? value) => OnFilterSelectionChanged();

    // An encounter or player narrows the details already on view.
    private void OnFilterSelectionChanged()
    {
        UpdateDetail();
        if (applyingSelection) return;
        ++detailChangeVersion; ++detailVersion;
        ResetEventPages();
        _ = page.RunSafelyAsync(RefreshDetailsAsync);
    }

    private void ResetEventPages()
    {
        timelinePage = sessionLootPage = 0;
        SessionLoot.Clear(); Timeline.Clear(); HistoryLiveTimeline = null;
        HasMoreSessionLoot = HasMoreTimeline = false;
    }

    // A run opens inside its play session. From a chart, an encounter keeps showing the whole session.
    private async Task OpenSessionDetailsAsync(Guid id, Guid? encounterId = null)
    {
        if (page.Persistence is not { } persistence) return;
        var run = await persistence.GetSessionAsync(id);
        if (page.IsDisposed || run?.PlaySessionId is not { } playSessionId) return;
        var keepWholeSession = encounterId is not null && SelectedPlaySession?.Id == playSessionId && SelectedSession is null;
        await OpenPlaySessionDetailsAsync(playSessionId, keepWholeSession ? null : id, encounterId);
    }

    /// <summary>Opens a run in History, inside its play session.</summary>
    internal void OpenRun(Guid runId) => _ = page.RunSafelyAsync(() => OpenSessionDetailsAsync(runId));

    /// <summary>Opens a chart's encounter in History, inside its play session.</summary>
    internal Task OpenEncounterAsync(Guid encounterId) => page.RunSafelyAsync(async () =>
    {
        if (page.Persistence is { } persistence && await persistence.GetEncounterAsync(encounterId) is { } encounter)
            await OpenSessionDetailsAsync(encounter.SessionId, encounter.Id);
    });

    [RelayCommand] private Task OpenTimelineEncounterAsync(Guid encounterId) => OpenEncounterAsync(encounterId);
    [RelayCommand] private Task LoadMoreEncountersAsync() => page.RunSafelyAsync(() => LoadEncountersAsync(true));
    [RelayCommand] private Task LoadMoreTimelineAsync() => page.RunSafelyAsync(() => LoadTimelineAsync(true));
    [RelayCommand] private Task LoadMoreSessionLootAsync() => page.RunSafelyAsync(() => LoadSessionLootAsync(true));

    [RelayCommand]
    private void ClearEncounter()
    {
        if (IsShowingMap) SelectedMap = null;
        else SelectedEncounter = null;
    }

    [RelayCommand] private void ClearPlayer() => SelectedPlayer = null;
    [RelayCommand] private void ClearRun() => SelectedSession = null;

    // Each export uses the exact query of the loot list it is opened from, across all pages.
    [RelayCommand]
    private Task ExportSessionLoot(LootExportFormat format) =>
        Export.ExportToFileAsync(CanExportSessionLoot ? DetailQuery(true) with { LootOnly = true } : null, format);

    // With a run chosen only that run is deleted; otherwise the whole play session.
    [RelayCommand]
    private async Task DeleteSession()
    {
        if (page.Dialogs is not { } dialogs || page.Persistence is not { } persistence || !CanDeleteSelection) return;
        var runId = SelectedSession?.Id;
        var playSessionId = SelectedPlaySession?.Id;
        var (label, target) = SelectedSession is { } run ? ("run", $"run “{run.Title}”") : ("session", $"session “{SelectedPlaySession?.Title}”");
        if (!await dialogs.ConfirmAsync($"Delete {label}?", $"Delete the {target} and all activity in it? This cannot be undone.", $"Delete {label}")
            || (SelectedPlaySession?.Id, SelectedSession?.Id) != (playSessionId, runId) || !CanDeleteSelection) return;
        await page.RunSafelyAsync(async () =>
        {
            if (runId is { } selectedRun)
            {
                await persistence.DeleteSessionAsync(selectedRun);
                SelectedSession = null;
            }
            else if (playSessionId is { } playSession)
            {
                await persistence.DeletePlaySessionAsync(playSession);
                SelectedPlaySession = null;
            }
            await LoadPlaySessionsAsync(false);
        });
    }

    public void Dispose()
    {
        page.Export.PropertyChanged -= OnExportChanged;
        DisposeHistoryReplay();
        ++detailVersion;
        historyQueryCancellation.Cancel(); detailQueryCancellation.Cancel();
        historyQueryCancellation.Dispose(); detailQueryCancellation.Dispose();
    }
}

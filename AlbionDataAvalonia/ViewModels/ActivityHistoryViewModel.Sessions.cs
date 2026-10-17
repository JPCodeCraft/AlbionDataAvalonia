using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
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

// The session list on the left of History, and opening a session from elsewhere.
public partial class ActivityHistoryViewModel
{
    private CancellationTokenSource historyQueryCancellation = new();
    private ActivityPlaySessionHeader? cachedDetailHeader;
    private int sessionPage;
    private long sessionListVersion;
    private long historyChangeVersion = 1;
    private long historyReadVersion;

    [ObservableProperty] private ObservableCollection<ActivityPlaySessionRow> playSessions = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPlaySession))]
    private ActivityPlaySessionRow? selectedPlaySession;
    [ObservableProperty] private bool hasMoreSessions;

    public bool HasSelectedPlaySession => SelectedPlaySession is not null;

    private ActivityQuery HistoryQuery() => new() { Scope = Filters.Scope };

    private async Task LoadPlaySessionsAsync(bool append, bool refresh = false)
    {
        if (page.Persistence is not { } persistence) return;
        var version = ++sessionListVersion;
        var dataVersion = historyChangeVersion;
        var cancellationToken = BeginRead(ref historyQueryCancellation);
        var query = HistoryQuery();
        var pageIndex = append ? sessionPage + 1 : refresh ? sessionPage : 0;
        var result = await ReadPagesAsync(index => persistence.GetPlaySessionsAsync(query, index, cancellationToken), pageIndex, append);
        if (page.IsDisposed || version != sessionListVersion || HistoryQuery() != query) return;
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
            sessionPage = pageIndex;
            HasMoreSessions = result.HasMore;
            UpdateDetail();
            if (!append) historyReadVersion = dataVersion;
        }
        finally { applyingSelection = false; }
        await LoadPlaySessionPartiesAsync(cancellationToken);
    }

    // Party members come from each run's recorded events.
    private async Task LoadPlaySessionPartiesAsync(CancellationToken cancellationToken)
    {
        if (page.Persistence is not { } persistence) return;
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
        if (page.IsDisposed) return;
        foreach (var row in rows)
            row.SetParty(row.Header, row.Runs.SelectMany(run => members.TryGetValue(run.Id, out var names) ? names : [])
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private void ApplyRunRows(ActivityPlaySessionHeader header)
    {
        var scope = Filters.Scope;
        ApplyHeaderRows(Sessions, header.Runs, row => row.Id, source => source.Id,
            source => new ActivitySessionRow(source, scope), (row, source) => row.Update(source, scope), false);
        UpdateDetail();
    }

    partial void OnSelectedPlaySessionChanged(ActivityPlaySessionRow? value)
    {
        UpdateDetail();
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
            if (IsImportedDetail) SelectedDetailTab = ActivityDetailTab.Items;
        }
        finally { applyingSelection = false; }
        ResetDetailRows();
        if (value is not null) _ = page.RunSafelyAsync(RefreshDetailsAsync);
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
        UpdateDetail();
        ++detailVersion;
    }

    internal async Task OpenPlaySessionDetailsAsync(Guid playSessionId, Guid? runId = null, Guid? encounterId = null)
    {
        if (page.Persistence is not { } persistence) return;
        var header = await persistence.GetPlaySessionAsync(playSessionId);
        if (page.IsDisposed || header is null) return;
        page.ShowTab(ActivityTab.History);
        await LoadPlaySessionsAsync(false);
        var row = PlaySessions.FirstOrDefault(existing => existing.Id == playSessionId);
        if (row is null) { row = new(header, Filters.Scope); PlaySessions.Insert(0, row); }
        var encounter = encounterId is { } selectedEncounterId ? await persistence.GetEncounterAsync(selectedEncounterId) : null;
        if (page.IsDisposed) return;
        applyingSelection = true;
        try
        {
            SelectedPlaySession = row;
            ApplyRunRows(header);
            SelectedSession = runId is { } id ? Sessions.FirstOrDefault(run => run.Id == id) : null;
            SelectedPlayer = null;
            ResetMaps();
            ResetDetailRows();
            SelectedEncounter = encounter is null ? null : new ActivityEncounterRow(encounter, Filters.Scope);
            if (SelectedEncounter is not null) Encounters.Add(SelectedEncounter);
            if (SelectedEncounter is not null && SelectedDetailTab == ActivityDetailTab.Maps) SelectedDetailTab = ActivityDetailTab.Encounters;
            if (IsImportedDetail) SelectedDetailTab = ActivityDetailTab.Items;
            ++detailChangeVersion;
        }
        finally { applyingSelection = false; }
        UpdateDetail();
        await RefreshDetailsAsync();
    }

    [RelayCommand] private Task LoadMoreSessionsAsync() => page.RunSafelyAsync(() => LoadPlaySessionsAsync(true));
}

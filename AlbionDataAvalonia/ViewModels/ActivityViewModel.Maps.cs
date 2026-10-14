using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public sealed class ActivityMapRow(ActivityMap source, DateTime? durationEndUtc = null) : ObservableObject
{
    private DateTime? durationEndUtc = durationEndUtc;
    public ActivityMap Source { get; private set; } = source;
    public string RawMapId => Source.RawMapId;
    public string Name => Source.Name;
    public string StartedText => ActivityDisplay.LocalDate(Source.StartedAtUtc);
    public int VisitCount => Source.Visits;
    public double DurationSeconds => Source.GetDurationSeconds(durationEndUtc ?? DateTime.UtcNow);
    public string DurationText => Source.Visits > 0 ? ActivityDisplay.Duration(DurationSeconds) : "Not recorded";

    public void Update(ActivityMap value, DateTime? endUtc)
    {
        if (Source == value && durationEndUtc == endUtc) return;
        Source = value;
        durationEndUtc = endUtc;
        OnPropertyChanged(string.Empty);
    }

    public void RefreshDuration() => OnPropertyChanged(nameof(DurationText));
}

public partial class ActivityViewModel
{
    private const int MapsDetailTabIndex = 6;
    private int mapPage;
    private Guid? mapsSessionId;
    private long mapsDataVersion = -1;
    private (Guid SessionId, string? MapId, ActivityScope Scope, long Version)? mapSummaryKey;
    private ActivityMapSummary? cachedMapSummary;

    [ObservableProperty] private ObservableCollection<ActivityMapRow> maps = new();
    [ObservableProperty] private ActivityMapRow? selectedMap;
    [ObservableProperty] private bool hasMoreMaps;

    public bool IsShowingMap => SelectedDetailTabIndex == MapsDetailTabIndex && SelectedMap is not null;
    public bool HasSelectedDetailSelection => HasSelectedEncounter || IsShowingMap;

    private async Task RefreshMapsAsync(long version, ActivitySession session, CancellationToken cancellationToken)
    {
        await LoadMapsAsync(false, version);
        if (!IsCurrentDetail(version, session.Id) || SelectedDetailTabIndex != MapsDetailTabIndex || persistence is null) return;
        var map = SelectedMap;
        var scope = SelectedScope.Value;
        var key = (session.Id, map?.RawMapId, scope, detailDataVersion);
        var totals = cachedMapSummary;
        if (totals is null || mapSummaryKey != key)
        {
            totals = map is null
                ? new ActivityMapSummary(ActivityProjection.GetSummary(session, scope),
                    await persistence.GetCombatDurationSecondsAsync(new ActivityQuery { SessionId = session.Id, Scope = scope }, cancellationToken))
                : await persistence.GetMapSummaryAsync(session.Id, map.RawMapId, scope, cancellationToken);
        }
        if (!IsCurrentDetail(version, session.Id) || SelectedDetailTabIndex != MapsDetailTabIndex ||
            SelectedMap?.RawMapId != map?.RawMapId || SelectedScope.Value != scope) return;
        cachedMapSummary = totals;
        mapSummaryKey = key;
        var duration = map?.DurationSeconds ?? ActivityProjection.GetDurationSeconds(session, DateTime.UtcNow);
        DetailSummary = new(totals.Summary, ActivityProjection.GetRates(totals.Summary, duration, totals.CombatDurationSeconds),
            true, map is null || map.Source.Visits > 0);
        NotifyDetailProperties();
    }

    private async Task LoadMapsAsync(bool append, long version)
    {
        if (persistence is null || SelectedSession is not { } selected || SelectedDetailTabIndex != MapsDetailTabIndex) return;
        var sessionId = selected.Id;
        var dataVersion = detailDataVersion;
        if (!append && mapsSessionId == sessionId && mapsDataVersion == dataVersion) return;
        var page = append ? mapPage + 1 : mapPage;
        var result = await ReadPagesAsync(index => persistence.GetMapsAsync(sessionId, index, detailQueryCancellation.Token), page, append);
        if (!IsCurrentDetail(version, sessionId) || SelectedDetailTabIndex != MapsDetailTabIndex) return;
        var selectedId = SelectedMap?.RawMapId;
        var durationEndUtc = tracker?.CurrentSessionId == sessionId ? (DateTime?)null : selected.Source.LastObservedAtUtc;
        applyingSelection = true;
        try
        {
            ApplyHeaderRows(Maps, result.Items, row => row.RawMapId, source => source.RawMapId,
                source => new ActivityMapRow(source, durationEndUtc), (row, source) => row.Update(source, durationEndUtc), append);
            SelectedMap = selectedId is null ? null : Maps.FirstOrDefault(row => row.RawMapId == selectedId);
            mapPage = page;
            HasMoreMaps = result.HasMore;
            mapsSessionId = sessionId;
            mapsDataVersion = dataVersion;
        }
        finally { applyingSelection = false; }
    }

    partial void OnSelectedMapChanged(ActivityMapRow? value)
    {
        NotifyDetailProperties();
        if (applyingSelection) return;
        ++detailChangeVersion;
        ++detailVersion;
        // Never briefly label the previous map's totals as the newly selected map.
        DetailSummary = ActivitySummaryPresentation.Empty;
        if (isActive && SelectedTabIndex == 1 && SelectedDetailTabIndex == MapsDetailTabIndex)
            _ = RunSafelyAsync(RefreshDetailsAsync);
    }

    private void ResetMaps()
    {
        SelectedMap = null;
        Maps.Clear();
        HasMoreMaps = false;
        mapPage = 0;
        mapsSessionId = null;
        mapsDataVersion = -1;
        mapSummaryKey = null;
        cachedMapSummary = null;
    }

    [RelayCommand]
    private async Task LoadMoreMapsAsync() => await RunSafelyAsync(() => LoadMapsAsync(true, detailVersion));
}

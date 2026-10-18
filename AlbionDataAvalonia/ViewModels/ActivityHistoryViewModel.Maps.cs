using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Locations;
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

public sealed class ActivityMapRow(ActivityMap source, DateTime? durationEndUtc = null) : ObservableObject
{
    private DateTime? durationEndUtc = durationEndUtc;
    public ActivityMap Source { get; private set; } = source;
    public string RawMapId => Source.RawMapId;
    public string Name => Source.Name;
    public string DetailsText => FormatDetails(Source.Metadata);
    public bool HasDetails => DetailsText.Length > 0;
    public string StartedText => UiFormat.LocalDateTime(Source.StartedAtUtc);
    public int VisitCount => Source.Visits;
    public double DurationSeconds => Source.GetDurationSeconds(durationEndUtc ?? DateTime.UtcNow);
    public string DurationText => Source.Visits > 0 ? UiFormat.Duration(DurationSeconds) : UiText.NotRecorded;

    public void Update(ActivityMap value, DateTime? endUtc)
    {
        if (Source == value && durationEndUtc == endUtc) return;
        Source = value;
        durationEndUtc = endUtc;
        OnPropertyChanged(string.Empty);
    }

    public void RefreshDuration() => OnPropertyChanged(nameof(DurationText));

    private static string FormatDetails(ActivityMapMetadata? metadata)
    {
        if (metadata is null) return string.Empty;
        var parts = new List<string>();
        if (metadata.Tier is > 0)
            parts.Add(metadata.Enchantment is > 0 ? $"T{metadata.Tier}.{metadata.Enchantment}" : $"T{metadata.Tier}");
        if (!string.IsNullOrWhiteSpace(metadata.ZoneType)) parts.Add($"{metadata.ZoneType} zone");
        if (!string.IsNullOrWhiteSpace(metadata.Rarity)) parts.Add(metadata.Rarity);
        if (!string.IsNullOrWhiteSpace(metadata.Mode)) parts.Add(metadata.Mode);
        if (!string.IsNullOrWhiteSpace(metadata.Faction)) parts.Add(metadata.Faction);
        if (!string.IsNullOrWhiteSpace(metadata.OriginMapId))
        {
            var origin = AlbionLocations.ResolveActivityLocation(metadata.OriginMapId);
            if (origin.Kind != ActivityMapKind.Unknown) parts.Add($"From {origin.DisplayName}");
        }
        return string.Join(" · ", parts);
    }
}

public partial class ActivityHistoryViewModel
{
    private int mapPage;
    private ActivityQuery? mapsOwner;
    private long mapsDataVersion = -1;
    private (ActivityQuery Owner, string? MapId, long Version)? mapSummaryKey;
    private ActivityMapSummary? cachedMapSummary;

    [ObservableProperty] private ObservableCollection<ActivityMapRow> maps = new();
    [ObservableProperty] private ActivityMapRow? selectedMap;
    [ObservableProperty] private bool hasMoreMaps;

    // A session with a single run maps that run; with several, a run chip picks one. Combined sessions have no one run.
    private Guid? MapRunId => IsCombined ? null : SelectedSession?.Id ?? (SelectedPlaySession?.Runs is { Count: 1 } runs ? runs[0].Id : null);

    // Maps cover the chosen run, or every run of the play sessions on view.
    private async Task RefreshMapsAsync(long version, Guid playSessionId, IReadOnlyList<ActivityPlaySessionHeader> headers, ActivitySession? run,
        CancellationToken cancellationToken)
    {
        await LoadMapsAsync(false, version);
        if (!IsCurrentDetail(version, playSessionId) || SelectedDetailTab != ActivityDetailTab.Maps || page.Persistence is not { } persistence) return;
        var map = SelectedMap;
        var scope = Filters.Scope;
        var owner = SelectionOwner(run?.Id) with { Scope = scope };
        var key = (owner, map?.RawMapId, detailDataVersion);
        var totals = cachedMapSummary;
        if (totals is null || mapSummaryKey != key)
        {
            totals = map is null
                ? new ActivityMapSummary(run is null ? ActivityPlaySessionProjection.Summary(headers.SelectMany(header => header.Runs), scope) : ActivityProjection.GetSummary(run, scope),
                    await persistence.GetCombatDurationSecondsAsync(owner, cancellationToken))
                : await persistence.GetMapSummaryAsync(owner, map.RawMapId, scope, cancellationToken);
        }
        if (!IsCurrentDetail(version, playSessionId) || SelectedDetailTab != ActivityDetailTab.Maps ||
            SelectedMap?.RawMapId != map?.RawMapId || Filters.Scope != scope) return;
        cachedMapSummary = totals;
        mapSummaryKey = key;
        var duration = map?.DurationSeconds ?? (run is not null ? ActivityProjection.GetDurationSeconds(run, DateTime.UtcNow)
            : PlayDurations(headers, null, DateTime.UtcNow).PlaySeconds);
        DetailSummary = new(WithImportedTotals(totals.Summary), ActivityProjection.GetRates(totals.Summary, duration, totals.CombatDurationSeconds),
            true, map is null || map.Source.Visits > 0);
    }

    private async Task LoadMapsAsync(bool append, long version)
    {
        if (page.Persistence is not { } persistence || SelectedPlaySession is not { } playSession || SelectedDetailTab != ActivityDetailTab.Maps) return;
        var run = SelectedSession;
        var owner = SelectionOwner(run?.Id);
        var dataVersion = detailDataVersion;
        if (!append && mapsOwner == owner && mapsDataVersion == dataVersion) return;
        var pageIndex = append ? mapPage + 1 : mapPage;
        var result = await ReadPagesAsync(index => persistence.GetMapsAsync(owner, index, detailQueryCancellation.Token), pageIndex, append);
        if (!IsCurrentDetail(version, playSession.Id) || SelectionOwner(SelectedSession?.Id) != owner || SelectedDetailTab != ActivityDetailTab.Maps) return;
        var selectedId = SelectedMap?.RawMapId;
        // A visit still open keeps counting; a closed run or session ends at its last observation.
        var rows = SelectedRows;
        var live = run is not null ? page.Tracker?.CurrentSessionId == run.Id : rows.Any(row => row.IsOpen);
        var durationEndUtc = live ? (DateTime?)null : run?.Source.LastObservedAtUtc ??
            rows.Max(row => row.Source.EndedAtUtc ?? row.Source.LastActivityAtUtc);
        applyingSelection = true;
        try
        {
            ApplyHeaderRows(Maps, result.Items, row => row.RawMapId, source => source.RawMapId,
                source => new ActivityMapRow(source, durationEndUtc), (row, source) => row.Update(source, durationEndUtc), append);
            SelectedMap = selectedId is null ? null : Maps.FirstOrDefault(row => row.RawMapId == selectedId);
            mapPage = pageIndex;
            HasMoreMaps = result.HasMore;
            mapsOwner = owner;
            mapsDataVersion = dataVersion;
        }
        finally { applyingSelection = false; }
    }

    partial void OnSelectedMapChanged(ActivityMapRow? value)
    {
        UpdateDetail();
        if (applyingSelection) return;
        ++detailChangeVersion;
        ++detailVersion;
        // Never briefly label the previous map's totals as the newly selected map.
        DetailSummary = ActivitySummaryPresentation.Empty;
        if (page.IsShowing(ActivityTab.History) && SelectedDetailTab == ActivityDetailTab.Maps)
            _ = page.RunSafelyAsync(RefreshDetailsAsync);
    }

    private void ResetMaps()
    {
        SelectedMap = null;
        Maps.Clear();
        HasMoreMaps = false;
        mapPage = 0;
        mapsOwner = null;
        mapsDataVersion = -1;
        mapSummaryKey = null;
        cachedMapSummary = null;
    }

    [RelayCommand] private Task LoadMoreMapsAsync() => page.RunSafelyAsync(() => LoadMapsAsync(true, detailVersion));

    [RelayCommand]
    private void ShowRunOnMap()
    {
        if (MapRunId is { } id) page.MapWindow?.ShowRun(id);
    }
}

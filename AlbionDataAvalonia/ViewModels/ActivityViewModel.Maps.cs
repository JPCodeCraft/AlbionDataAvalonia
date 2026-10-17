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
    public string DurationText => Source.Visits > 0 ? UiFormat.Duration(DurationSeconds) : "Not recorded";

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

public partial class ActivityViewModel
{
    private const int MapsDetailTabIndex = 6;
    private int mapPage;
    private (Guid PlaySessionId, Guid? RunId)? mapsOwner;
    private long mapsDataVersion = -1;
    private (Guid PlaySessionId, Guid? RunId, string? MapId, ActivityScope Scope, long Version)? mapSummaryKey;
    private ActivityMapSummary? cachedMapSummary;

    [ObservableProperty] private ObservableCollection<ActivityMapRow> maps = new();
    [ObservableProperty] private ActivityMapRow? selectedMap;
    [ObservableProperty] private bool hasMoreMaps;

    public bool IsShowingMap => SelectedDetailTabIndex == MapsDetailTabIndex && SelectedMap is not null;
    public bool HasSelectedDetailSelection => HasSelectedEncounter || IsShowingMap;
    public string DetailMapMetadataText => IsShowingMap ? SelectedMap!.DetailsText : string.Empty;
    public bool HasDetailMapMetadata => DetailMapMetadataText.Length > 0;

    // Maps cover the chosen run, or every run of the play session.
    private async Task RefreshMapsAsync(long version, ActivityPlaySessionHeader header, ActivitySession? run, CancellationToken cancellationToken)
    {
        var playSessionId = header.PlaySession.Id;
        await LoadMapsAsync(false, version);
        if (!IsCurrentDetail(version, playSessionId) || SelectedDetailTabIndex != MapsDetailTabIndex || persistence is null) return;
        var map = SelectedMap;
        var scope = SelectedScope.Value;
        var owner = new ActivityQuery { PlaySessionId = playSessionId, SessionId = run?.Id, Scope = scope };
        var key = (playSessionId, run?.Id, map?.RawMapId, scope, detailDataVersion);
        var totals = cachedMapSummary;
        if (totals is null || mapSummaryKey != key)
        {
            totals = map is null
                ? new ActivityMapSummary(run is null ? ActivityPlaySessionProjection.Summary(header.Runs, scope) : ActivityProjection.GetSummary(run, scope),
                    await persistence.GetCombatDurationSecondsAsync(owner, cancellationToken))
                : await persistence.GetMapSummaryAsync(owner, map.RawMapId, scope, cancellationToken);
        }
        if (!IsCurrentDetail(version, playSessionId) || SelectedDetailTabIndex != MapsDetailTabIndex ||
            SelectedMap?.RawMapId != map?.RawMapId || SelectedScope.Value != scope) return;
        cachedMapSummary = totals;
        mapSummaryKey = key;
        var duration = map?.DurationSeconds ?? (run is not null ? ActivityProjection.GetDurationSeconds(run, DateTime.UtcNow)
            : ActivityPlaySessionProjection.Durations(header.PlaySession, header.Runs, null, DateTime.UtcNow).PlaySeconds);
        DetailSummary = new(totals.Summary, ActivityProjection.GetRates(totals.Summary, duration, totals.CombatDurationSeconds),
            true, map is null || map.Source.Visits > 0);
        NotifyDetailProperties();
    }

    private async Task LoadMapsAsync(bool append, long version)
    {
        if (persistence is null || SelectedPlaySession is not { } playSession || SelectedDetailTabIndex != MapsDetailTabIndex) return;
        var run = SelectedSession;
        var owner = (playSession.Id, run?.Id);
        var dataVersion = detailDataVersion;
        if (!append && mapsOwner == owner && mapsDataVersion == dataVersion) return;
        var page = append ? mapPage + 1 : mapPage;
        var query = new ActivityQuery { PlaySessionId = playSession.Id, SessionId = run?.Id };
        var result = await ReadPagesAsync(index => persistence.GetMapsAsync(query, index, detailQueryCancellation.Token), page, append);
        if (!IsCurrentDetail(version, playSession.Id) || SelectedSession?.Id != run?.Id || SelectedDetailTabIndex != MapsDetailTabIndex) return;
        var selectedId = SelectedMap?.RawMapId;
        // A visit still open keeps counting; a closed run or session ends at its last observation.
        var live = run is not null ? tracker?.CurrentSessionId == run.Id : playSession.IsOpen;
        var durationEndUtc = live ? (DateTime?)null : run?.Source.LastObservedAtUtc ?? playSession.Source.EndedAtUtc ?? playSession.Source.LastActivityAtUtc;
        applyingSelection = true;
        try
        {
            ApplyHeaderRows(Maps, result.Items, row => row.RawMapId, source => source.RawMapId,
                source => new ActivityMapRow(source, durationEndUtc), (row, source) => row.Update(source, durationEndUtc), append);
            SelectedMap = selectedId is null ? null : Maps.FirstOrDefault(row => row.RawMapId == selectedId);
            mapPage = page;
            HasMoreMaps = result.HasMore;
            mapsOwner = owner;
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
        mapsOwner = null;
        mapsDataVersion = -1;
        mapSummaryKey = null;
        cachedMapSummary = null;
    }

    [RelayCommand]
    private async Task LoadMoreMapsAsync() => await RunSafelyAsync(() => LoadMapsAsync(true, detailVersion));
}

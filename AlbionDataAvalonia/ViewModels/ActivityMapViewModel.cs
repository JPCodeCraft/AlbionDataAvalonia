using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Maps;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.Ui;
using AlbionDataAvalonia.Views;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public sealed record ActivityMapRunOption(Guid Id, string Label);

public sealed record ActivityMapRunMapOption(string RawMapId, string Label, DateTime StartUtc, DateTime EndUtc,
    IReadOnlyList<ActivityPathPoint> Points, IReadOnlyList<ActivityMapMarker> Markers, string? Layout = null);

/// <summary>Feeds the map window: the live map follows the tracker; history replays one recorded run.</summary>
public sealed partial class ActivityMapViewModel : ObservableObject, IDisposable
{
    private const double TimelineMax = 1000;
    private static readonly TimeSpan ReplayLength = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FadeRefresh = TimeSpan.FromSeconds(1);
    // Without a minimap, the path is shown on a grid at least this many units across.
    private const double MinimumFallbackSize = 160;

    private readonly ActivityTrackerService tracker;
    private readonly ActivityPersistence persistence;
    private readonly MapDataService maps;
    private readonly UserSettings settings;
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer replayTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private ActivityLiveMap live = ActivityLiveMap.Empty;
    private long liveVersion = -1;
    private DateTime liveRenderedAt;
    private ZoneView? liveZone;
    private string liveZoneRequest = "";
    private (double MinX, double MinY, double MaxX, double MaxY)? liveFallbackBounds;
    private ActivityRunMap? runMap;
    private ZoneView? historyZone;
    private Guid? requestedRunId;
    private bool runsLoaded;
    private bool disposed;
    private readonly LatestLoad loads = new();

    public ActivityMapViewModel(ActivityTrackerService tracker, ActivityPersistence persistence, MapDataService maps, UserSettings settings)
    {
        this.tracker = tracker;
        this.persistence = persistence;
        this.maps = maps;
        this.settings = settings;
        selectedScopeIndex = settings.MapPartyEvents ? 1 : 0;
        selectedFadeIndex = Math.Max(0, Array.FindIndex(FadeOptionValues, x => x == settings.MapFadeMinutes));
        if (FadeOptionValues[selectedFadeIndex] != settings.MapFadeMinutes) selectedFadeIndex = 1;
        liveTimer.Tick += (_, _) => RefreshLive();
        replayTimer.Tick += (_, _) => AdvanceReplay();
    }

    private static readonly int[] FadeOptionValues = [1, 5, 15, 0];
    public IReadOnlyList<Option<int>> ModeOptions { get; } = [new(0, "Live"), new(1, "History")];
    public IReadOnlyList<Option<int>> FadeOptions { get; } = [new(1, "1 min"), new(5, "5 min"), new(15, "15 min"), new(0, "Never")];
    public IReadOnlyList<Option<int>> ScopeOptions { get; } = [new(0, "Personal"), new(1, "Party")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(IsHistory), nameof(ShowFollow))]
    private int selectedModeIndex;

    [ObservableProperty] private int selectedFadeIndex;
    [ObservableProperty] private int selectedScopeIndex;
    [ObservableProperty] private bool follow = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFollow))]
    private ActivityMapScene? scene;
    [ObservableProperty] private string title = "Map";
    [ObservableProperty] private string subtitle = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string note = "";

    [ObservableProperty] private bool showPath = true;
    [ObservableProperty] private bool showKills = true;
    [ObservableProperty] private bool showLoot = true;
    [ObservableProperty] private bool showGathering = true;
    [ObservableProperty] private bool showDeaths = true;
    [ObservableProperty] private bool showFeatures = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuns))]
    private IReadOnlyList<ActivityMapRunOption> runs = [];

    [ObservableProperty] private ActivityMapRunOption? selectedRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleRunMaps))]
    private IReadOnlyList<ActivityMapRunMapOption> runMaps = [];

    [ObservableProperty] private ActivityMapRunMapOption? selectedRunMap;
    [ObservableProperty] private double timelineValue = TimelineMax;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayButtonText))]
    [NotifyPropertyChangedFor(nameof(PlayButtonName))]
    private bool isPlaying;

    [ObservableProperty] private string timelineStartText = "";
    [ObservableProperty] private string timelineEndText = "";
    [ObservableProperty] private string timelineNowText = "";
    [ObservableProperty] private string runSummaryText = "";
    [ObservableProperty] private IReadOnlyList<ActivityMapTick> ticks = [];

    public bool IsLive => SelectedModeIndex == 0;
    public bool IsHistory => SelectedModeIndex == 1;
    public bool ShowFollow => IsLive && Scene?.Position is not null;
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool HasRuns => Runs.Count > 0;
    public bool HasMultipleRunMaps => RunMaps.Count > 1;
    public string PlayButtonText => IsPlaying ? "❚❚" : "▶";
    public string PlayButtonName => IsPlaying ? "Pause replay" : "Play replay";
    public double TimelineMaximum => TimelineMax;

    private ActivityMapLayers Layers =>
        (ShowPath ? ActivityMapLayers.Path : 0) | (ShowKills ? ActivityMapLayers.Kills : 0) | (ShowLoot ? ActivityMapLayers.Loot : 0)
        | (ShowGathering ? ActivityMapLayers.Gathering : 0) | (ShowDeaths ? ActivityMapLayers.Deaths : 0) | (ShowFeatures ? ActivityMapLayers.Features : 0);

    private double FadeSeconds => FadeOptionValues[Math.Clamp(SelectedFadeIndex, 0, FadeOptionValues.Length - 1)] * 60d;

    public void Start()
    {
        liveTimer.Start();
        RefreshLive();
    }

    /// <summary>Opens history on a run, for "Show on map" in the Activity tab.</summary>
    public void ShowRun(Guid runId)
    {
        requestedRunId = runId;
        runsLoaded = false;
        if (IsHistory) _ = LoadRunsAsync();
        else SelectedModeIndex = 1;
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        StopReplay();
        if (IsHistory && !runsLoaded) _ = LoadRunsAsync();
        Rebuild();
    }

    partial void OnSelectedFadeIndexChanged(int value)
    {
        settings.MapFadeMinutes = FadeOptionValues[Math.Clamp(value, 0, FadeOptionValues.Length - 1)];
        Rebuild();
    }

    partial void OnSelectedScopeIndexChanged(int value)
    {
        settings.MapPartyEvents = value == 1;
        if (SelectedRunMap is { } option) UpdateHistoryDetails(option);
        Rebuild();
    }

    partial void OnShowPathChanged(bool value) => Rebuild();
    partial void OnShowKillsChanged(bool value) => Rebuild();
    partial void OnShowLootChanged(bool value) => Rebuild();
    partial void OnShowGatheringChanged(bool value) => Rebuild();
    partial void OnShowDeathsChanged(bool value) => Rebuild();
    partial void OnShowFeaturesChanged(bool value) => Rebuild();

    partial void OnSelectedRunChanged(ActivityMapRunOption? value)
    {
        StopReplay();
        if (value is not null) _ = LoadRunAsync(value.Id);
    }

    partial void OnSelectedRunMapChanged(ActivityMapRunMapOption? value)
    {
        StopReplay();
        if (value is not null) _ = LoadHistoryZoneAsync(value);
    }

    partial void OnTimelineValueChanged(double value)
    {
        if (IsHistory) RebuildHistory();
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            StopReplay();
            return;
        }

        if (SelectedRunMap is null) return;
        if (TimelineValue >= TimelineMax) TimelineValue = 0;
        IsPlaying = true;
        replayTimer.Start();
    }

    [RelayCommand]
    private void FollowMe() => Follow = true;

    private void AdvanceReplay()
    {
        var next = TimelineValue + TimelineMax * replayTimer.Interval.TotalSeconds / ReplayLength.TotalSeconds;
        TimelineValue = Math.Min(TimelineMax, next);
        if (TimelineValue >= TimelineMax) StopReplay();
    }

    private void StopReplay()
    {
        replayTimer.Stop();
        IsPlaying = false;
    }

    private void Rebuild()
    {
        if (IsLive) RebuildLive();
        else RebuildHistory();
    }

    private void RefreshLive()
    {
        if (disposed || !IsLive) return;
        var version = tracker.LiveMapVersion;
        if (version != liveVersion)
        {
            liveVersion = version;
            live = tracker.GetLiveMap();
            // A Mists layout can arrive after the map itself; the path so far stays on the same grid.
            var request = $"{live.RawMapId}|{live.Layout}";
            if (request != liveZoneRequest)
            {
                if (!liveZoneRequest.StartsWith(live.RawMapId + "|", StringComparison.Ordinal)) liveFallbackBounds = null;
                liveZoneRequest = request;
                liveZone = null;
                if (live.RawMapId.Length > 0) _ = LoadLiveZoneAsync(request, live.RawMapId, live.Layout, live.MapName);
            }

            RebuildLive();
        }
        // Fading moves on even when nothing new happens.
        else if (FadeSeconds > 0 && DateTime.UtcNow - liveRenderedAt >= FadeRefresh) RebuildLive();
    }

    private async Task LoadLiveZoneAsync(string request, string rawMapId, string? layout, string mapName)
    {
        var view = await LoadZoneAsync(rawMapId, layout, mapName);
        if (disposed || request != liveZoneRequest) return;
        liveZone = view;
        RebuildLive();
    }

    private void RebuildLive()
    {
        if (!IsLive) return;
        liveRenderedAt = DateTime.UtcNow;
        var zone = liveZone is { } view && view.RawMapId == live.RawMapId ? view : null;
        Title = zone?.Title ?? (string.IsNullOrWhiteSpace(live.MapName) ? "Map" : live.MapName);
        Subtitle = zone?.Subtitle ?? "";
        if (live.RawMapId.Length == 0)
        {
            Note = "Waiting for the game. The map follows you once you enter a zone.";
            Scene = null;
            return;
        }

        var geometry = zone?.Geometry ?? LiveFallbackGeometry();
        Note = live.Position is null ? "Waiting for your position. Take a few steps in game."
            : zone is { Failed: true } ? "Couldn't load map data. Check your connection; your path still shows."
            : zone is { Image: null } ? "No map image for this zone. Showing your path only."
            : "";
        if (geometry is null)
        {
            Scene = null;
            return;
        }

        Scene = new ActivityMapScene($"live:{live.RawMapId}:{zone?.Zone?.Id}", geometry, zone?.Image, zone?.Frame, live.Trail, Shown(live.Markers),
            zone?.Exits ?? [], zone?.Icons ?? [], live.Position, live.Heading, true, DateTime.UtcNow, FadeSeconds, null, null, Layers);
    }

    // Grows with the path so the view stays steady while walking a zone without a minimap.
    private MapImageGeometry? LiveFallbackGeometry()
    {
        var points = live.Trail.Select(x => (x.X, x.Y)).Concat(Shown(live.Markers).Select(x => (x.X, x.Y))).ToArray();
        if (points.Length == 0) return null;
        var bounds = (MinX: points.Min(x => x.X), MinY: points.Min(x => x.Y), MaxX: points.Max(x => x.X), MaxY: points.Max(x => x.Y));
        if (liveFallbackBounds is { } known)
            bounds = (Math.Min(known.MinX, bounds.MinX), Math.Min(known.MinY, bounds.MinY), Math.Max(known.MaxX, bounds.MaxX), Math.Max(known.MaxY, bounds.MaxY));
        liveFallbackBounds = bounds;
        return FallbackGeometry(bounds);
    }

    private static MapImageGeometry? FallbackGeometry((double MinX, double MinY, double MaxX, double MaxY) bounds)
    {
        var centerX = (bounds.MinX + bounds.MaxX) / 2;
        var centerY = (bounds.MinY + bounds.MaxY) / 2;
        var half = Math.Max(MinimumFallbackSize, Math.Max(bounds.MaxX - bounds.MinX, bounds.MaxY - bounds.MinY) + 60) / 2;
        return MapImageGeometry.From(new(centerX - half, centerY - half), new(centerX + half, centerY + half));
    }

    private async Task LoadRunsAsync()
    {
        var version = loads.Begin();
        try
        {
            var mapped = await persistence.GetMappedRunsAsync();
            if (disposed || !loads.IsCurrent(version)) return;
            runsLoaded = true;
            var requested = requestedRunId;
            var options = mapped.Select(x => new ActivityMapRunOption(x.Id, RunLabel(x))).ToList();
            // A run shown from the Activity tab is listed even if it recorded no positions yet.
            if (requested is { } id && options.All(x => x.Id != id) && await persistence.GetSessionAsync(id) is { } run)
                options.Insert(0, new ActivityMapRunOption(run.Id, RunLabel(run)));
            if (disposed || !loads.IsCurrent(version)) return;
            Runs = options;
            requestedRunId = null;
            SelectedRun = options.FirstOrDefault(x => x.Id == requested) ?? options.FirstOrDefault();
            if (SelectedRun is null)
            {
                Note = "No runs with positions yet. Runs record your path from now on.";
                Scene = null;
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log.Warning(error, "Could not load runs for the map.");
            Note = "Couldn't load your runs. Try again.";
        }
    }

    private async Task LoadRunAsync(Guid runId)
    {
        var version = loads.Begin();
        try
        {
            var loaded = await persistence.GetRunMapAsync(runId);
            if (disposed || !loads.IsCurrent(version)) return;
            runMap = loaded;
            historyZone = null;
            if (loaded is null)
            {
                RunMaps = [];
                SelectedRunMap = null;
                Note = "This run no longer exists.";
                Scene = null;
                return;
            }

            var visits = VisitDetails(loaded.Run);
            var points = loaded.Segments.SelectMany(x => x.ReadPoints().Select(point => (x.RawMapId, Point: point)))
                .GroupBy(x => x.RawMapId).ToDictionary(x => x.Key, x => (IReadOnlyList<ActivityPathPoint>)x.Select(y => y.Point).OrderBy(y => y.CapturedAtUtc).ToArray());
            var markers = loaded.Markers.GroupBy(x => x.RawMapId).ToDictionary(x => x.Key, x => (IReadOnlyList<ActivityMapMarker>)x.ToArray());
            var options = points.Keys.Union(markers.Keys).Select(id =>
            {
                var path = points.GetValueOrDefault(id) ?? [];
                var placed = markers.GetValueOrDefault(id) ?? [];
                var times = path.Select(x => x.CapturedAtUtc).Concat(placed.Select(x => x.CapturedAtUtc)).ToArray();
                var visit = visits.GetValueOrDefault(id);
                return new ActivityMapRunMapOption(id, visit.Name ?? loaded.Run.LocationName, times.Min(), times.Max(), path, placed, visit.Layout);
            }).OrderBy(x => x.StartUtc).ToArray();
            RunMaps = options;
            SelectedRunMap = options.FirstOrDefault();
            if (SelectedRunMap is null)
            {
                RunSummaryText = "";
                Ticks = [];
                Note = "This run recorded no positions. Runs record your path from now on.";
                Scene = null;
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Log.Warning(error, "Could not load the run map.");
            Note = "Couldn't load this run. Try again.";
        }
    }

    private async Task LoadHistoryZoneAsync(ActivityMapRunMapOption option)
    {
        historyZone = null;
        TimelineStartText = UiFormat.LocalShortTime(option.StartUtc);
        TimelineEndText = UiFormat.LocalShortTime(option.EndUtc);
        UpdateHistoryDetails(option);
        TimelineValue = TimelineMax;
        RebuildHistory();
        var view = await LoadZoneAsync(option.RawMapId, option.Layout, option.Label);
        if (disposed || SelectedRunMap != option) return;
        historyZone = view;
        RebuildHistory();
    }

    private void RebuildHistory()
    {
        if (!IsHistory) return;
        if (runMap is null || SelectedRunMap is not { } option)
        {
            Title = "History";
            Subtitle = "";
            Scene = null;
            return;
        }

        var zone = historyZone is { } view && view.RawMapId == option.RawMapId ? view : null;
        Title = zone?.Title ?? option.Label;
        Subtitle = zone?.Subtitle ?? "";
        var until = TimelineValue >= TimelineMax ? (DateTime?)null
            : option.StartUtc + TimeSpan.FromSeconds((option.EndUtc - option.StartUtc).TotalSeconds * TimelineValue / TimelineMax);
        TimelineNowText = until is { } moment ? UiFormat.LocalTime(moment) : "";
        var markers = Shown(option.Markers);
        var points = option.Points.Select(x => (x.X, x.Y)).Concat(markers.Select(x => (x.X, x.Y))).ToArray();
        var geometry = zone?.Geometry ?? (points.Length == 0 ? null
            : FallbackGeometry((points.Min(x => x.X), points.Min(x => x.Y), points.Max(x => x.X), points.Max(x => x.Y))));
        Note = zone is { Failed: true } ? "Couldn't load map data. Check your connection; your path still shows."
            : zone is { Image: null } ? "No map image for this zone. Showing your path only."
            : "";
        if (geometry is null)
        {
            Scene = null;
            return;
        }

        Scene = new ActivityMapScene($"history:{runMap.Run.Id}:{option.RawMapId}:{zone?.Zone?.Id}", geometry, zone?.Image, zone?.Frame, option.Points,
            markers, zone?.Exits ?? [], zone?.Icons ?? [], null, null, false, DateTime.UtcNow, 0, option.StartUtc, until, Layers);
    }

    private async Task<ZoneView> LoadZoneAsync(string rawMapId, string? layout, string fallbackName)
    {
        try
        {
            var zone = await maps.FindZoneAsync(rawMapId, layout);
            if (zone is null) return new ZoneView(rawMapId, null, null, null, [], [], fallbackName, "", null, false);
            var imageTask = maps.GetImageAsync(zone);
            var iconsTask = maps.GetIconsAsync(zone);
            var frameTask = maps.GetFrameImageAsync();
            var exits = new List<ActivityMapExit>();
            foreach (var exit in zone.Exits)
            {
                var target = await maps.ZoneAsync(exit.TargetId);
                exits.Add(new ActivityMapExit(exit.Position, $"To {target?.DisplayName ?? exit.TargetId}", Zone(target?.PvpCategory)));
            }

            var icons = await iconsTask;
            var iconImages = new Dictionary<string, Bitmap?>();
            foreach (var type in icons.Select(x => x.MarkerType).Distinct()) iconImages[type] = await maps.GetIconImageAsync(type);
            // Without its minimap a zone still has its name, exits and icons; the path draws on a grid.
            Bitmap? image = null;
            var failed = false;
            try { image = await imageTask; }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.IO.IOException)
            {
                Log.Warning(error, "Could not load the minimap for {RawMapId}", rawMapId);
                failed = true;
            }
            return new ZoneView(rawMapId, zone, image, await frameTask, exits, icons.Select(x => new ActivityMapIcon(x.Position, x.Label, iconImages.GetValueOrDefault(x.MarkerType), x.Scale)).ToArray(),
                zone.DisplayName, ZoneDescription(zone), zone.Geometry, failed);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.IO.IOException or JsonException)
        {
            Log.Warning(error, "Could not load map data for {RawMapId}", rawMapId);
            return new ZoneView(rawMapId, null, null, null, [], [], fallbackName, "", null, true);
        }
    }

    // The path is always the character's own; events follow the Personal or Party choice.
    private IReadOnlyList<ActivityMapMarker> Shown(IReadOnlyList<ActivityMapMarker> markers) =>
        SelectedScopeIndex == 1 ? markers : markers.Where(x => x.IsOwn).ToArray();

    private void UpdateHistoryDetails(ActivityMapRunMapOption option)
    {
        var span = Math.Max(1, (option.EndUtc - option.StartUtc).TotalSeconds);
        Ticks = Shown(option.Markers).Select(x => new ActivityMapTick(Math.Clamp((x.CapturedAtUtc - option.StartUtc).TotalSeconds / span, 0, 1), x.Kind)).ToArray();
        if (runMap is not null) RunSummaryText = RunSummary(runMap, RunMaps, Shown(runMap.Markers));
    }

    private static MapZoneKind Zone(string? pvpCategory) => pvpCategory switch
    {
        "blue" => MapZoneKind.Blue,
        "yellow" => MapZoneKind.Yellow,
        "red" => MapZoneKind.Red,
        "black" => MapZoneKind.Black,
        _ => MapZoneKind.Other
    };

    private static string ZoneDescription(MapZone zone)
    {
        var kind = zone.MapCategory switch
        {
            "openworld" => "Open world",
            "city" or "startingcity" or "portalcity" => "City",
            "roads" => "Roads of Avalon",
            "dungeon" => "Dungeon",
            "corrupted" => "Corrupted dungeon",
            "hellden" => "Hellgate",
            "hideout" => "Hideout area",
            "expedition" => "Expedition",
            "arena" => "Arena",
            "island" => "Island",
            "passage" => "Passage",
            _ => ""
        };
        var zoneColor = zone.PvpCategory switch
        {
            "blue" => "Blue zone",
            "yellow" => "Yellow zone",
            "red" => "Red zone",
            "orange" => "Orange zone",
            "black" => "Black zone",
            _ => ""
        };
        return string.Join(" · ", new[] { kind, zone.Tier is > 0 ? $"T{zone.Tier}" : "", zoneColor }.Where(x => x.Length > 0));
    }

    private static Dictionary<string, (string? Name, string? Layout)> VisitDetails(ActivitySession run)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<ActivityVisit>>(run.VisitsJson) ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.RawMapId))
                .GroupBy(x => x.RawMapId).ToDictionary(x => x.Key, x => (
                    x.Select(y => y.LocationName).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                    x.Select(y => y.Metadata?.Layout).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y))));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string RunLabel(ActivitySession run)
    {
        var started = run.StartedAtUtc.ToLocalTime();
        var ended = (run.ClosedAtUtc ?? run.LastObservedAtUtc).ToLocalTime();
        var day = started.Date == DateTime.Today ? "Today" : started.Date == DateTime.Today.AddDays(-1) ? "Yesterday"
            : started.ToString("d", CultureInfo.CurrentCulture);
        var title = string.IsNullOrWhiteSpace(run.Title) ? run.LocationName : run.Title;
        return $"{title} · {day} {started.ToString("t", CultureInfo.CurrentCulture)} – {ended.ToString("t", CultureInfo.CurrentCulture)}";
    }

    private static string RunSummary(ActivityRunMap run, IReadOnlyList<ActivityMapRunMapOption> mapsInRun, IReadOnlyList<ActivityMapMarker> markers)
    {
        var duration = (run.Run.ClosedAtUtc ?? run.Run.LastObservedAtUtc) - run.Run.StartedAtUtc;
        var parts = new List<string>
        {
            UiFormat.Count(mapsInRun.Count, "map"),
            UiFormat.Duration(duration)
        };
        var kills = markers.Count(x => x.Kind == ActivityMapMarkerKind.Kill);
        if (kills > 0) parts.Add(UiFormat.Count(kills, "kill"));
        var loot = markers.Count(x => x.Kind == ActivityMapMarkerKind.Loot);
        if (loot > 0) parts.Add(UiFormat.Count(loot, "pickup"));
        return string.Join(" · ", parts);
    }

    public void Dispose()
    {
        disposed = true;
        liveTimer.Stop();
        replayTimer.Stop();
    }

    private sealed record ZoneView(string RawMapId, MapZone? Zone, Bitmap? Image, Bitmap? Frame, IReadOnlyList<ActivityMapExit> Exits,
        IReadOnlyList<ActivityMapIcon> Icons, string Title, string Subtitle, MapImageGeometry? Geometry, bool Failed);
}

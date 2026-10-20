using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Maps;
using AlbionDataAvalonia.Ui;
using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace AlbionDataAvalonia.Views;

/// <summary>Draws a zone minimap with the character's path, events, exits and icons, and handles zoom, panning and hover details.</summary>
public sealed class ActivityMapCanvas : Control
{
    public static readonly StyledProperty<ActivityMapScene?> SceneProperty =
        AvaloniaProperty.Register<ActivityMapCanvas, ActivityMapScene?>(nameof(Scene));

    public static readonly StyledProperty<bool> FollowProperty =
        AvaloniaProperty.Register<ActivityMapCanvas, bool>(nameof(Follow), defaultBindingMode: BindingMode.TwoWay);

    private static Color MarkerOutline => UiPalette.Color("UiMapMarkerOutlineColor");
    private static ImmutablePen OutlinePen => UiPalette.Pen(MarkerOutline, 1.4);
    private static ImmutablePen WhitePen => UiPalette.Pen(UiPalette.Color("UiMapMarkerEdgeColor"), 1.6, join: PenLineJoin.Round);
    private const double LiveZoom = 3.5;
    private const double MinZoom = 0.5;
    private const double MaxZoom = 40;
    // Consecutive points farther apart than this are a portal, respawn or teleport, not a walk.
    private const double PathBreakDistance = 50;
    private const int FadeBuckets = 8;
    // Enough steps that a run's color change reads as smooth rather than banded.
    private const int GradientBuckets = 48;
    // Markers of one kind closer than this on screen are drawn as one group.
    private const double ClusterRadius = 16;
    private const int GroupLinesShown = 6;
    private const double SpotSpacing = 22;
    // The live position updates a few times a second. The marker, and the view while following, glide to
    // each new one over about as long as the last update took, so walking reads as movement, not hops.
    private static readonly TimeSpan MaxGlide = TimeSpan.FromMilliseconds(400);
    // About 60 frames a second is smooth; a faster screen would otherwise redraw the whole map on every refresh.
    private static readonly TimeSpan GlideFrameInterval = TimeSpan.FromMilliseconds(15);

    private readonly List<Hit> hits = new();
    private ActivityPathPoint? glideFrom;
    private ActivityPathPoint? glideTo;
    private double? glideHeadingFrom;
    private double? glideHeadingTo;
    private long glideStartedAt;
    private TimeSpan glideLength;
    private bool glideFrameRequested;
    private long glideFrameDrawnAt;
    private string? viewKey;
    private bool livePositioned;
    private double zoom = 1;
    private Point center;
    private Point? pointer;
    private Point? dragStart;
    private Point dragCenter;
    private bool dragged;
    private Hit? hovered;

    static ActivityMapCanvas()
    {
        AffectsRender<ActivityMapCanvas>(SceneProperty, FollowProperty);
    }

    public ActivityMapCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
    }

    public ActivityMapScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public bool Follow
    {
        get => GetValue(FollowProperty);
        set => SetValue(FollowProperty, value);
    }

    public void ZoomBy(double factor) => ZoomAround(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    /// <summary>Shows the whole path (or the whole zone when there is none), or recentres on the character while following.</summary>
    public void Fit()
    {
        if (Scene is not { } scene) return;
        var size = ImageSize(scene);
        var points = scene.Path.Where(x => scene.UntilUtc is not { } until || x.CapturedAtUtc <= until)
            .Select(x => ImagePoint(scene, x.X, x.Y)).Concat(scene.Markers.Select(x => ImagePoint(scene, x.X, x.Y))).ToArray();
        if (scene.IsLive || points.Length < 2)
        {
            zoom = scene.IsLive && scene.Position is not null ? LiveZoom : 1;
            center = new Point(size.Width / 2, size.Height / 2);
        }
        else
        {
            var left = points.Min(x => x.X);
            var right = points.Max(x => x.X);
            var top = points.Min(x => x.Y);
            var bottom = points.Max(x => x.Y);
            var fit = FitScale(scene);
            var width = Math.Max(right - left, size.Width * 0.08) * 1.3;
            var height = Math.Max(bottom - top, size.Height * 0.08) * 1.3;
            zoom = Math.Clamp(Math.Min(Bounds.Width / (width * fit), Bounds.Height / (height * fit)), MinZoom, MaxZoom);
            center = new Point((left + right) / 2, (top + bottom) / 2);
        }

        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SceneProperty || Scene is not { } scene) return;
        var newView = scene.ViewKey != viewKey;
        Glide(scene, newView);
        if (newView)
        {
            viewKey = scene.ViewKey;
            livePositioned = scene.Position is not null;
            hovered = null;
            Fit();
        }
        // Entering a zone shows all of it until the first step; then zoom in on the character.
        else if (scene.IsLive && scene.Position is not null && !livePositioned)
        {
            livePositioned = true;
            Fit();
        }
    }

    // Starts a glide from wherever the marker is drawn now to the scene's position. A new map, a first
    // position or a jump too long to be a walk (a portal or respawn) places the marker straight away.
    private void Glide(ActivityMapScene scene, bool newView)
    {
        if (!scene.IsLive || scene.Position is not { } target)
        {
            glideFrom = glideTo = null;
            return;
        }

        // Standing still, the facing can still change; the arrow then turns in place.
        if (!newView && glideTo is { } previous && previous.X == target.X && previous.Y == target.Y && glideHeadingTo == scene.Heading) return;
        var now = Stopwatch.GetTimestamp();
        var shown = newView ? null : Shown(now);
        if (shown is { } from && Math.Sqrt(Math.Pow(target.X - from.Point.X, 2) + Math.Pow(target.Y - from.Point.Y, 2)) <= PathBreakDistance)
        {
            glideFrom = from.Point;
            glideHeadingFrom = from.Heading;
            var sinceLast = Stopwatch.GetElapsedTime(glideStartedAt, now);
            glideLength = sinceLast < MaxGlide ? sinceLast : MaxGlide;
        }
        else glideFrom = null;

        glideTo = target;
        glideHeadingTo = scene.Heading;
        glideStartedAt = now;
        RequestGlideFrame();
    }

    private (ActivityPathPoint Point, double? Heading)? Shown(long now)
    {
        if (glideTo is not { } to) return null;
        if (glideFrom is not { } from || glideLength <= TimeSpan.Zero) return (to, glideHeadingTo);
        var t = Math.Clamp(Stopwatch.GetElapsedTime(glideStartedAt, now) / glideLength, 0, 1);
        var point = to with { X = from.X + (to.X - from.X) * t, Y = from.Y + (to.Y - from.Y) * t };
        // Turn the short way round.
        var heading = glideHeadingFrom is { } a && glideHeadingTo is { } b ? a + Math.IEEERemainder(b - a, 2 * Math.PI) * t : glideHeadingTo;
        return (point, heading);
    }

    private bool Gliding => glideFrom is not null && Stopwatch.GetElapsedTime(glideStartedAt) < glideLength;

    private void RequestGlideFrame()
    {
        if (glideFrameRequested || TopLevel.GetTopLevel(this) is not { } top) return;
        glideFrameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            glideFrameRequested = false;
            var gliding = Gliding;
            // The glide's last frame is always drawn, so the marker comes to rest where it stopped.
            if (!gliding || Stopwatch.GetElapsedTime(glideFrameDrawnAt) >= GlideFrameInterval)
            {
                glideFrameDrawnAt = Stopwatch.GetTimestamp();
                InvalidateVisual();
            }
            if (gliding) RequestGlideFrame();
        });
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // The first layout gives the canvas its size; fit the history path to it once known.
        if (e.PreviousSize.Width <= 0 && Scene is { IsLive: false }) Fit();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(UiPalette.Brush("UiMapBackgroundColor"), bounds);
        hits.Clear();
        if (Scene is not { } scene || bounds.Width <= 0 || bounds.Height <= 0) return;

        var size = ImageSize(scene);
        var shown = scene.IsLive ? Shown(Stopwatch.GetTimestamp()) : null;
        if (Follow && shown is { } position) center = ImagePoint(scene, position.Point.X, position.Point.Y);
        var scale = FitScale(scene) * zoom;

        if (scene.Image is { } image)
        {
            var origin = ToScreen(new Point(0, 0), scale);
            var rect = new Rect(origin, new Size(size.Width * scale, size.Height * scale));
            if (scene.ImageQuarterTurns % 4 == 0) context.DrawImage(image, new Rect(image.Size), rect);
            else
            {
                // A turned Mists layout turns about the centre of its square minimap.
                var centre = rect.Center;
                using (context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y)
                    * Matrix.CreateRotation(scene.ImageQuarterTurns * Math.PI / 2) * Matrix.CreateTranslation(centre.X, centre.Y)))
                    context.DrawImage(image, new Rect(image.Size), rect);
            }
        }
        else DrawZoneOutline(context, scene, scale);
        DrawFrame(context, scene, size, scale);

        if (scene.Layers.HasFlag(ActivityMapLayers.Features))
        {
            DrawIcons(context, scene, scale);
            DrawExits(context, scene, scale);
        }

        if (scene.Layers.HasFlag(ActivityMapLayers.Path)) DrawPath(context, scene, scale);
        DrawMarkers(context, scene, scale);
        DrawPosition(context, scene, scale, shown);
        // Icons are asked for only for the tooltip on view; one that arrives later redraws it.
        if (hovered is { } hover && pointer is { } at)
            ChartDrawing.DrawTooltip(context, bounds, at, hover.Title,
                hover.Items.Select(row => new ChartDrawing.TooltipRow(row.Mob ? LazyMobImage.ForDrawing(row.UniqueName, InvalidateVisual)
                    : LazyItemImage.ForDrawing(row.UniqueName, row.Quality, InvalidateVisual), row.Text, row.Value, row.Mob)).ToArray(),
                hover.Lines);
    }

    // The frame is centred on the minimap, which fills MapFrame.MapScale of it. Only the minimap's empty corners lie
    // under it, and everything else is drawn after it, so nothing on the map moves or hides.
    private void DrawFrame(DrawingContext context, ActivityMapScene scene, Size size, double scale)
    {
        if (scene.Frame is not { } frame) return;
        var width = size.Width / MapFrame.MapScale;
        var height = size.Height / MapFrame.MapScale;
        var origin = ToScreen(new Point((size.Width - width) / 2, (size.Height - height) / 2), scale);
        context.DrawImage(frame, new Rect(frame.Size), new Rect(origin, new Size(width * scale, height * scale)));
    }

    private void DrawZoneOutline(DrawingContext context, ActivityMapScene scene, double scale)
    {
        var geometry = scene.Geometry;
        var corners = new[] { (geometry.Min.X, geometry.Min.Y), (geometry.Max.X, geometry.Min.Y), (geometry.Max.X, geometry.Max.Y), (geometry.Min.X, geometry.Max.Y) }
            .Select(x => ToScreen(ImagePoint(scene, x.Item1, x.Item2), scale)).ToArray();
        var outline = new StreamGeometry();
        using (var path = outline.Open())
        {
            path.BeginFigure(corners[0], true);
            foreach (var corner in corners.Skip(1)) path.LineTo(corner);
            path.EndFigure(true);
        }

        context.DrawGeometry(UiPalette.Brush("UiMapZoneColor"), null, outline);
        // A faint 50-unit grid gives zones without a minimap a sense of distance.
        var pen = UiPalette.Pen(UiPalette.Color("UiMapGridColor"));
        var step = 50;
        for (var x = Math.Ceiling(geometry.Min.X / step) * step; x <= geometry.Max.X; x += step)
            context.DrawLine(pen, ToScreen(ImagePoint(scene, x, geometry.Min.Y), scale), ToScreen(ImagePoint(scene, x, geometry.Max.Y), scale));
        for (var y = Math.Ceiling(geometry.Min.Y / step) * step; y <= geometry.Max.Y; y += step)
            context.DrawLine(pen, ToScreen(ImagePoint(scene, geometry.Min.X, y), scale), ToScreen(ImagePoint(scene, geometry.Max.X, y), scale));
    }

    private void DrawIcons(DrawingContext context, ActivityMapScene scene, double scale)
    {
        // Like the website: 30 px, growing up to a quarter larger when zoomed in.
        var baseSize = 30 * Math.Clamp(1 + 0.25 * (zoom - 1) / 3, 1, 1.25);
        foreach (var icon in scene.Icons)
        {
            if ((icon.Image ?? LazyMobImage.ForDrawing(icon.Mob, InvalidateVisual)) is not { } image) continue;
            var iconSize = baseSize * icon.Scale;
            var at = ToScreen(ImagePoint(scene, icon.Position.X, icon.Position.Y), scale);
            if (!Visible(at, iconSize)) continue;
            var rect = new Rect(at.X - iconSize / 2, at.Y - iconSize / 2, iconSize, iconSize);
            context.DrawImage(image, new Rect(image.Size), rect);
            hits.Add(new Hit(at, iconSize / 2 + 2, icon.Label, [], []));
        }
    }

    private void DrawExits(DrawingContext context, ActivityMapScene scene, double scale)
    {
        foreach (var exit in scene.Exits)
        {
            var at = ToScreen(ImagePoint(scene, exit.Position.X, exit.Position.Y), scale);
            if (!Visible(at, 8)) continue;
            context.DrawEllipse(UiPalette.Brush(ActivityPalette.Zone(exit.Zone)), OutlinePen, at, 6, 6);
            hits.Add(new Hit(at, 9, exit.Label, [], []));
        }
    }

    private void DrawPath(DrawingContext context, ActivityMapScene scene, double scale)
    {
        var points = scene.Path;
        if (points.Count < 2) return;
        var live = scene.IsLive;
        var buckets = live ? FadeBuckets : GradientBuckets;
        var figures = new StreamGeometry?[buckets];
        var contexts = new StreamGeometryContext?[buckets];
        var last = new Point?[buckets];
        var startUtc = scene.StartUtc ?? points[0].CapturedAtUtc;
        var endUtc = scene.UntilUtc ?? points[^1].CapturedAtUtc;
        var span = Math.Max(1, (points[^1].CapturedAtUtc - startUtc).TotalSeconds);
        try
        {
            for (var index = 1; index < points.Count; index++)
            {
                var from = points[index - 1];
                var to = points[index];
                if (to.CapturedAtUtc > endUtc && !live) break;
                if (Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2)) > PathBreakDistance) continue;
                int bucket;
                if (live)
                {
                    var strength = Strength(scene, to.CapturedAtUtc);
                    if (strength <= 0) continue;
                    bucket = Math.Clamp((int)Math.Ceiling(strength * buckets) - 1, 0, buckets - 1);
                }
                else bucket = Math.Clamp((int)((to.CapturedAtUtc - startUtc).TotalSeconds / span * buckets), 0, buckets - 1);

                var a = ToScreen(ImagePoint(scene, from.X, from.Y), scale);
                var b = ToScreen(ImagePoint(scene, to.X, to.Y), scale);
                if (contexts[bucket] is null)
                {
                    figures[bucket] = new StreamGeometry();
                    contexts[bucket] = figures[bucket]!.Open();
                }

                if (last[bucket] != a)
                {
                    if (last[bucket] is not null) contexts[bucket]!.EndFigure(false);
                    contexts[bucket]!.BeginFigure(a, false);
                }

                contexts[bucket]!.LineTo(b);
                last[bucket] = b;
            }
        }
        finally
        {
            for (var bucket = 0; bucket < buckets; bucket++)
            {
                if (contexts[bucket] is not { } open) continue;
                if (last[bucket] is not null) open.EndFigure(false);
                open.Dispose();
            }
        }

        for (var bucket = 0; bucket < buckets; bucket++)
        {
            if (figures[bucket] is not { } figure) continue;
            var fraction = (bucket + 1d) / buckets;
            // History runs the first step at the start color and the last at the end color, as the legend does.
            var color = live ? UiPalette.WithAlpha(ActivityPalette.PathEnd, 40 + 215 * fraction) : ActivityPalette.Path(bucket / (buckets - 1d));
            var width = live ? 2 + 1.6 * fraction : 3;
            context.DrawGeometry(null, UiPalette.Pen(color, width, PenLineCap.Round, PenLineJoin.Round), figure);
        }

        if (!live)
        {
            var visible = points.Where(x => x.CapturedAtUtc <= endUtc).ToArray();
            if (visible.Length > 0)
            {
                var start = ToScreen(ImagePoint(scene, visible[0].X, visible[0].Y), scale);
                context.DrawEllipse(UiPalette.Brush(ActivityPalette.PathStart), WhitePen, start, 5, 5);
                hits.Add(new Hit(start, 7, "Run started here", [When(visible[0].CapturedAtUtc, scene)], []));
            }
        }
    }

    // Events of one kind close together on screen merge into one marker with a count, so a cleared mob pack
    // or a looted chest reads as one spot. Zooming in spreads them apart again.
    private void DrawMarkers(DrawingContext context, ActivityMapScene scene, double scale)
    {
        var clusters = new List<MarkerCluster>();
        var grid = new Dictionary<(ActivityMapMarkerKind Kind, int X, int Y), List<MarkerCluster>>();
        // Newest first: a group sits on its latest event and fades with it.
        foreach (var marker in scene.Markers.OrderByDescending(x => x.CapturedAtUtc))
        {
            var layer = marker.Kind switch
            {
                ActivityMapMarkerKind.Kill => ActivityMapLayers.Kills,
                ActivityMapMarkerKind.Loot => ActivityMapLayers.Loot,
                ActivityMapMarkerKind.Gathering => ActivityMapLayers.Gathering,
                _ => ActivityMapLayers.Deaths
            };
            if (!scene.Layers.HasFlag(layer)) continue;
            if (!scene.IsLive && scene.UntilUtc is { } until && marker.CapturedAtUtc > until) continue;
            var opacity = scene.IsLive ? Math.Max(0, Strength(scene, marker.CapturedAtUtc)) : 1;
            if (opacity <= 0) continue;
            var at = ToScreen(ImagePoint(scene, marker.X, marker.Y), scale);
            if (!Visible(at, ClusterRadius)) continue;
            var cellX = (int)Math.Floor(at.X / ClusterRadius);
            var cellY = (int)Math.Floor(at.Y / ClusterRadius);
            MarkerCluster? cluster = null;
            for (var dx = -1; dx <= 1 && cluster is null; dx++)
                for (var dy = -1; dy <= 1 && cluster is null; dy++)
                    if (grid.TryGetValue((marker.Kind, cellX + dx, cellY + dy), out var nearby))
                        cluster = nearby.FirstOrDefault(x => Distance(x.At, at) <= ClusterRadius);
            if (cluster is null)
            {
                cluster = new MarkerCluster(marker.Kind, at, opacity);
                clusters.Add(cluster);
                var key = (marker.Kind, cellX, cellY);
                if (!grid.TryGetValue(key, out var cell)) grid[key] = cell = new List<MarkerCluster>();
                cell.Add(cluster);
            }

            cluster.Members.Add(marker);
        }

        // Groups of different kinds on the same spot sit side by side instead of hiding each other.
        var spots = new List<List<MarkerCluster>>();
        foreach (var cluster in clusters)
        {
            var spot = spots.FirstOrDefault(x => Distance(x[0].At, cluster.At) <= ClusterRadius);
            if (spot is null) spots.Add([cluster]);
            else spot.Add(cluster);
        }

        // Oldest first, so the newest spots end up on top.
        foreach (var spot in Enumerable.Reverse(spots))
        {
            var row = spot.OrderBy(x => x.Kind).ToArray();
            var center = new Point(row.Average(x => x.At.X), row.Average(x => x.At.Y));
            for (var index = 0; index < row.Length; index++)
            {
                var cluster = row[index];
                var at = row.Length == 1 ? cluster.At : new Point(center.X + (index - (row.Length - 1) / 2d) * SpotSpacing, center.Y);
                var count = cluster.Members.Count;
                using (context.PushOpacity(Math.Max(0.2, cluster.Opacity)))
                {
                    // A party member's event gets a light ring so it reads apart from the character's own.
                    if (cluster.Members.All(x => !x.IsOwn))
                        context.DrawEllipse(null, UiPalette.Pen(UiPalette.Color("UiMapPartyRingColor"), 1.4), at, count > 1 ? 12 : 10.5, count > 1 ? 12 : 10.5);
                    DrawMarkerShape(context, cluster.Kind, at, count > 1 ? 7.5 : 6.5);
                    if (count > 1) ChartDrawing.DrawCountBadge(context, at, count);
                }

                hits.Add(count == 1 ? MarkerHit(at, cluster.Members[0], scene)
                    : new Hit(at, 11, GroupTitle(cluster.Kind, cluster.Members), GroupLines(cluster.Kind, cluster.Members, scene), GroupItems(cluster.Kind, cluster.Members)));
            }
        }
    }

    // A pickup or harvest shows its item like the lists do: icon, name and EMV. A mob kill shows the mob's portrait.
    private static Hit MarkerHit(Point at, ActivityMapMarker marker, ActivityMapScene scene) =>
        new(at, 9, marker.Title, new[] { marker.Detail, When(marker.CapturedAtUtc, scene) }.Where(x => x.Length > 0).ToArray(),
            HasItems(marker.Kind) ? [ItemLine(marker.ItemUniqueName, marker.Quality, marker.Subject, marker.Quantity, marker.Value, marker.Value is null)]
            : IsMobKill(marker) ? [MobLine(marker.MobUniqueName, marker.Subject, 1)] : []);

    private static bool HasItems(ActivityMapMarkerKind kind) => kind is ActivityMapMarkerKind.Loot or ActivityMapMarkerKind.Gathering;

    private static bool IsMobKill(ActivityMapMarker marker) => marker.Kind == ActivityMapMarkerKind.Kill && marker.MobUniqueName.Length > 0;

    private static ItemRow MobLine(string uniqueName, string name, int kills) =>
        new(uniqueName, 1, name, kills > 1 ? $"×{UiFormat.Whole(kills)}" : "", Mob: true);

    // Mobs that share a name (the tiers of one mob) share a row, with the most common one's portrait.
    private static IGrouping<string, ActivityMapMarker>[] MobGroups(IReadOnlyList<ActivityMapMarker> members) =>
        members.Where(IsMobKill).GroupBy(x => x.Subject).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).ToArray();

    private static ItemRow ItemLine(string uniqueName, int quality, string name, long quantity, long? value, bool missing) =>
        new(uniqueName, quality, $"{name}{(quality > 1 ? $" ({ItemQuality.Format(quality)})" : "")} ×{UiFormat.Whole(quantity)}",
            missing && value is not > 0 ? "no price" : UiFormat.Compact(value ?? 0));

    // Pickups and gathering list their items, most valuable first; kills list their mobs, the most common first.
    private static IReadOnlyList<ItemRow> GroupItems(ActivityMapMarkerKind kind, IReadOnlyList<ActivityMapMarker> members) =>
        kind == ActivityMapMarkerKind.Kill ? MobGroups(members).Take(GroupLinesShown).Select(x => MobLine(
            x.GroupBy(m => m.MobUniqueName).OrderByDescending(m => m.Count()).First().Key, x.Key, x.Count())).ToArray()
        : !HasItems(kind) ? []
        : ItemGroups(members).Take(GroupLinesShown).Select(x => ItemLine(x.Key.ItemUniqueName, x.Key.Quality, x.Key.Subject,
            x.Sum(m => m.Quantity), x.Sum(m => m.Value ?? 0), x.Any(m => m.Value is null))).ToArray();

    private static IGrouping<(string Subject, string ItemUniqueName, int Quality), ActivityMapMarker>[] ItemGroups(IReadOnlyList<ActivityMapMarker> members) =>
        members.GroupBy(x => (x.Subject, x.ItemUniqueName, x.Quality))
            .OrderByDescending(x => x.Sum(m => m.Value ?? 0)).ThenByDescending(x => x.Sum(m => m.Quantity)).ThenBy(x => x.Key.Subject).ToArray();

    private static string GroupTitle(ActivityMapMarkerKind kind, IReadOnlyList<ActivityMapMarker> members) => kind switch
    {
        ActivityMapMarkerKind.Kill => UiFormat.Count(members.Count, "kill"),
        ActivityMapMarkerKind.Loot => UiFormat.Count(members.Count, "pickup"),
        ActivityMapMarkerKind.Gathering => $"{UiFormat.Whole(members.Sum(x => x.Quantity))} items gathered",
        _ => UiFormat.Count(members.Count, "death")
    };

    // Kills list the players under their mob rows, the most common first; pickups and gathering add a total under
    // their item rows. The time the group covers comes last.
    private static IReadOnlyList<string> GroupLines(ActivityMapMarkerKind kind, IReadOnlyList<ActivityMapMarker> members, ActivityMapScene scene)
    {
        var lines = new List<string>();
        if (HasItems(kind))
        {
            var items = ItemGroups(members).Length;
            if (items > GroupLinesShown)
                lines.Add($"and {UiFormat.Count(items - GroupLinesShown, "more item")}");
            var missing = members.Where(x => x.Value is null).Sum(x => x.Quantity);
            lines.Add($"Total {Emv(members.Sum(x => x.Value ?? 0))}{(missing > 0 ? $" · {UiFormat.Whole(missing)} without a price" : "")}");
        }
        else
        {
            var mobs = kind == ActivityMapMarkerKind.Kill ? MobGroups(members) : [];
            var shown = Math.Max(0, GroupLinesShown - mobs.Length);
            var groups = members.Where(x => kind != ActivityMapMarkerKind.Kill || !IsMobKill(x))
                .GroupBy(x => kind == ActivityMapMarkerKind.Kill ? x.Subject : x.Title)
                .OrderByDescending(x => x.Count()).ThenBy(x => x.Key).ToArray();
            lines = groups.Take(shown).Select(x => x.Count() > 1 ? $"{x.Key} ×{x.Count()}" : x.Key).ToList();
            var more = mobs.Skip(GroupLinesShown).Sum(x => x.Count()) + groups.Skip(shown).Sum(x => x.Count());
            if (more > 0) lines.Add($"and {UiFormat.Whole(more)} more");
        }

        var party = members.Count(x => !x.IsOwn);
        if (party > 0 && party < members.Count) lines.Add($"{UiFormat.Whole(party)} by your party");
        var first = When(members.Min(x => x.CapturedAtUtc), scene);
        var last = When(members.Max(x => x.CapturedAtUtc), scene);
        lines.Add(first == last ? first : scene.IsLive ? $"{last} to {first}" : $"{first} – {last}");
        return lines;
    }

    private static string Emv(long silver) => $"EMV {UiFormat.Compact(silver)}";

    private static void DrawMarkerShape(DrawingContext context, ActivityMapMarkerKind kind, Point at, double radius)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            switch (kind)
            {
                case ActivityMapMarkerKind.Kill:
                    path.BeginFigure(new Point(at.X, at.Y - radius * 1.2), true);
                    path.LineTo(new Point(at.X + radius * 1.2, at.Y));
                    path.LineTo(new Point(at.X, at.Y + radius * 1.2));
                    path.LineTo(new Point(at.X - radius * 1.2, at.Y));
                    path.EndFigure(true);
                    break;
                case ActivityMapMarkerKind.Gathering:
                    path.BeginFigure(new Point(at.X, at.Y - radius * 1.2), true);
                    path.LineTo(new Point(at.X + radius * 1.1, at.Y + radius * 0.85));
                    path.LineTo(new Point(at.X - radius * 1.1, at.Y + radius * 0.85));
                    path.EndFigure(true);
                    break;
            }
        }

        var fill = UiPalette.Brush(ActivityPalette.MapMarker(kind));
        switch (kind)
        {
            case ActivityMapMarkerKind.Kill:
            case ActivityMapMarkerKind.Gathering:
                context.DrawGeometry(fill, OutlinePen, geometry);
                break;
            case ActivityMapMarkerKind.Loot:
                context.DrawEllipse(fill, OutlinePen, at, radius * 0.9, radius * 0.9);
                break;
            default:
                context.DrawEllipse(UiPalette.Brush("UiMapDeathFillColor"), UiPalette.Pen(ActivityPalette.Death, 1.5), at, radius, radius);
                var pen = UiPalette.Pen(ActivityPalette.Death, 1.8, PenLineCap.Round);
                var arm = radius * 0.5;
                context.DrawLine(pen, new Point(at.X - arm, at.Y - arm), new Point(at.X + arm, at.Y + arm));
                context.DrawLine(pen, new Point(at.X + arm, at.Y - arm), new Point(at.X - arm, at.Y + arm));
                break;
        }
    }

    private void DrawPosition(DrawingContext context, ActivityMapScene scene, double scale, (ActivityPathPoint Point, double? Heading)? glide)
    {
        ActivityPathPoint? current = glide?.Point ?? scene.Position;
        double? heading = glide is { } gliding ? gliding.Heading : scene.Heading;
        if (!scene.IsLive)
        {
            // A history replay shows where the character was at the chosen moment.
            var shown = scene.Path.Where(x => scene.UntilUtc is not { } until || x.CapturedAtUtc <= until).TakeLast(2).ToArray();
            if (shown.Length == 0) return;
            current = shown[^1];
            heading = shown.Length == 2 ? Math.Atan2(shown[1].Y - shown[0].Y, shown[1].X - shown[0].X) : null;
        }

        if (current is not { } point) return;
        var at = ToScreen(ImagePoint(scene, point.X, point.Y), scale);
        var you = ActivityPalette.PathEnd;
        context.DrawEllipse(UiPalette.Brush(UiPalette.WithAlpha(you, 50)), UiPalette.Pen(UiPalette.WithAlpha(you, 140)), at, 16, 16);
        if (heading is not { } angle)
        {
            context.DrawEllipse(UiPalette.Brush(you), WhitePen, at, 6, 6);
            return;
        }

        // Project the local heading through the minimap rotation so the arrow matches the drawn map.
        var ahead = ToScreen(ImagePoint(scene, point.X + Math.Cos(angle), point.Y + Math.Sin(angle)), scale);
        var screenAngle = Math.Atan2(ahead.Y - at.Y, ahead.X - at.X);
        Point Rotate(double x, double y) => new(at.X + x * Math.Cos(screenAngle) - y * Math.Sin(screenAngle),
            at.Y + x * Math.Sin(screenAngle) + y * Math.Cos(screenAngle));
        var arrow = new StreamGeometry();
        using (var path = arrow.Open())
        {
            path.BeginFigure(Rotate(11, 0), true);
            path.LineTo(Rotate(-7, -7));
            path.LineTo(Rotate(-3, 0));
            path.LineTo(Rotate(-7, 7));
            path.EndFigure(true);
        }

        context.DrawGeometry(UiPalette.Brush(you), WhitePen, arrow);
    }

    private static string When(DateTime? at, ActivityMapScene scene)
    {
        if (at is not { } time) return "";
        return scene.IsLive ? UiFormat.Ago(time, scene.NowUtc) : UiFormat.LocalShortTime(time);
    }

    // 1 for fresh items, falling to 0 at the fade time; without fading everything stays fully visible.
    private static double Strength(ActivityMapScene scene, DateTime at) => scene.FadeSeconds <= 0 ? 1
        : Math.Clamp(1 - (scene.NowUtc - at).TotalSeconds / scene.FadeSeconds, 0, 1);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAround(e.GetPosition(this), Math.Pow(1.2, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragStart = e.GetPosition(this);
        dragCenter = center;
        dragged = false;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = e.GetPosition(this);
        pointer = at;
        if (dragStart is { } start && Scene is { } scene)
        {
            var delta = at - start;
            if (!dragged && Math.Abs(delta.X) + Math.Abs(delta.Y) < 4) return;
            dragged = true;
            // Panning by hand stops following; the follow button or Fit brings it back.
            if (Follow) { Follow = false; dragCenter = center; start = at; dragStart = at; delta = default; }
            var scale = FitScale(scene) * zoom;
            center = new Point(dragCenter.X - delta.X / scale, dragCenter.Y - delta.Y / scale);
            InvalidateVisual();
            return;
        }

        var next = hits.Where(x => Distance(x.At, at) <= x.Radius).OrderBy(x => Distance(x.At, at)).FirstOrDefault();
        if (next != hovered)
        {
            hovered = next;
            InvalidateVisual();
        }
        else if (hovered is not null) InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        dragStart = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        pointer = null;
        if (hovered is null) return;
        hovered = null;
        InvalidateVisual();
    }

    private void ZoomAround(Point at, double factor)
    {
        if (Scene is not { } scene) return;
        var scale = FitScale(scene) * zoom;
        var anchor = new Point(center.X + (at.X - Bounds.Width / 2) / scale, center.Y + (at.Y - Bounds.Height / 2) / scale);
        zoom = Math.Clamp(zoom * factor, MinZoom, MaxZoom);
        var next = FitScale(scene) * zoom;
        // Keep the spot under the pointer in place; while following, the character stays centred instead.
        if (!Follow || !scene.IsLive) center = new Point(anchor.X - (at.X - Bounds.Width / 2) / next, anchor.Y - (at.Y - Bounds.Height / 2) / next);
        InvalidateVisual();
    }

    private static Size ImageSize(ActivityMapScene scene) => scene.Image is { } image
        ? image.Size : new Size(scene.Geometry.CanvasWidth * 4, scene.Geometry.CanvasHeight * 4);

    private static Point ImagePoint(ActivityMapScene scene, double x, double y)
    {
        var fraction = scene.Geometry.ToImageFraction(x, y);
        var size = ImageSize(scene);
        return new Point(fraction.X * size.Width, fraction.Y * size.Height);
    }

    // Zoom 1 shows the whole frame when there is one, and the whole minimap otherwise.
    private double FitScale(ActivityMapScene scene)
    {
        var size = ImageSize(scene);
        if (scene.Frame is not null) size = new Size(size.Width / MapFrame.MapScale, size.Height / MapFrame.MapScale);
        return size.Width <= 0 || size.Height <= 0 ? 1 : Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height);
    }

    private Point ToScreen(Point image, double scale) =>
        new((image.X - center.X) * scale + Bounds.Width / 2, (image.Y - center.Y) * scale + Bounds.Height / 2);

    private bool Visible(Point at, double margin) =>
        at.X >= -margin && at.Y >= -margin && at.X <= Bounds.Width + margin && at.Y <= Bounds.Height + margin;

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private sealed record Hit(Point At, double Radius, string Title, IReadOnlyList<string> Lines, IReadOnlyList<ItemRow> Items);

    private sealed record ItemRow(string UniqueName, int Quality, string Text, string Value, bool Mob = false);

    private sealed class MarkerCluster(ActivityMapMarkerKind kind, Point at, double opacity)
    {
        public ActivityMapMarkerKind Kind { get; } = kind;
        public Point At { get; } = at;
        public double Opacity { get; } = opacity;
        public List<ActivityMapMarker> Members { get; } = new();
    }
}

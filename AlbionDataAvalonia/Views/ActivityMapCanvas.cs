using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
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

    internal static readonly Color KillColor = Color.Parse("#EF5350");
    internal static readonly Color LootColor = Color.Parse("#FFCA28");
    internal static readonly Color GatheringColor = Color.Parse("#66BB6A");
    internal static readonly Color DeathColor = Color.Parse("#E0E0E0");
    internal static readonly Color PathColor = Color.Parse("#FFA726");
    internal static readonly Color PathStartColor = Color.Parse("#4FC3F7");
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#1C1E1E"));
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#22FFFFFF"));
    private static readonly IBrush ZoneBrush = new SolidColorBrush(Color.Parse("#14FFFFFF"));
    private static readonly IBrush TooltipBrush = new SolidColorBrush(Color.Parse("#F0252525"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#EFEFEF"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#B5B5B5"));
    private static readonly Pen TooltipPen = new(new SolidColorBrush(Color.Parse("#555555")), 1);
    private static readonly Pen OutlinePen = new(new SolidColorBrush(Color.Parse("#CC111111")), 1.4);
    private static readonly Pen WhitePen = new(Brushes.White, 1.6, lineJoin: PenLineJoin.Round);
    private static readonly Pen PartyRingPen = new(new SolidColorBrush(Color.Parse("#D0FFFFFF")), 1.4);
    private const double LiveZoom = 3.5;
    private const double MinZoom = 0.5;
    private const double MaxZoom = 40;
    // Consecutive points farther apart than this are a portal, respawn or teleport, not a walk.
    private const double PathBreakDistance = 50;
    private const int FadeBuckets = 8;
    private const int GradientBuckets = 12;

    private readonly List<Hit> hits = new();
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
            var fit = FitScale(size);
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
        if (scene.ViewKey != viewKey)
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

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // The first layout gives the canvas its size; fit the history path to it once known.
        if (e.PreviousSize.Width <= 0 && Scene is { IsLive: false }) Fit();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        hits.Clear();
        if (Scene is not { } scene || bounds.Width <= 0 || bounds.Height <= 0) return;

        var size = ImageSize(scene);
        if (Follow && scene.IsLive && scene.Position is { } position) center = ImagePoint(scene, position.X, position.Y);
        var scale = FitScale(size) * zoom;

        if (scene.Image is { } image)
        {
            var origin = ToScreen(new Point(0, 0), scale);
            context.DrawImage(image, new Rect(image.Size), new Rect(origin, new Size(size.Width * scale, size.Height * scale)));
        }
        else DrawZoneOutline(context, scene, scale);

        if (scene.Layers.HasFlag(ActivityMapLayers.Features))
        {
            DrawIcons(context, scene, scale);
            DrawExits(context, scene, scale);
        }

        if (scene.Layers.HasFlag(ActivityMapLayers.Path)) DrawPath(context, scene, scale);
        DrawMarkers(context, scene, scale);
        DrawPosition(context, scene, scale);
        if (hovered is { } hover && pointer is { } at) DrawTooltip(context, hover, at, scene);
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

        context.DrawGeometry(ZoneBrush, null, outline);
        // A faint 50-unit grid gives zones without a minimap a sense of distance.
        var pen = new Pen(GridBrush, 1);
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
            if (icon.Image is not { } image) continue;
            var iconSize = baseSize * icon.Scale;
            var at = ToScreen(ImagePoint(scene, icon.Position.X, icon.Position.Y), scale);
            if (!Visible(at, iconSize)) continue;
            var rect = new Rect(at.X - iconSize / 2, at.Y - iconSize / 2, iconSize, iconSize);
            context.DrawImage(image, new Rect(image.Size), rect);
            hits.Add(new Hit(at, iconSize / 2 + 2, icon.Label, "", null));
        }
    }

    private void DrawExits(DrawingContext context, ActivityMapScene scene, double scale)
    {
        foreach (var exit in scene.Exits)
        {
            var at = ToScreen(ImagePoint(scene, exit.Position.X, exit.Position.Y), scale);
            if (!Visible(at, 8)) continue;
            context.DrawEllipse(exit.Fill, OutlinePen, at, 6, 6);
            hits.Add(new Hit(at, 9, exit.Label, "", null));
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
            var color = live ? Color.FromArgb((byte)(40 + 215 * fraction), PathColor.R, PathColor.G, PathColor.B) : Blend(PathStartColor, PathColor, fraction);
            var width = live ? 2 + 1.6 * fraction : 3;
            context.DrawGeometry(null, new Pen(new SolidColorBrush(color), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), figure);
        }

        if (!live)
        {
            var visible = points.Where(x => x.CapturedAtUtc <= endUtc).ToArray();
            if (visible.Length > 0)
            {
                var start = ToScreen(ImagePoint(scene, visible[0].X, visible[0].Y), scale);
                context.DrawEllipse(new SolidColorBrush(PathStartColor), WhitePen, start, 5, 5);
                hits.Add(new Hit(start, 7, "Run started here", "", visible[0].CapturedAtUtc));
            }
        }
    }

    private void DrawMarkers(DrawingContext context, ActivityMapScene scene, double scale)
    {
        foreach (var marker in scene.Markers)
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
            if (!Visible(at, 10)) continue;
            using (context.PushOpacity(Math.Max(0.2, opacity)))
            {
                // A party member's event gets a light ring so it reads apart from the character's own.
                if (!marker.IsOwn) context.DrawEllipse(null, PartyRingPen, at, 10.5, 10.5);
                DrawMarkerShape(context, marker.Kind, at, 6.5);
            }
            hits.Add(new Hit(at, 9, marker.Title, marker.Detail, marker.CapturedAtUtc));
        }
    }

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

        switch (kind)
        {
            case ActivityMapMarkerKind.Kill:
                context.DrawGeometry(new SolidColorBrush(KillColor), OutlinePen, geometry);
                break;
            case ActivityMapMarkerKind.Gathering:
                context.DrawGeometry(new SolidColorBrush(GatheringColor), OutlinePen, geometry);
                break;
            case ActivityMapMarkerKind.Loot:
                context.DrawEllipse(new SolidColorBrush(LootColor), OutlinePen, at, radius * 0.9, radius * 0.9);
                break;
            default:
                context.DrawEllipse(new SolidColorBrush(Color.Parse("#E0212121")), new Pen(new SolidColorBrush(DeathColor), 1.5), at, radius, radius);
                var pen = new Pen(new SolidColorBrush(DeathColor), 1.8, lineCap: PenLineCap.Round);
                var arm = radius * 0.5;
                context.DrawLine(pen, new Point(at.X - arm, at.Y - arm), new Point(at.X + arm, at.Y + arm));
                context.DrawLine(pen, new Point(at.X + arm, at.Y - arm), new Point(at.X - arm, at.Y + arm));
                break;
        }
    }

    private void DrawPosition(DrawingContext context, ActivityMapScene scene, double scale)
    {
        ActivityPathPoint? current = scene.Position;
        double? heading = scene.Heading;
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
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(50, PathColor.R, PathColor.G, PathColor.B)),
            new Pen(new SolidColorBrush(Color.FromArgb(140, PathColor.R, PathColor.G, PathColor.B)), 1), at, 16, 16);
        if (heading is not { } angle)
        {
            context.DrawEllipse(new SolidColorBrush(PathColor), WhitePen, at, 6, 6);
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

        context.DrawGeometry(new SolidColorBrush(PathColor), WhitePen, arrow);
    }

    private void DrawTooltip(DrawingContext context, Hit hit, Point at, ActivityMapScene scene)
    {
        var lines = new List<(string Text, IBrush Brush, double Size, FontWeight Weight)> { (hit.Title, TextBrush, 13, FontWeight.SemiBold) };
        var detail = string.Join(" · ", new[] { hit.Detail, When(hit.Time, scene) }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (detail.Length > 0) lines.Add((detail, MutedBrush, 12, FontWeight.Normal));
        var texts = lines.Select(x => new FormattedText(x.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, x.Weight), x.Size, x.Brush)).ToArray();
        var width = texts.Max(x => x.Width) + 20;
        var height = texts.Sum(x => x.Height) + 14;
        var left = Math.Clamp(at.X + 14, 4, Math.Max(4, Bounds.Width - width - 4));
        var top = at.Y + 16 + height > Bounds.Height ? at.Y - height - 10 : at.Y + 16;
        context.DrawRectangle(TooltipBrush, TooltipPen, new Rect(left, Math.Max(4, top), width, height), 6, 6);
        var y = Math.Max(4, top) + 7;
        foreach (var text in texts)
        {
            context.DrawText(text, new Point(left + 10, y));
            y += text.Height;
        }
    }

    private static string When(DateTime? at, ActivityMapScene scene)
    {
        if (at is not { } time) return "";
        if (!scene.IsLive) return time.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        var age = scene.NowUtc - time;
        return age < TimeSpan.FromMinutes(1) ? "just now" : age < TimeSpan.FromHours(1) ? $"{(int)age.TotalMinutes} min ago" : time.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
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
            var scale = FitScale(ImageSize(scene)) * zoom;
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
        var scale = FitScale(ImageSize(scene)) * zoom;
        var anchor = new Point(center.X + (at.X - Bounds.Width / 2) / scale, center.Y + (at.Y - Bounds.Height / 2) / scale);
        zoom = Math.Clamp(zoom * factor, MinZoom, MaxZoom);
        var next = FitScale(ImageSize(scene)) * zoom;
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

    private double FitScale(Size size) => size.Width <= 0 || size.Height <= 0 ? 1 : Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height);

    private Point ToScreen(Point image, double scale) =>
        new((image.X - center.X) * scale + Bounds.Width / 2, (image.Y - center.Y) * scale + Bounds.Height / 2);

    private bool Visible(Point at, double margin) =>
        at.X >= -margin && at.Y >= -margin && at.X <= Bounds.Width + margin && at.Y <= Bounds.Height + margin;

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static Color Blend(Color from, Color to, double fraction) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * fraction), (byte)(from.G + (to.G - from.G) * fraction), (byte)(from.B + (to.B - from.B) * fraction));

    private sealed record Hit(Point At, double Radius, string Title, string Detail, DateTime? Time);
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AlbionDataAvalonia.Views;

/// <summary>Bounded drawing of the rolling projection; has no capture or accounting logic.</summary>
internal sealed class ActivityLiveTimelineCanvas : Control
{
    internal static readonly Color DamageColor = Color.Parse("#69B2F0");
    internal static readonly Color HealingColor = Color.Parse("#69D2A0");
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#EFEFEF"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#C5C5C5"));
    private static readonly IBrush ChartBrush = new SolidColorBrush(Color.Parse("#343434"));
    private static readonly IBrush PillBrush = new SolidColorBrush(Color.Parse("#3D3D3D"));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.Parse("#505050")), 1);
    private readonly List<MarkerHit> markerHits = new();
    private readonly List<EncounterHit> encounterHits = new();
    private readonly Dictionary<string, long> introducedMarkers = new();
    private HashSet<string> knownMarkerIds = new();
    private ActivityLiveTimelineSnapshot? snapshot;
    private DateTime endUtc;
    private Point? pointer;
    private string? lastInspection;
    private const double ChartTop = 100;
    private const double ChartBottom = 226;
    private const double EncounterTop = 268;
    private Rect Plot => new(52, ChartTop, Math.Max(1, Bounds.Width - 68), ChartBottom - ChartTop);
    private DateTime StartUtc => endUtc.AddMinutes(-5);

    public event Action? HoverStarted;
    public event Action? HoverEnded;
    public event Action<string?>? InspectionChanged;
    public event Action<ActivityLiveTimelineMarker>? MarkerSelected;
    public event Action<Guid>? EncounterSelected;
    public bool ShowReceived { get; set; }
    public bool IsLive { get; set; } = true;
    public bool HasInspection => pointer != null;

    public ActivityLiveTimelineCanvas()
    {
        Height = 304;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public void SetSnapshot(ActivityLiveTimelineSnapshot? value, DateTime windowEndUtc)
    {
        if (!ReferenceEquals(snapshot, value))
        {
            var ids = value?.Markers.Select(marker => marker.Id).ToHashSet() ?? new HashSet<string>();
            if (snapshot?.SessionId != value?.SessionId || snapshot?.Scope != value?.Scope)
                introducedMarkers.Clear();
            else
            {
                foreach (var id in ids.Where(id => !knownMarkerIds.Contains(id))) introducedMarkers[id] = Stopwatch.GetTimestamp();
                foreach (var id in introducedMarkers.Keys.Where(id => !ids.Contains(id)).ToArray()) introducedMarkers.Remove(id);
            }
            knownMarkerIds = ids;
        }
        snapshot = value;
        endUtc = windowEndUtc;
        InvalidateVisual();
    }

    public void ClearInspection()
    {
        pointer = null;
        lastInspection = null;
        InspectionChanged?.Invoke(null);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 120) return;
        var plot = Plot;
        markerHits.Clear();
        encounterHits.Clear();
        context.DrawRectangle(ChartBrush, null, plot, 4, 4);
        var samples = snapshot?.Samples ?? Array.Empty<ActivityLiveTimelineSample>();
        var max = NiceMaximum(samples.Where(sample => sample.CapturedAtUtc >= StartUtc && sample.CapturedAtUtc <= endUtc)
            .Select(sample => Math.Max(Damage(sample), Healing(sample))).DefaultIfEmpty(0).Max());
        for (var line = 0; line <= 3; line++)
        {
            var y = plot.Bottom - plot.Height * line / 3;
            context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            Text(context, Compact(max * line / 3), new Point(plot.Left - 7, y - 7), 11, MutedBrush, rightAligned: true);
        }
        Text(context, "/ sec", new Point(3, ChartTop - 19), 10, MutedBrush);
        using (context.PushClip(plot))
        {
            DrawArea(context, samples, max, Damage, DamageColor);
            DrawArea(context, samples, max, Healing, HealingColor);
            DrawGaps(context);
        }
        DrawAxis(context);
        DrawEncounters(context);
        DrawMarkers(context);
        if (snapshot?.SessionId == null || (samples.Count == 0 && snapshot.Markers.Count == 0))
            Text(context, "Your combat and important events will appear here", new Point(plot.Center.X, plot.Center.Y - 8), 12, MutedBrush, centered: true);
        if (pointer is { } position && position.X >= plot.Left && position.X <= plot.Right)
        {
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#969696")), 1), new Point(position.X, ChartTop), new Point(position.X, ChartBottom));
        }
    }

    private void DrawArea(DrawingContext context, IReadOnlyList<ActivityLiveTimelineSample> samples, double maximum,
        Func<ActivityLiveTimelineSample, double> value, Color color)
    {
        var points = new List<Point>(samples.Count);
        void DrawSegment()
        {
            if (points.Count == 0) return;
            var area = new StreamGeometry();
            using (var path = area.Open())
            {
                path.BeginFigure(new Point(points[0].X, ChartBottom), true);
                foreach (var point in points) path.LineTo(point);
                path.LineTo(new Point(points[^1].X, ChartBottom));
                path.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(65, color.R, color.G, color.B)), null, area);
            var line = new StreamGeometry();
            using (var path = line.Open())
            {
                path.BeginFigure(points[0], false);
                foreach (var point in points.Skip(1)) path.LineTo(point);
                path.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.5), line);
            points.Clear();
        }
        foreach (var sample in samples)
        {
            if (sample.CapturedAtUtc < StartUtc.AddSeconds(-1) || sample.CapturedAtUtc > endUtc) continue;
            if (snapshot!.Gaps.Any(gap => sample.CapturedAtUtc >= gap.StartedAtUtc && sample.CapturedAtUtc < gap.EndedAtUtc))
            {
                DrawSegment();
                continue;
            }
            var y = ChartBottom - Math.Clamp(value(sample) / maximum, 0, 1) * Plot.Height;
            points.Add(new Point(X(sample.CapturedAtUtc), y));
            // Each sample describes one second. Complete that bucket without extending it through an unknown gap.
            var bucketEnd = sample.CapturedAtUtc.AddSeconds(1);
            if (bucketEnd > endUtc) bucketEnd = endUtc;
            points.Add(new Point(X(bucketEnd), y));
        }
        DrawSegment();
    }

    private void DrawGaps(DrawingContext context)
    {
        if (snapshot == null) return;
        foreach (var gap in snapshot.Gaps)
        {
            var left = Math.Max(Plot.Left, X(gap.StartedAtUtc));
            var right = Math.Min(Plot.Right, X(gap.EndedAtUtc));
            if (right <= left) continue;
            var rect = new Rect(left, ChartTop, right - left, Plot.Height);
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#494949")), null, rect);
            using (context.PushClip(rect))
                for (var x = left - Plot.Height; x < right; x += 10)
                    context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#626262")), 1), new Point(x, ChartBottom), new Point(x + Plot.Height, ChartTop));
            var label = gap.Reason == "Before session" ? "Before session" : "Recording gap / outside";
            if (right - left > MakeText(label, 11, TextBrush).Width + 12)
                Text(context, label, new Point(rect.Center.X, rect.Center.Y - 6), 11, TextBrush, centered: true);
        }
    }

    private void DrawAxis(DrawingContext context)
    {
        var divisions = Bounds.Width < 620 ? 3 : 5;
        for (var step = 0; step <= divisions; step++)
        {
            var x = Plot.Left + Plot.Width * step / divisions;
            var time = StartUtc.AddSeconds(300d * step / divisions);
            var label = step == divisions && IsLive ? "Now" : time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            context.DrawLine(GridPen, new Point(x, ChartBottom), new Point(x, ChartBottom + 4));
            Text(context, label, new Point(x, ChartBottom + 10), 11, MutedBrush, centered: step != divisions, rightAligned: step == divisions);
        }
    }

    private void DrawEncounters(DrawingContext context)
    {
        if (snapshot == null) return;
        foreach (var encounter in snapshot.Encounters)
        {
            var left = Math.Max(Plot.Left, X(encounter.StartedAtUtc));
            var combatEnd = encounter.LastCombatAtUtc > encounter.StartedAtUtc.AddSeconds(1)
                ? encounter.LastCombatAtUtc : encounter.StartedAtUtc.AddSeconds(1);
            var right = Math.Min(Plot.Right, X(combatEnd));
            if (right < Plot.Left || left > Plot.Right || right <= left) continue;
            var rect = new Rect(left, EncounterTop, Math.Max(3, right - left), 23);
            var color = encounter.Kind switch
            {
                ActivityEncounterKind.Pvp => Color.Parse("#CE8585"),
                ActivityEncounterKind.Mixed => Color.Parse("#B499CB"),
                ActivityEncounterKind.Healing => HealingColor,
                _ => DamageColor
            };
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, color.R, color.G, color.B)), new Pen(new SolidColorBrush(color), encounter.State == ActivityState.Open ? 1.5 : 0.7), rect, 3, 3);
            encounterHits.Add(new EncounterHit(rect, encounter));
            using (context.PushClip(rect.Deflate(3)))
            {
                var label = "#" + encounter.Number + " " + EncounterKind(encounter.Kind);
                if (rect.Width >= 64) Text(context, label, new Point(rect.Center.X, rect.Top + 4), 11, TextBrush, centered: true);
                else if (rect.Width >= 22) Text(context, "#" + encounter.Number, new Point(rect.Center.X, rect.Top + 4), 10, TextBrush, centered: true);
            }
        }
        if (encounterHits.Count == 0) Text(context, "Encounters", new Point(Plot.Left, EncounterTop + 5), 11, MutedBrush);
    }

    private void DrawMarkers(DrawingContext context)
    {
        if (snapshot == null) return;
        var occupied = new List<Rect>();
        var visible = snapshot.Markers.Where(marker => marker.CapturedAtUtc >= StartUtc && marker.CapturedAtUtc <= endUtc)
            .OrderByDescending(marker => Priority(marker.Kind)).ThenByDescending(marker => marker.CapturedAtUtc).ToArray();
        // Leave a small selectable overflow pill when an unusually dense burst needs more room.
        var overflowBounds = new Rect(Math.Max(Plot.Left, Plot.Right - 121), 7, Math.Min(121, Plot.Width), 23);
        occupied.Add(overflowBounds);
        foreach (var marker in visible.Take(96))
        {
            var x = X(marker.CapturedAtUtc);
            var label = Ellipsize(marker.Label, 19);
            var labelWidth = MakeText(label, 11, TextBrush).Width;
            var width = Math.Min(144, labelWidth + 31);
            var placed = false;
            for (var compact = 0; compact < 2 && !placed; compact++)
            {
                var markerWidth = compact == 0 ? width : 23;
                for (var lane = 0; lane < 3; lane++)
                {
                    var left = Math.Clamp(x - markerWidth / 2, Plot.Left, Math.Max(Plot.Left, Plot.Right - markerWidth));
                    var rect = new Rect(left, 7 + lane * 29, markerWidth, 23);
                    if (occupied.Any(other => other.Inflate(3).Intersects(rect))) continue;
                    occupied.Add(rect);
                    var color = MarkerColor(marker.Kind);
                    context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(65, color.R, color.G, color.B)), 1), new Point(x, rect.Bottom), new Point(x, ChartTop));
                    var highlight = introducedMarkers.TryGetValue(marker.Id, out var introduced)
                        ? Math.Max(0, 1 - Stopwatch.GetElapsedTime(introduced).TotalSeconds / 1.2) : 0;
                    var fill = highlight > 0 ? new SolidColorBrush(Color.FromArgb((byte)(45 + highlight * 65), color.R, color.G, color.B)) : PillBrush;
                    context.DrawRectangle(fill, new Pen(new SolidColorBrush(color), 1 + highlight), rect, 4, 4);
                    DrawIcon(context, marker.Kind, new Point(rect.Left + 12, rect.Center.Y), color);
                    if (compact == 0) Text(context, label, new Point(rect.Left + 24, rect.Top + 4), 11, TextBrush);
                    context.DrawEllipse(new SolidColorBrush(color), null, new Point(x, ChartTop), 2, 2);
                    markerHits.Add(new MarkerHit(rect, marker));
                    placed = true;
                    break;
                }
            }
        }
        var drawn = markerHits.Select(hit => hit.Marker.Id).ToHashSet();
        var hidden = visible.Where(marker => !drawn.Contains(marker.Id)).ToArray();
        if (hidden.Length == 0) return;
        var overflow = new ActivityLiveTimelineMarker("timeline-overflow", ActivityLiveTimelineMarkerKind.Loot, endUtc,
            $"+{hidden.Length:N0} more events",
            string.Join("\n\n", hidden.Take(30).Select(marker => marker.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss") + " · " + marker.Label + "\n" + marker.Detail)) +
                (hidden.Length > 30 ? "\n\nMore events are available in the session timeline." : ""),
            hidden.SelectMany(marker => marker.EventIds).Distinct().ToArray(), null);
        context.DrawRectangle(PillBrush, GridPen, overflowBounds, 4, 4);
        Text(context, overflow.Label, new Point(overflowBounds.Center.X, overflowBounds.Top + 4), 11, TextBrush, centered: true);
        markerHits.Add(new MarkerHit(overflowBounds, overflow));
    }

    private void Inspect(Point point)
    {
        string text;
        var marker = markerHits.LastOrDefault(hit => hit.Bounds.Contains(point));
        var encounter = encounterHits.LastOrDefault(hit => hit.Bounds.Contains(point));
        var time = StartUtc.AddSeconds(Math.Clamp((point.X - Plot.Left) / Plot.Width, 0, 1) * 300);
        var gap = snapshot?.Gaps.FirstOrDefault(item => time >= item.StartedAtUtc && time < item.EndedAtUtc);
        if (marker != null) text = marker.Marker.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss") + " · " + marker.Marker.Label + " · " +
            (marker.Marker.Id == "timeline-overflow" ? "Select to inspect the nearby events" : Ellipsize(marker.Marker.Detail.Replace('\n', ' '), 280));
        else if (encounter != null) text = "Encounter " + encounter.Encounter.Number + " · " + EncounterKind(encounter.Encounter.Kind) + " · " +
            Math.Max(1, (encounter.Encounter.LastCombatAtUtc - encounter.Encounter.StartedAtUtc).TotalSeconds).ToString("N0") + " seconds of combat · Select to view details";
        else if (gap != null) text = time.ToLocalTime().ToString("HH:mm:ss") + " · " + gap.Reason;
        else
        {
            var sample = snapshot?.Samples.MinBy(item => Math.Abs((item.CapturedAtUtc - time).TotalSeconds));
            var damage = sample != null && Math.Abs((sample.CapturedAtUtc - time).TotalSeconds) <= 1.5 ? Damage(sample) : 0;
            var healing = sample != null && Math.Abs((sample.CapturedAtUtc - time).TotalSeconds) <= 1.5 ? Healing(sample) : 0;
            text = time.ToLocalTime().ToString("HH:mm:ss") + " · Damage " + damage.ToString("N0") + " / sec · Healing " + healing.ToString("N0") + " / sec";
        }
        Cursor = new Cursor(marker != null || encounter != null ? StandardCursorType.Hand : StandardCursorType.Cross);
        if (text == lastInspection) return;
        lastInspection = text;
        InspectionChanged?.Invoke(text);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (pointer == null) HoverStarted?.Invoke();
        pointer = e.GetPosition(this);
        Inspect(pointer.Value);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearInspection();
        HoverEnded?.Invoke();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var position = e.GetPosition(this);
        var marker = markerHits.LastOrDefault(hit => hit.Bounds.Contains(position));
        var encounter = encounterHits.LastOrDefault(hit => hit.Bounds.Contains(position));
        if (marker != null) { MarkerSelected?.Invoke(marker.Marker); e.Handled = true; }
        else if (encounter != null) { EncounterSelected?.Invoke(encounter.Encounter.Id); e.Handled = true; }
    }

    private double X(DateTime time) => Plot.Left + (time - StartUtc).TotalSeconds / 300 * Plot.Width;
    private double Damage(ActivityLiveTimelineSample sample) => ShowReceived ? sample.DamageReceived : sample.DamageDealt;
    private double Healing(ActivityLiveTimelineSample sample) => ShowReceived ? sample.HealingReceived : sample.HealingDone;
    private static string EncounterKind(ActivityEncounterKind kind) => kind switch { ActivityEncounterKind.Pve => "PvE", ActivityEncounterKind.Pvp => "PvP", _ => kind.ToString() };
    private static int Priority(ActivityLiveTimelineMarkerKind kind) => kind switch
    {
        ActivityLiveTimelineMarkerKind.Death => 7,
        ActivityLiveTimelineMarkerKind.PlayerKill => 6,
        ActivityLiveTimelineMarkerKind.Knockdown => 5,
        ActivityLiveTimelineMarkerKind.Loot => 4,
        ActivityLiveTimelineMarkerKind.MobKill => 3,
        ActivityLiveTimelineMarkerKind.Fame => 2,
        _ => 1
    };
    private static Color MarkerColor(ActivityLiveTimelineMarkerKind kind) => kind switch
    {
        ActivityLiveTimelineMarkerKind.Death or ActivityLiveTimelineMarkerKind.Knockdown => Color.Parse("#F47D7D"),
        ActivityLiveTimelineMarkerKind.PlayerKill => Color.Parse("#EB9C64"),
        ActivityLiveTimelineMarkerKind.Loot => Color.Parse("#BF91F0"),
        ActivityLiveTimelineMarkerKind.Fame => Color.Parse("#86D1A2"),
        ActivityLiveTimelineMarkerKind.Silver => Color.Parse("#E7BD64"),
        _ => Color.Parse("#64CBDD")
    };
    private static void DrawIcon(DrawingContext context, ActivityLiveTimelineMarkerKind kind, Point center, Color color)
    {
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, 1.5);
        switch (kind)
        {
            case ActivityLiveTimelineMarkerKind.Death:
                context.DrawLine(pen, center + new Vector(-4, -4), center + new Vector(4, 4));
                context.DrawLine(pen, center + new Vector(-4, 4), center + new Vector(4, -4));
                context.DrawEllipse(null, pen, center, 6, 6);
                break;
            case ActivityLiveTimelineMarkerKind.Knockdown:
                context.DrawLine(pen, center + new Vector(-4, -2), center + new Vector(0, 3));
                context.DrawLine(pen, center + new Vector(0, 3), center + new Vector(4, -2));
                context.DrawLine(pen, center + new Vector(-4, 5), center + new Vector(4, 5));
                break;
            case ActivityLiveTimelineMarkerKind.Loot:
                context.DrawRectangle(null, pen, new Rect(center.X - 5, center.Y - 3, 10, 8), 1, 1);
                context.DrawLine(pen, center + new Vector(-3, -3), center + new Vector(-2, -6));
                context.DrawLine(pen, center + new Vector(-2, -6), center + new Vector(2, -6));
                context.DrawLine(pen, center + new Vector(2, -6), center + new Vector(3, -3));
                break;
            case ActivityLiveTimelineMarkerKind.Silver:
                context.DrawEllipse(null, pen, center, 5, 6);
                context.DrawLine(pen, center + new Vector(0, -3), center + new Vector(0, 3));
                break;
            case ActivityLiveTimelineMarkerKind.Fame:
            case ActivityLiveTimelineMarkerKind.PlayerKill:
                context.DrawLine(pen, center + new Vector(-5, 0), center + new Vector(5, 0));
                context.DrawLine(pen, center + new Vector(0, -6), center + new Vector(0, 6));
                context.DrawLine(pen, center + new Vector(-3, -3), center + new Vector(3, 3));
                context.DrawLine(pen, center + new Vector(-3, 3), center + new Vector(3, -3));
                break;
            default:
                context.DrawLine(pen, center + new Vector(-5, -5), center + new Vector(5, 5));
                context.DrawLine(pen, center + new Vector(-5, 5), center + new Vector(5, -5));
                context.DrawLine(pen, center + new Vector(-5, 2), center + new Vector(-2, 5));
                context.DrawLine(pen, center + new Vector(2, 5), center + new Vector(5, 2));
                break;
        }
    }
    private static double NiceMaximum(double maximum)
    {
        if (!double.IsFinite(maximum) || maximum <= 1) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(maximum)));
        var ratio = maximum / magnitude;
        return (ratio <= 2 ? 2 : ratio <= 5 ? 5 : 10) * magnitude;
    }
    private static string Compact(double number) => number >= 1000000 ? (number / 1000000).ToString("0.#") + "m" : number >= 1000 ? (number / 1000).ToString("0.#") + "k" : number.ToString("0.#");
    private static string Ellipsize(string text, int maximum) => text.Length > maximum ? text[..(maximum - 1)] + "…" : text;
    private static FormattedText MakeText(string value, double size, IBrush brush) => new(value, CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, Typeface.Default, size, brush);
    private static void Text(DrawingContext context, string value, Point position, double size, IBrush brush, bool centered = false, bool rightAligned = false)
    {
        var text = MakeText(value, size, brush);
        context.DrawText(text, new Point(position.X - (centered ? text.Width / 2 : rightAligned ? text.Width : 0), position.Y));
    }
    private sealed record MarkerHit(Rect Bounds, ActivityLiveTimelineMarker Marker);
    private sealed record EncounterHit(Rect Bounds, ActivityLiveTimelineEncounter Encounter);
}

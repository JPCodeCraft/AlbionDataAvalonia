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
    internal static readonly Color GatheringColor = Color.Parse("#A0D477");
    internal static readonly Color FishingColor = Color.Parse("#64CBDD");
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
    private DateTime endUtc = DateTime.UtcNow;
    private Point? pointer;
    private string? lastInspection;
    private const double ChartTop = 100;
    private const double ChartBottom = 226;
    private const double EncounterTop = 268;
    private Rect Plot => new(52, ChartTop, Math.Max(1, Bounds.Width - 68), ChartBottom - ChartTop);
    private double WindowSeconds => snapshot is { } value && value.WindowEndUtc > value.WindowStartUtc
        ? (value.WindowEndUtc - value.WindowStartUtc).TotalSeconds : ActivityLiveTimeline.WindowSeconds;
    private double SampleSeconds => Math.Max(1, snapshot?.SampleSeconds ?? 1);
    private DateTime StartUtc => new(Math.Max(DateTime.MinValue.Ticks,
        endUtc.Ticks - (long)(WindowSeconds * TimeSpan.TicksPerSecond)), DateTimeKind.Utc);
    private DateTime BucketEndUtc(ActivityLiveTimelineSample sample) => new(sample.CapturedAtUtc.Ticks +
        Math.Min(DateTime.MaxValue.Ticks - sample.CapturedAtUtc.Ticks, (long)(SampleSeconds * TimeSpan.TicksPerSecond)), DateTimeKind.Utc);

    public event Action? HoverStarted;
    public event Action? HoverEnded;
    public event Action<ActivityLiveTimelineTooltip?, Point>? InspectionChanged;
    public event Action<Guid>? EncounterSelected;
    public bool ShowReceived { get; set; }
    public bool IsGatheringMode { get; set; }
    public bool IsLive { get; set; } = true;
    public bool IsReplay { get; set; }

    public ActivityLiveTimelineCanvas()
    {
        Height = 304;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        DetachedFromVisualTree += (_, _) => ClearInspection();
    }

    public void SetSnapshot(ActivityLiveTimelineSnapshot? value, DateTime windowEndUtc)
    {
        if (!ReferenceEquals(snapshot, value))
        {
            var ids = value?.Markers.Select(marker => marker.Id).ToHashSet() ?? new HashSet<string>();
            if (snapshot?.ContextKey != value?.ContextKey || snapshot?.HasContext != value?.HasContext ||
                snapshot?.SessionId != value?.SessionId || snapshot?.Scope != value?.Scope || snapshot?.Categories != value?.Categories)
            {
                introducedMarkers.Clear();
                ClearInspection();
            }
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
        InspectionChanged?.Invoke(null, default);
        Cursor = new Cursor(StandardCursorType.Cross);
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
        var max = NiceMaximum(samples.Where(sample => BucketEndUtc(sample) > StartUtc && sample.CapturedAtUtc < endUtc)
            .Select(sample => Math.Max(Damage(sample), Healing(sample))).DefaultIfEmpty(0).Max());
        for (var line = 0; line <= 3; line++)
        {
            var y = plot.Bottom - plot.Height * line / 3;
            context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            Text(context, Compact(max * line / 3), new Point(plot.Left - 7, y - 7), 11, MutedBrush, rightAligned: true);
        }
        Text(context, IsGatheringMode ? "items/s" : "HP/s", new Point(3, ChartTop - 19), 10, MutedBrush);
        using (context.PushClip(plot))
        {
            DrawArea(context, samples, max, Damage, IsGatheringMode ? GatheringColor : DamageColor);
            DrawArea(context, samples, max, Healing, IsGatheringMode ? FishingColor : HealingColor);
            DrawGaps(context);
        }
        DrawAxis(context);
        DrawEncounters(context);
        DrawMarkers(context);
        if (snapshot?.HasContext != true || (samples.Count == 0 && snapshot.Markers.Count == 0))
            Text(context, "No activity in this period", new Point(plot.Center.X, plot.Center.Y - 8), 12, MutedBrush, centered: true);
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
        var gaps = snapshot?.Gaps.OrderBy(gap => gap.StartedAtUtc).ToArray() ?? [];
        foreach (var sample in samples)
        {
            var bucketEnd = BucketEndUtc(sample);
            if (bucketEnd <= StartUtc || sample.CapturedAtUtc >= endUtc) continue;
            if (bucketEnd > endUtc) bucketEnd = endUtc;
            var segmentStart = sample.CapturedAtUtc < StartUtc ? StartUtc : sample.CapturedAtUtc;
            var y = ChartBottom - Math.Clamp(value(sample) / maximum, 0, 1) * Plot.Height;
            foreach (var gap in gaps.Where(gap => gap.EndedAtUtc > segmentStart && gap.StartedAtUtc < bucketEnd))
            {
                if (gap.StartedAtUtc > segmentStart)
                {
                    points.Add(new Point(X(segmentStart), y));
                    points.Add(new Point(X(gap.StartedAtUtc), y));
                }
                DrawSegment();
                if (gap.EndedAtUtc > segmentStart) segmentStart = gap.EndedAtUtc;
                if (segmentStart >= bucketEnd) break;
            }
            if (segmentStart < bucketEnd)
            {
                points.Add(new Point(X(segmentStart), y));
                points.Add(new Point(X(bucketEnd), y));
            }
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
            var label = GapLabel(gap.Reason);
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
            var time = StartUtc.AddSeconds(WindowSeconds * step / divisions);
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
                var label = ("#" + encounter.Number + " " + EncounterKind(encounter.Kind)).TrimEnd();
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
        var crowded = new List<ActivityLiveTimelineMarker>();
        var visible = snapshot.Markers.Where(marker => marker.CapturedAtUtc >= StartUtc && marker.CapturedAtUtc <= endUtc)
            .OrderByDescending(marker => Priority(marker.Kind)).ThenByDescending(marker => marker.CapturedAtUtc).ToArray();
        foreach (var marker in visible)
        {
            var drawn = false;
            // Keep the third lane available for local overflow, or a single remaining marker.
            for (var lane = 0; lane < 2; lane++)
            {
                var rect = MarkerBounds(marker.CapturedAtUtc, MakeText(marker.ValueText, 11, TextBrush).Width + 31, lane);
                if (occupied.Any(other => other.Inflate(3).Intersects(rect))) continue;
                occupied.Add(rect);
                DrawMarker(context, marker, rect);
                drawn = true;
                break;
            }
            if (!drawn) crowded.Add(marker);
        }

        var groups = new List<List<ActivityLiveTimelineMarker>>();
        foreach (var marker in crowded.OrderBy(marker => marker.CapturedAtUtc).ThenBy(marker => marker.Id))
        {
            var bounds = MarkerBounds(marker.CapturedAtUtc, MakeText(marker.ValueText, 11, TextBrush).Width + 31, 2);
            if (groups.Count > 0 && CrowdedBounds(groups[^1]).Inflate(3).Intersects(bounds))
                groups[^1].Add(marker);
            else
                groups.Add([marker]);
            // A growing count can widen a badge; merge only adjacent colliding groups.
            while (groups.Count > 1 && CrowdedBounds(groups[^2]).Inflate(3).Intersects(CrowdedBounds(groups[^1])))
            {
                groups[^2].AddRange(groups[^1]);
                groups.RemoveAt(groups.Count - 1);
            }
        }
        foreach (var group in groups)
        {
            var rect = CrowdedBounds(group);
            if (group.Count == 1)
            {
                DrawMarker(context, group[0], rect);
                continue;
            }
            var time = group[0].CapturedAtUtc;
            var lines = group.Take(4).Select(marker => marker.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) +
                " · " + marker.Label + " · " + marker.ValueText).ToList();
            if (group.Count > 4) lines.Add($"+{group.Count - 4:N0} more events");
            var tooltip = new ActivityLiveTimelineTooltip("More events", "", $"{group.Count:N0} events", lines);
            var overflow = new ActivityLiveTimelineMarker("timeline-overflow:" + group[0].Id, ActivityLiveTimelineMarkerKind.Overflow, time,
                "More events", string.Join("\n", lines), group.SelectMany(marker => marker.EventIds).Distinct().ToArray(), null,
                group.Count, ValueText: $"+{group.Count:N0}", Tooltip: tooltip);
            var x = X(time);
            context.DrawLine(GridPen, new Point(x, rect.Bottom), new Point(x, ChartTop));
            context.DrawEllipse(MutedBrush, null, new Point(x, ChartTop), 2, 2);
            context.DrawRectangle(PillBrush, GridPen, rect, 4, 4);
            Text(context, overflow.ValueText, new Point(rect.Center.X, rect.Top + 4), 11, TextBrush, centered: true);
            markerHits.Add(new MarkerHit(rect, overflow));
        }
    }

    private Rect MarkerBounds(DateTime time, double width, int lane)
    {
        width = Math.Min(Plot.Width, width);
        var left = Math.Clamp(X(time) - width / 2, Plot.Left, Math.Max(Plot.Left, Plot.Right - width));
        return new Rect(left, 7 + lane * 29, width, 23);
    }

    private Rect CrowdedBounds(IReadOnlyList<ActivityLiveTimelineMarker> group) => MarkerBounds(group[0].CapturedAtUtc,
        group.Count == 1 ? MakeText(group[0].ValueText, 11, TextBrush).Width + 31 :
            Math.Max(36, MakeText($"+{group.Count:N0}", 11, TextBrush).Width + 16), 2);

    private void DrawMarker(DrawingContext context, ActivityLiveTimelineMarker marker, Rect rect)
    {
        var x = X(marker.CapturedAtUtc);
        var color = MarkerColor(marker.Kind);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(65, color.R, color.G, color.B)), 1), new Point(x, rect.Bottom), new Point(x, ChartTop));
        var highlight = introducedMarkers.TryGetValue(marker.Id, out var introduced)
            ? Math.Max(0, 1 - Stopwatch.GetElapsedTime(introduced).TotalSeconds / 1.2) : 0;
        var fill = highlight > 0 ? new SolidColorBrush(Color.FromArgb((byte)(45 + highlight * 65), color.R, color.G, color.B)) : PillBrush;
        context.DrawRectangle(fill, new Pen(new SolidColorBrush(color), 1 + highlight), rect, 4, 4);
        DrawIcon(context, marker.Kind, new Point(rect.Left + 12, rect.Center.Y), color);
        Text(context, marker.ValueText, new Point(rect.Left + 24, rect.Top + 4), 11, TextBrush);
        context.DrawEllipse(new SolidColorBrush(color), null, new Point(x, ChartTop), 2, 2);
        markerHits.Add(new MarkerHit(rect, marker));
    }

    private void Inspect(Point point)
    {
        ActivityLiveTimelineTooltip tooltip;
        var marker = markerHits.LastOrDefault(hit => hit.Bounds.Contains(point));
        var encounter = encounterHits.LastOrDefault(hit => hit.Bounds.Contains(point));
        var time = StartUtc.AddSeconds(Math.Clamp((point.X - Plot.Left) / Plot.Width, 0, 1) * WindowSeconds);
        var gap = snapshot?.Gaps.FirstOrDefault(item => time >= item.StartedAtUtc && time < item.EndedAtUtc);
        var timeText = time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        if (marker != null) tooltip = marker.Marker.Tooltip ?? new(marker.Marker.Label,
            marker.Marker.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture), "", [marker.Marker.Detail]);
        else if (encounter != null) tooltip = new("Encounter " + encounter.Encounter.Number +
            (encounter.Encounter.Kind == ActivityEncounterKind.Unknown ? "" : " · " + EncounterKind(encounter.Encounter.Kind)),
            encounter.Encounter.StartedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture), "",
            [$"{Math.Max(1, (encounter.Encounter.LastCombatAtUtc - encounter.Encounter.StartedAtUtc).TotalSeconds):N0} " +
                (IsReplay && encounter.Encounter.State == ActivityState.Open ? "seconds elapsed so far" : "seconds of combat"), "Click to view details"]);
        else if (gap != null && Plot.Contains(point)) tooltip = new(GapLabel(gap.Reason), timeText, "", []);
        else
        {
            if (!Plot.Contains(point) || snapshot?.HasContext != true)
            {
                HideTooltip();
                return;
            }
            var sample = snapshot?.Samples.LastOrDefault(item => item.CapturedAtUtc <= time &&
                time < BucketEndUtc(item));
            var damage = sample != null ? Damage(sample) : 0;
            var healing = sample != null ? Healing(sample) : 0;
            tooltip = IsGatheringMode ? new("Gathering rate", timeText, "", [$"Gathering {damage:N0} items/s", $"Fishing {healing:N0} items/s"]) :
                new(ShowReceived ? "Combat received" : "Combat dealt", timeText, "", [$"Damage {damage:N0} / sec", $"Healing {healing:N0} / sec"]);
        }
        Cursor = new Cursor(encounter != null ? StandardCursorType.Hand : StandardCursorType.Cross);
        var text = tooltip.Title + "\n" + tooltip.TimeText + "\n" + tooltip.ContextText + "\n" + string.Join("\n", tooltip.Lines);
        if (text == lastInspection) return;
        lastInspection = text;
        InspectionChanged?.Invoke(tooltip, point);
    }

    private void HideTooltip()
    {
        lastInspection = null;
        InspectionChanged?.Invoke(null, default);
        Cursor = new Cursor(StandardCursorType.Cross);
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
        var encounter = encounterHits.LastOrDefault(hit => hit.Bounds.Contains(position));
        if (encounter != null) { EncounterSelected?.Invoke(encounter.Encounter.Id); e.Handled = true; }
    }

    private double X(DateTime time) => Plot.Left + (time - StartUtc).TotalSeconds / WindowSeconds * Plot.Width;
    private double Damage(ActivityLiveTimelineSample sample) => IsGatheringMode ? sample.GatheringQuantity : ShowReceived ? sample.DamageReceived : sample.DamageDealt;
    private double Healing(ActivityLiveTimelineSample sample) => IsGatheringMode ? sample.FishingQuantity : ShowReceived ? sample.HealingReceived : sample.HealingDone;
    private static string EncounterKind(ActivityEncounterKind kind) => kind switch { ActivityEncounterKind.Pve => "PvE", ActivityEncounterKind.Pvp => "PvP", ActivityEncounterKind.Unknown => "", _ => kind.ToString() };
    private static string GapLabel(string reason) => reason switch
    {
        "Capture paused" => "Recording paused",
        "Outside the session" => "Outside session",
        "No tracked activity" => "No activity recorded",
        _ => reason
    };
    private static int Priority(ActivityLiveTimelineMarkerKind kind) => kind switch
    {
        ActivityLiveTimelineMarkerKind.Death => 7,
        ActivityLiveTimelineMarkerKind.PlayerKill => 6,
        ActivityLiveTimelineMarkerKind.Knockdown => 5,
        ActivityLiveTimelineMarkerKind.Loot or ActivityLiveTimelineMarkerKind.Gathering or ActivityLiveTimelineMarkerKind.Fishing => 4,
        ActivityLiveTimelineMarkerKind.MobKill => 3,
        ActivityLiveTimelineMarkerKind.Fame => 2,
        _ => 1
    };
    private static Color MarkerColor(ActivityLiveTimelineMarkerKind kind) => kind switch
    {
        ActivityLiveTimelineMarkerKind.Death or ActivityLiveTimelineMarkerKind.Knockdown => Color.Parse("#F47D7D"),
        ActivityLiveTimelineMarkerKind.PlayerKill => Color.Parse("#EB9C64"),
        ActivityLiveTimelineMarkerKind.Loot => Color.Parse("#BF91F0"),
        ActivityLiveTimelineMarkerKind.Gathering => GatheringColor,
        ActivityLiveTimelineMarkerKind.Fishing => FishingColor,
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
            case ActivityLiveTimelineMarkerKind.Gathering:
                context.DrawLine(pen, center + new Vector(-4, 5), center + new Vector(3, -4));
                context.DrawLine(pen, center + new Vector(-5, -3), center + new Vector(0, -6));
                context.DrawLine(pen, center + new Vector(0, -6), center + new Vector(5, -3));
                break;
            case ActivityLiveTimelineMarkerKind.Fishing:
                context.DrawLine(pen, center + new Vector(-4, 5), center + new Vector(1, -6));
                context.DrawLine(pen, center + new Vector(1, -6), center + new Vector(5, -3));
                context.DrawLine(pen, center + new Vector(5, -3), center + new Vector(5, 2));
                context.DrawEllipse(null, pen, center + new Vector(3, 3), 2, 2);
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

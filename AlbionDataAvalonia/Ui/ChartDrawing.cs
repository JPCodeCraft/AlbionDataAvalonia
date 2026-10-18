using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlbionDataAvalonia.Ui;

public enum TextAlign { Left, Center, Right }

/// <summary>
/// Text, tooltips and count badges for the canvases that draw their own content (the live timeline and the map),
/// in one look and with brushes from <see cref="UiPalette"/>.
/// </summary>
public static class ChartDrawing
{
    public const double TooltipTitleSize = 13;
    public const double TooltipLineSize = 12;
    public const double TooltipRadius = 6;
    public static readonly Thickness TooltipPadding = new(10, 7);
    public const double TooltipIconSize = 30;
    private const double TooltipRowHeight = 34;
    private const double TooltipRowGap = 6;
    private const double TooltipIconGap = 8;
    private const double TooltipValueGap = 16;

    /// <summary>
    /// An item or mob row in a tooltip; Icon is null until the image has loaded. A round row (a mob's portrait) keeps a
    /// round space while it loads.
    /// </summary>
    public sealed record TooltipRow(IImage? Icon, string Text, string Value = "", bool Round = false);

    public static FormattedText Text(string value, double size, Color color, FontWeight weight = FontWeight.Normal) =>
        new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, UiPalette.Brush(color));

    /// <summary>Draws text with its left, center or right edge at the point.</summary>
    public static void DrawText(DrawingContext context, string value, Point at, double size, Color color,
        TextAlign align = TextAlign.Left, FontWeight weight = FontWeight.Normal)
    {
        var text = Text(value, size, color, weight);
        var x = align switch { TextAlign.Center => at.X - text.Width / 2, TextAlign.Right => at.X - text.Width, _ => at.X };
        context.DrawText(text, new Point(x, at.Y));
    }

    /// <summary>
    /// A tooltip box near the pointer: a bold title, then muted lines. It flips above the pointer and stays inside
    /// the bounds.
    /// </summary>
    public static void DrawTooltip(DrawingContext context, Rect bounds, Point pointer, string title, IEnumerable<string> lines) =>
        DrawTooltip(context, bounds, pointer, title, [], lines);

    /// <summary>
    /// A tooltip with item rows between the title and the lines: an icon, a name and a value on the right, like the
    /// item cells in lists. A row whose icon hasn't loaded keeps its space.
    /// </summary>
    public static void DrawTooltip(DrawingContext context, Rect bounds, Point pointer, string title, IReadOnlyList<TooltipRow> rows,
        IEnumerable<string> lines)
    {
        var heading = Text(title, TooltipTitleSize, UiPalette.Text, FontWeight.SemiBold);
        var texts = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => Text(line, TooltipLineSize, UiPalette.Muted)).ToArray();
        var cells = rows.Select(row => (row.Icon, row.Round, Name: Text(row.Text, TooltipLineSize, UiPalette.Text),
            Value: row.Value.Length > 0 ? Text(row.Value, TooltipLineSize, UiPalette.Text, FontWeight.SemiBold) : null)).ToArray();
        var rowWidth = cells.Length == 0 ? 0 : cells.Max(cell => TooltipIconSize + TooltipIconGap + cell.Name.Width +
            (cell.Value is null ? 0 : TooltipValueGap + cell.Value.Width));
        var width = Math.Max(Math.Max(heading.Width, rowWidth), texts.Length == 0 ? 0 : texts.Max(text => text.Width))
            + TooltipPadding.Left + TooltipPadding.Right;
        var height = heading.Height + cells.Length * TooltipRowHeight + (cells.Length > 0 ? TooltipRowGap : 0)
            + texts.Sum(text => text.Height) + TooltipPadding.Top + TooltipPadding.Bottom;
        var left = Math.Clamp(pointer.X + 14, bounds.Left + 4, Math.Max(bounds.Left + 4, bounds.Right - width - 4));
        var top = Math.Max(bounds.Top + 4, pointer.Y + 16 + height > bounds.Bottom ? pointer.Y - height - 10 : pointer.Y + 16);
        context.DrawRectangle(UiPalette.Brush(UiPalette.Tooltip), UiPalette.Pen(UiPalette.TooltipEdge), new Rect(left, top, width, height),
            TooltipRadius, TooltipRadius);
        var x = left + TooltipPadding.Left;
        var y = top + TooltipPadding.Top;
        context.DrawText(heading, new Point(x, y));
        y += heading.Height + (cells.Length > 0 ? TooltipRowGap / 2 : 0);
        foreach (var cell in cells)
        {
            var icon = new Rect(x, y + (TooltipRowHeight - TooltipIconSize) / 2, TooltipIconSize, TooltipIconSize);
            if (cell.Icon is { } image) context.DrawImage(image, new Rect(image.Size), icon);
            else if (cell.Round) context.DrawEllipse(UiPalette.Brush(UiPalette.TooltipEdge), null, icon.Center, icon.Width * 0.35, icon.Height * 0.35);
            else context.DrawRectangle(UiPalette.Brush(UiPalette.TooltipEdge), null, icon, 4, 4);
            var middle = y + TooltipRowHeight / 2;
            context.DrawText(cell.Name, new Point(icon.Right + TooltipIconGap, middle - cell.Name.Height / 2));
            if (cell.Value is { } value) context.DrawText(value, new Point(left + width - TooltipPadding.Right - value.Width, middle - value.Height / 2));
            y += TooltipRowHeight;
        }
        y += cells.Length > 0 ? TooltipRowGap / 2 : 0;
        foreach (var text in texts)
        {
            context.DrawText(text, new Point(x, y));
            y += text.Height;
        }
    }

    /// <summary>A small rounded count, such as "12" or "99+", with its bottom-left corner beside the point.</summary>
    public static void DrawCountBadge(DrawingContext context, Point at, int count)
    {
        var text = Text(count > 99 ? "99+" : count.ToString(CultureInfo.CurrentCulture), 10, UiPalette.Color("UiTextColor"), FontWeight.Bold);
        var width = Math.Max(15, text.Width + 7);
        var badge = new Rect(at.X + 4, at.Y - 15, width, 14);
        context.DrawRectangle(UiPalette.Brush("UiMapCountColor"), UiPalette.Pen(UiPalette.Color("UiMapCountEdgeColor")), badge, 7, 7);
        context.DrawText(text, new Point(badge.X + (width - text.Width) / 2, badge.Y + (14 - text.Height) / 2));
    }
}

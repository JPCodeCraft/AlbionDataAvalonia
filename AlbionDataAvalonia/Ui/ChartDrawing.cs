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
    public static void DrawTooltip(DrawingContext context, Rect bounds, Point pointer, string title, IEnumerable<string> lines)
    {
        var texts = new List<FormattedText> { Text(title, TooltipTitleSize, UiPalette.Text, FontWeight.SemiBold) };
        texts.AddRange(lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => Text(line, TooltipLineSize, UiPalette.Muted)));
        var width = texts.Max(text => text.Width) + TooltipPadding.Left + TooltipPadding.Right;
        var height = texts.Sum(text => text.Height) + TooltipPadding.Top + TooltipPadding.Bottom;
        var left = Math.Clamp(pointer.X + 14, bounds.Left + 4, Math.Max(bounds.Left + 4, bounds.Right - width - 4));
        var top = Math.Max(bounds.Top + 4, pointer.Y + 16 + height > bounds.Bottom ? pointer.Y - height - 10 : pointer.Y + 16);
        context.DrawRectangle(UiPalette.Brush(UiPalette.Tooltip), UiPalette.Pen(UiPalette.TooltipEdge), new Rect(left, top, width, height),
            TooltipRadius, TooltipRadius);
        var y = top + TooltipPadding.Top;
        foreach (var text in texts)
        {
            context.DrawText(text, new Point(left + TooltipPadding.Left, y));
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

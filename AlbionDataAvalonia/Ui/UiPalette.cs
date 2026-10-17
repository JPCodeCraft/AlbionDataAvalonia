using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Serilog;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Ui;

/// <summary>
/// Reads the color tokens in Themes/Tokens.axaml for code that draws, so a canvas and a view can't disagree.
/// Brushes and pens are cached by color, because canvases redraw many times a second. XAML uses
/// {DynamicResource} instead; controls built in code use <see cref="Bind"/>.
/// </summary>
public static class UiPalette
{
    private static readonly Dictionary<string, Color> colors = new();
    private static readonly Dictionary<Color, IImmutableSolidColorBrush> brushes = new();
    private static readonly Dictionary<(Color, double, PenLineCap, PenLineJoin), ImmutablePen> pens = new();

    public static Color Text => Color("UiCanvasTextColor");
    public static Color Muted => Color("UiCanvasMutedColor");
    public static Color Pill => Color("UiCanvasPillColor");
    public static Color Grid => Color("UiCanvasGridColor");
    public static Color Cursor => Color("UiCanvasCursorColor");
    public static Color Gap => Color("UiCanvasGapColor");
    public static Color Chart => Color("UiSurfacePressedColor");
    public static Color Hatch => Color("UiBorderStrongColor");
    public static Color Tooltip => Color("UiTooltipColor");
    public static Color TooltipEdge => Color("UiBorderStrongColor");
    public static Color Hover => Color("UiHoverBrush");

    /// <summary>A color token by key. A missing key is logged once and drawn magenta, so it is easy to spot.</summary>
    public static Color Color(string key)
    {
        if (colors.TryGetValue(key, out var color)) return color;
        object? value = null;
        var found = Application.Current?.TryGetResource(key, ThemeVariant.Dark, out value) == true;
        color = value switch
        {
            Color token => token,
            ISolidColorBrush brush => brush.Color,
            _ => Colors.Magenta
        };
        if (!found) Log.Warning("UI token {Key} is missing.", key);
        colors[key] = color;
        return color;
    }

    public static IImmutableSolidColorBrush Brush(string key) => Brush(Color(key));

    public static IImmutableSolidColorBrush Brush(Color color)
    {
        if (!brushes.TryGetValue(color, out var brush)) brushes[color] = brush = new ImmutableSolidColorBrush(color);
        return brush;
    }

    public static ImmutablePen Pen(Color color, double thickness = 1, PenLineCap cap = PenLineCap.Flat, PenLineJoin join = PenLineJoin.Miter)
    {
        var key = (color, thickness, cap, join);
        if (!pens.TryGetValue(key, out var pen))
            pens[key] = pen = new ImmutablePen(Brush(color), thickness, lineCap: cap, lineJoin: join);
        return pen;
    }

    /// <summary>The same color with a new opacity, from 0 to 255.</summary>
    public static Color WithAlpha(Color color, double alpha) =>
        Avalonia.Media.Color.FromArgb((byte)System.Math.Clamp(alpha, 0, 255), color.R, color.G, color.B);

    /// <summary>A color part way from one to the other, such as along the path from its start to now.</summary>
    public static Color Blend(Color from, Color to, double fraction) => Avalonia.Media.Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * fraction), (byte)(from.G + (to.G - from.G) * fraction), (byte)(from.B + (to.B - from.B) * fraction));

    /// <summary>Gives a property of a control built in code a token (a brush, size or radius), the way {DynamicResource} does in XAML.</summary>
    public static void Bind(Control control, AvaloniaProperty property, string key) =>
        control.Bind(property, control.GetResourceObservable(key));
}

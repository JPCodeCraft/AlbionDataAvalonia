using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Views;

public sealed record ActivityMapTick(double Fraction, ActivityMapMarkerKind Kind);

/// <summary>Coloured ticks along the history timeline, one per event, behind the slider.</summary>
public sealed class ActivityMapTimelineTicks : Control
{
    public static readonly StyledProperty<IReadOnlyList<ActivityMapTick>?> TicksProperty =
        AvaloniaProperty.Register<ActivityMapTimelineTicks, IReadOnlyList<ActivityMapTick>?>(nameof(Ticks));

    static ActivityMapTimelineTicks()
    {
        AffectsRender<ActivityMapTimelineTicks>(TicksProperty);
    }

    public IReadOnlyList<ActivityMapTick>? Ticks
    {
        get => GetValue(TicksProperty);
        set => SetValue(TicksProperty, value);
    }

    // The slider thumb travels inside a margin of half its width, so ticks use the same inset.
    public double Inset { get; set; } = 10;

    public override void Render(DrawingContext context)
    {
        if (Ticks is not { } ticks || Bounds.Width <= Inset * 2) return;
        var width = Bounds.Width - Inset * 2;
        foreach (var tick in ticks)
            context.FillRectangle(UiPalette.Brush(ActivityPalette.MapMarker(tick.Kind)), new Rect(Inset + tick.Fraction * width - 1, 0, 2, Bounds.Height));
    }
}

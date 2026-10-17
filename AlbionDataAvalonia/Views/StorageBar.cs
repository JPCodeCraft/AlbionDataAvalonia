using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Views;

/// <summary>A share of the bar; a null kind is the rest of the database.</summary>
public sealed record StorageSegment(double Value, DataCleanupType? Kind);

/// <summary>A single rounded bar split into segments sized by their share of the total.</summary>
public sealed class StorageBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<StorageSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<StorageBar, IReadOnlyList<StorageSegment>?>(nameof(Segments));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<StorageBar, IBrush?>(nameof(TrackBrush));

    static StorageBar()
    {
        AffectsRender<StorageBar>(SegmentsProperty, TrackBrushProperty);
    }

    public IReadOnlyList<StorageSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using (context.PushClip(new RoundedRect(bounds, bounds.Height / 2)))
        {
            context.FillRectangle(TrackBrush ?? UiPalette.Brush("UiHoverBrush"), bounds);
            var segments = Segments;
            var total = segments?.Sum(segment => segment.Value) ?? 0;
            if (segments is null || total <= 0)
            {
                return;
            }

            var x = 0d;
            foreach (var segment in segments)
            {
                var width = bounds.Width * segment.Value / total;
                if (width > 0)
                {
                    context.FillRectangle(UiPalette.Brush(ActivityPalette.Storage(segment.Kind)), new Rect(x, 0, width, bounds.Height));
                }

                x += width;
            }
        }
    }
}

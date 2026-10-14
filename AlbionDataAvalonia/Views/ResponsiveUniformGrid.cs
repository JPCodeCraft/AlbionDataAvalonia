using Avalonia;
using Avalonia.Controls.Primitives;
using System;

namespace AlbionDataAvalonia.Views;

// Keep equal-sized cards readable as the available pane narrows.
public class ResponsiveUniformGrid : UniformGrid
{
    public static readonly StyledProperty<double> MinimumItemWidthProperty =
        AvaloniaProperty.Register<ResponsiveUniformGrid, double>(nameof(MinimumItemWidth), 160,
            validate: value => double.IsFinite(value) && value > 0);
    public static readonly StyledProperty<int> MaximumColumnsProperty =
        AvaloniaProperty.Register<ResponsiveUniformGrid, int>(nameof(MaximumColumns), 4,
            validate: value => value > 0);

    static ResponsiveUniformGrid() =>
        AffectsMeasure<ResponsiveUniformGrid>(MinimumItemWidthProperty, MaximumColumnsProperty);

    public double MinimumItemWidth
    {
        get => GetValue(MinimumItemWidthProperty);
        set => SetValue(MinimumItemWidthProperty, value);
    }

    public int MaximumColumns
    {
        get => GetValue(MaximumColumnsProperty);
        set => SetValue(MaximumColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Columns = double.IsFinite(availableSize.Width)
            ? (int)Math.Clamp(Math.Floor(availableSize.Width / MinimumItemWidth), 1, MaximumColumns)
            : MaximumColumns;
        return base.MeasureOverride(availableSize);
    }
}

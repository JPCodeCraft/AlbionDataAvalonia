using Avalonia;
using Avalonia.Controls;
using System;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>
/// A row with its first child on the left and its second on the right, such as a list's scope and its actions.
/// When the row is too narrow for both, the second moves under the first, still on the right. A hidden child
/// leaves the line to the other one.
/// </summary>
public class ActionRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ActionRow, double>(nameof(Spacing), 12,
            validate: value => double.IsFinite(value) && value >= 0);

    static ActionRow() => AffectsMeasure<ActionRow>(SpacingProperty);

    /// <summary>The gap between the two children, beside or under each other.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private bool BothVisible => Children.Count > 1 && Children[0].IsVisible && Children[1].IsVisible;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        var (start, end) = (Measured(0), Measured(1));
        return OnOneLine(availableSize.Width)
            ? new Size(start.Width + (BothVisible ? Spacing : 0) + end.Width, Math.Max(start.Height, end.Height))
            : new Size(Math.Max(start.Width, end.Width), start.Height + Spacing + end.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (start, end) = (Measured(0), Measured(1));
        var oneLine = OnOneLine(finalSize.Width);
        var endWidth = Math.Min(end.Width, finalSize.Width);
        if (Children.Count > 0)
            Children[0].Arrange(new Rect(0, 0, Math.Min(start.Width, finalSize.Width), oneLine ? finalSize.Height : start.Height));
        if (Children.Count > 1)
            Children[1].Arrange(oneLine
                ? new Rect(finalSize.Width - endWidth, 0, endWidth, finalSize.Height)
                : new Rect(finalSize.Width - endWidth, start.Height + Spacing, endWidth, end.Height));
        return finalSize;
    }

    private bool OnOneLine(double width) => !BothVisible || Measured(0).Width + Spacing + Measured(1).Width <= width + 0.5;

    private Size Measured(int index) => index < Children.Count ? Children[index].DesiredSize : default;
}

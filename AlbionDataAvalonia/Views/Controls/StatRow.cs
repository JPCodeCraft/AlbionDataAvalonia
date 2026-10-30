using Avalonia;
using Avalonia.Controls.Primitives;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>
/// One detail line inside a <see cref="StatCard"/>: a label on the left, the value on the right with an optional
/// rate or note after it. The <c>Dim</c> class greys the row for a value that is zero or not recorded.
/// </summary>
public class StatRow : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<StatRow, string?>(nameof(Label));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<StatRow, string?>(nameof(Value));

    public static readonly StyledProperty<string?> RateProperty =
        AvaloniaProperty.Register<StatRow, string?>(nameof(Rate));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Rate
    {
        get => GetValue(RateProperty);
        set => SetValue(RateProperty, value);
    }
}

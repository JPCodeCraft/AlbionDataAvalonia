using Avalonia;
using Avalonia.Controls.Primitives;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>
/// A label, a value and an optional detail line. Classes set the look: <c>Card</c> and <c>Outlined</c> for the frame,
/// <c>Small</c>, <c>Heading</c>, <c>Large</c> or <c>Text</c> for the value size, <c>Accent</c> for an orange value and
/// <c>Compact</c> to keep every line on one row.
/// </summary>
public class StatTile : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<StatTile, string?>(nameof(Label));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<StatTile, string?>(nameof(Value));

    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<StatTile, string?>(nameof(Detail));

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

    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }
}

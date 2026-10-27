using Avalonia;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>
/// A card that groups one subject's numbers: an upper-case caption, a headline value with a short unit or rate
/// beside it, and the content below for the detail rows (<see cref="StatRow"/>). The <c>Accent</c> class colours
/// the headline orange.
/// </summary>
public class StatCard : ContentControl
{
    public static readonly StyledProperty<string?> CaptionProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Caption));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Value));

    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Unit));

    public string? Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }
}

using Avalonia;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>The message a list or table shows when it has nothing to show, with an optional hint and action.</summary>
public class EmptyState : ContentControl
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Message));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Hint));

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }
}

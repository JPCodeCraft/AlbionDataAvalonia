using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>
/// A labelled filter in a page's toolbar. The label sits above the control, and the control is named after it for
/// screen readers unless it has a name of its own.
/// </summary>
public class FilterField : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FilterField, string?>(nameof(Label));

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == LabelProperty || change.Property == ContentProperty)
            && Content is Control control && !string.IsNullOrEmpty(Label)
            && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
        {
            AutomationProperties.SetName(control, Label);
        }
    }
}

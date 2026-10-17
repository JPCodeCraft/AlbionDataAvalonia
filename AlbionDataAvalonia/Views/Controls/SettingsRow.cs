using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>
/// One row of a <see cref="SettingsGroup"/>: a title and hint with the row's control at the end, or below them
/// with the <c>Stacked</c> class. Without a title, the content fills the row. The control is named after the title
/// for screen readers unless it has a name of its own.
/// </summary>
public class SettingsRow : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Hint));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public SettingsRow() => PseudoClasses.Set(":untitled", true);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
        {
            PseudoClasses.Set(":untitled", string.IsNullOrEmpty(Title));
            NameContent();
        }
        else if (change.Property == HintProperty)
        {
            PseudoClasses.Set(":hinted", !string.IsNullOrEmpty(Hint));
        }
        else if (change.Property == ContentProperty)
        {
            NameContent();
        }
    }

    private void NameContent()
    {
        if (Content is Control control && !string.IsNullOrEmpty(Title)
            && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
        {
            AutomationProperties.SetName(control, Title);
        }
    }
}

using Avalonia;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views.Controls;

public enum InfoBannerKind { Warning, Error, Info }

/// <summary>
/// A notice above a page's content: a title, a message and an optional action. The action sits at the end of the
/// banner, or below the message with the <c>Stacked</c> class.
/// </summary>
public class InfoBanner : ContentControl
{
    public static readonly StyledProperty<InfoBannerKind> KindProperty =
        AvaloniaProperty.Register<InfoBanner, InfoBannerKind>(nameof(Kind));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<InfoBanner, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<InfoBanner, string?>(nameof(Message));

    public InfoBannerKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public InfoBanner() => UpdateKind();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty) UpdateKind();
    }

    private void UpdateKind()
    {
        PseudoClasses.Set(":error", Kind == InfoBannerKind.Error);
        PseudoClasses.Set(":info", Kind == InfoBannerKind.Info);
    }
}

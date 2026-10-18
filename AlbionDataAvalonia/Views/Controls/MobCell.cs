using Avalonia;
using Avalonia.Controls.Primitives;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>A mob's portrait, in the ring that shows its rank, with its name and a line under it, like <see cref="ItemCell"/>.</summary>
public class MobCell : TemplatedControl
{
    public static readonly StyledProperty<string?> MobUniqueNameProperty =
        AvaloniaProperty.Register<MobCell, string?>(nameof(MobUniqueName));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<MobCell, double>(nameof(IconSize), 32);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<MobCell, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<MobCell, string?>(nameof(Subtitle));

    public string? MobUniqueName
    {
        get => GetValue(MobUniqueNameProperty);
        set => SetValue(MobUniqueNameProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}

using Avalonia;
using Avalonia.Controls.Primitives;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>An item's icon with its name and up to two lines under it, such as its quality.</summary>
public class ItemCell : TemplatedControl
{
    public static readonly StyledProperty<string?> ItemUniqueNameProperty =
        AvaloniaProperty.Register<ItemCell, string?>(nameof(ItemUniqueName));

    public static readonly StyledProperty<int> ImageQualityProperty =
        AvaloniaProperty.Register<ItemCell, int>(nameof(ImageQuality), 1);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<ItemCell, double>(nameof(IconSize), 32);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ItemCell, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<ItemCell, string?>(nameof(Subtitle));

    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<ItemCell, string?>(nameof(Detail));

    public string? ItemUniqueName
    {
        get => GetValue(ItemUniqueNameProperty);
        set => SetValue(ItemUniqueNameProperty, value);
    }

    public int ImageQuality
    {
        get => GetValue(ImageQualityProperty);
        set => SetValue(ImageQualityProperty, value);
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

    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }
}

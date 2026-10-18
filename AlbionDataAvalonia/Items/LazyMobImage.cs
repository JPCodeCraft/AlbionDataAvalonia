using AlbionDataAvalonia.Items.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items;

/// <summary>
/// Mob portraits for <see cref="Image"/> controls (<c>items:LazyMobImage.MobUniqueName</c>) and for code that draws,
/// the way <see cref="LazyItemImage"/> gives item icons.
/// </summary>
public sealed class LazyMobImage
{
    public static readonly AttachedProperty<string?> MobUniqueNameProperty =
        AvaloniaProperty.RegisterAttached<LazyMobImage, Image, string?>("MobUniqueName");

    private static MobImageService? mobImageService;

    static LazyMobImage()
    {
        MobUniqueNameProperty.Changed.AddClassHandler<Image>((image, _) => Load(image));
    }

    private LazyMobImage()
    {
    }

    public static void Configure(MobImageService service)
    {
        mobImageService = service;
    }

    /// <summary>
    /// A mob's portrait for code that draws it (the chart and map tooltips). Null until it has loaded;
    /// <paramref name="loaded"/> then runs on the UI thread so the caller can redraw.
    /// </summary>
    public static IImage? ForDrawing(string? mobUniqueName, Action loaded)
    {
        if (mobImageService is not { } service || string.IsNullOrWhiteSpace(mobUniqueName)) return null;
        var portrait = service.GetPortraitAsync(mobUniqueName);
        if (portrait.IsCompletedSuccessfully) return portrait.Result;
        if (!portrait.IsCompleted) portrait.ContinueWith(_ => Dispatcher.UIThread.Post(loaded), TaskScheduler.Default);
        return null;
    }

    public static string? GetMobUniqueName(Image image) => image.GetValue(MobUniqueNameProperty);

    public static void SetMobUniqueName(Image image, string? value) => image.SetValue(MobUniqueNameProperty, value);

    // Recycled rows set a new name; a portrait that arrives after that is dropped.
    private static void Load(Image image)
    {
        image.Source = null;
        var name = GetMobUniqueName(image);
        if (mobImageService is not { } service || string.IsNullOrWhiteSpace(name)) return;
        var portrait = service.GetPortraitAsync(name);
        if (portrait.IsCompletedSuccessfully)
        {
            image.Source = portrait.Result;
            return;
        }

        portrait.ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            if (task.IsCompletedSuccessfully && string.Equals(GetMobUniqueName(image), name, StringComparison.Ordinal))
                image.Source = task.Result;
        }), TaskScheduler.Default);
    }
}

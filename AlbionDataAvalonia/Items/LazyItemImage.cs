using AlbionDataAvalonia.Items.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items;

public sealed class LazyItemImage
{
    public static readonly AttachedProperty<string?> ItemUniqueNameProperty =
        AvaloniaProperty.RegisterAttached<LazyItemImage, Image, string?>("ItemUniqueName");

    public static readonly AttachedProperty<int> QualityProperty =
        AvaloniaProperty.RegisterAttached<LazyItemImage, Image, int>("Quality", 1);

    private static readonly AttachedProperty<LazyImageState?> StateProperty =
        AvaloniaProperty.RegisterAttached<LazyItemImage, Image, LazyImageState?>("State");

    private static ItemImageService? itemImageService;

    static LazyItemImage()
    {
        ItemUniqueNameProperty.Changed.AddClassHandler<Image>((image, _) => RestartLoad(image));
        QualityProperty.Changed.AddClassHandler<Image>((image, _) => RestartLoad(image));
    }

    private LazyItemImage()
    {
    }

    public static void Configure(ItemImageService service)
    {
        itemImageService = service;
    }

    public static string? GetItemUniqueName(Image image)
    {
        return image.GetValue(ItemUniqueNameProperty);
    }

    public static void SetItemUniqueName(Image image, string? value)
    {
        image.SetValue(ItemUniqueNameProperty, value);
    }

    public static int GetQuality(Image image)
    {
        return image.GetValue(QualityProperty);
    }

    public static void SetQuality(Image image, int value)
    {
        image.SetValue(QualityProperty, value);
    }

    private static void RestartLoad(Image image)
    {
        var state = GetState(image);
        CancelCurrentLoad(state);
        image.Source = null;

        if (IsAttached(image))
        {
            BeginLoad(image, state);
        }
    }

    private static LazyImageState GetState(Image image)
    {
        var state = image.GetValue(StateProperty);
        if (state is not null)
        {
            return state;
        }

        state = new LazyImageState();
        image.SetValue(StateProperty, state);
        image.AttachedToVisualTree += ImageAttachedToVisualTree;
        image.DetachedFromVisualTree += ImageDetachedFromVisualTree;
        return state;
    }

    private static void ImageAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        BeginLoad(image, GetState(image));
    }

    private static void ImageDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        var state = GetState(image);
        CancelCurrentLoad(state);
        image.Source = null;
    }

    private static void BeginLoad(Image image, LazyImageState state)
    {
        var service = itemImageService;
        if (service is null)
        {
            return;
        }

        var itemUniqueName = GetItemUniqueName(image);
        if (string.IsNullOrWhiteSpace(itemUniqueName))
        {
            return;
        }

        var quality = NormalizeQuality(GetQuality(image));
        CancelCurrentLoad(state);
        image.Source = null;

        var version = ++state.Version;
        var cancellationTokenSource = new CancellationTokenSource();
        state.CancellationTokenSource = cancellationTokenSource;

        _ = LoadImageAsync(image, service, itemUniqueName, quality, version, cancellationTokenSource.Token);
    }

    private static async Task LoadImageAsync(
        Image image,
        ItemImageService service,
        string itemUniqueName,
        int quality,
        long version,
        CancellationToken cancellationToken)
    {
        try
        {
            var imageSource = await service.GetItemImageAsync(itemUniqueName, quality);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var state = image.GetValue(StateProperty);
                if (state?.Version != version
                    || cancellationToken.IsCancellationRequested
                    || !MatchesCurrentRequest(image, itemUniqueName, quality))
                {
                    return;
                }

                image.Source = imageSource;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Log.Debug(ex, "Failed to apply lazy Albion item image. ItemUniqueName={ItemUniqueName} Quality={Quality}", itemUniqueName, quality);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var state = image.GetValue(StateProperty);
                    if (state?.Version == version && MatchesCurrentRequest(image, itemUniqueName, quality))
                    {
                        image.Source = null;
                    }
                });
            }
        }
    }

    private static bool MatchesCurrentRequest(Image image, string itemUniqueName, int quality)
    {
        return string.Equals(GetItemUniqueName(image), itemUniqueName, StringComparison.Ordinal)
            && NormalizeQuality(GetQuality(image)) == quality
            && IsAttached(image);
    }

    private static bool IsAttached(Image image)
    {
        return image.GetVisualRoot() is not null;
    }

    private static int NormalizeQuality(int quality)
    {
        return quality <= 0 ? 1 : quality;
    }

    private static void CancelCurrentLoad(LazyImageState state)
    {
        state.Version++;
        state.CancellationTokenSource?.Cancel();
        state.CancellationTokenSource?.Dispose();
        state.CancellationTokenSource = null;
    }

    private sealed class LazyImageState
    {
        public long Version { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }
    }
}

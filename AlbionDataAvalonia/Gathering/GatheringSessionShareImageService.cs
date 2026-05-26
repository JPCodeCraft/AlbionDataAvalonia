using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Gathering;

public sealed class GatheringSessionShareImageService
{
    public const int CardLogicalWidth = 1200;
    public const int CardLogicalHeight = 675;
    public const int ImageScale = 2;
    public const int CardWidth = CardLogicalWidth * ImageScale;
    public const int CardHeight = CardLogicalHeight * ImageScale;

    private const double BaseDpi = 96;

    public async Task<RenderTargetBitmap> RenderPngBitmapAsync(Control card)
    {
        await Dispatcher.UIThread.InvokeAsync(card.UpdateLayout, DispatcherPriority.Render);

        var dpi = new Vector(BaseDpi * ImageScale, BaseDpi * ImageScale);
        var bitmap = new RenderTargetBitmap(new PixelSize(CardWidth, CardHeight), dpi);
        bitmap.Render(card);
        return bitmap;
    }
}

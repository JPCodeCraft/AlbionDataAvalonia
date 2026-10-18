using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Runtime.InteropServices;

namespace AlbionDataAvalonia.Items;

/// <summary>
/// A mob's round portrait with the ring Albion draws around it (standard, veteran, elite or boss), as one image for an
/// <see cref="Avalonia.Controls.Image"/> or a canvas. Both textures share one square canvas with empty space around
/// the ring, so the image is the square around the ring: a standard mob's face fills more of it than a boss's, whose
/// ring is larger, as in the game.
/// </summary>
public sealed class MobPortrait : IImage
{
    // The textures are larger than most places show them; smooth scaling keeps the small ring readable.
    private static readonly RenderOptions Smooth = new() { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality };
    // Fainter pixels are the glow around a ring, which may be cut.
    private const byte VisibleAlpha = 64;

    private readonly Bitmap face;
    private readonly Bitmap? ring;
    private readonly Rect crop;

    private MobPortrait(Bitmap face, Bitmap? ring, Rect crop)
    {
        this.face = face;
        this.ring = ring;
        this.crop = crop;
    }

    public Size Size => crop.Size;

    /// <summary>Reads how far the portrait and ring reach; call it off the UI thread.</summary>
    public static MobPortrait Create(Bitmap face, Bitmap? ring)
    {
        var size = face.Size;
        var reach = Reach(face);
        if (ring is not null) reach = Math.Max(reach, Reach(ring) * size.Width / ring.Size.Width);
        var half = Math.Clamp(reach + 1, 1, size.Width / 2);
        return new MobPortrait(face, ring, new Rect(size.Width / 2 - half, size.Height / 2 - half, half * 2, half * 2));
    }

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
        var source = sourceRect.Translate(crop.Position);
        using (context.PushRenderOptions(Smooth))
        {
            context.DrawImage(face, source, destRect);
            if (ring is null) return;
            var scaleX = ring.Size.Width / face.Size.Width;
            var scaleY = ring.Size.Height / face.Size.Height;
            context.DrawImage(ring, new Rect(source.X * scaleX, source.Y * scaleY, source.Width * scaleX, source.Height * scaleY), destRect);
        }
    }

    // How far from the center the image's visible pixels reach, in device-independent pixels.
    private static double Reach(Bitmap bitmap)
    {
        var pixels = bitmap.PixelSize;
        if (pixels.Width <= 0 || pixels.Height <= 0) return 0;
        var stride = pixels.Width * 4;
        var bytes = new byte[stride * pixels.Height];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(pixels), handle.AddrOfPinnedObject(), bytes.Length, stride);
        }
        catch (Exception)
        {
            return bitmap.Size.Width / 2;
        }
        finally
        {
            handle.Free();
        }

        // Bgra and Rgba both keep alpha last.
        double reach = 0;
        double centerX = pixels.Width / 2d, centerY = pixels.Height / 2d;
        for (var y = 0; y < pixels.Height; y++)
            for (var x = 0; x < pixels.Width; x++)
                if (bytes[y * stride + x * 4 + 3] >= VisibleAlpha)
                    reach = Math.Max(reach, Math.Max(Math.Abs(x + 0.5 - centerX), Math.Abs(y + 0.5 - centerY)));
        return reach * bitmap.Size.Width / pixels.Width;
    }
}

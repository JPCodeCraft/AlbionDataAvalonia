using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Maps;

public readonly record struct MapPoint(double X, double Y);

/// <summary>A connection to another zone, at its position in this zone's local coordinates.</summary>
public sealed record MapZoneExit(string TargetId, MapPoint Position);

/// <summary>One of the game's own minimap icons (banks, markets, outposts) in a zone.</summary>
public sealed record MapZoneIcon(string MarkerType, string Label, MapPoint Position, double Scale = 1);

/// <summary>A zone from the AlbionWorld catalog the website also uses.</summary>
public sealed record MapZone(string Id, string DisplayName, string ImageFile, string MapCategory, string PvpCategory, int? Tier,
    MapPoint BoundsMin, MapPoint BoundsMax, IReadOnlyList<MapZoneExit> Exits)
{
    public MapImageGeometry? Geometry { get; } = MapImageGeometry.From(BoundsMin, BoundsMax);
}

/// <summary>The in-game zone map frame AlbionWorld publishes, drawn around a minimap.</summary>
public static class MapFrame
{
    /// <summary>The frame is a square image centred on the minimap, which fills this share of its side
    /// (<c>mapScale</c> in AlbionWorld's map-features manifest; the website uses the same number).</summary>
    public const double MapScale = 0.84;
}

/// <summary>
/// The generated minimaps are the zone's local rectangle rotated by -45 degrees on an expanded canvas
/// (Pillow's centred rotate with expand). This mirrors the website's map-image functions.
/// </summary>
public sealed class MapImageGeometry
{
    private const double AngleRadians = -45 * Math.PI / 180;
    private static readonly double Cosine = Math.Cos(AngleRadians);
    private static readonly double Sine = Math.Sin(AngleRadians);

    private MapImageGeometry(MapPoint min, MapPoint max, double canvasWidth, double canvasHeight)
    {
        Min = min;
        Max = max;
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
    }

    public MapPoint Min { get; }
    public MapPoint Max { get; }
    public double CanvasWidth { get; }
    public double CanvasHeight { get; }

    public static MapImageGeometry? From(MapPoint min, MapPoint max)
    {
        var sourceWidth = max.X - min.X;
        var sourceHeight = max.Y - min.Y;
        if (!double.IsFinite(sourceWidth) || !double.IsFinite(sourceHeight) || sourceWidth <= 0 || sourceHeight <= 0) return null;

        // Pillow rounds its affine coefficients to 15 decimals before measuring the expanded canvas.
        var cosine = Math.Round(Cosine, 15);
        var sine = Math.Round(Sine, 15);
        var rotatedWidth = Math.Abs(sourceWidth * cosine) + Math.Abs(sourceHeight * sine);
        var rotatedHeight = Math.Abs(sourceWidth * sine) + Math.Abs(sourceHeight * cosine);
        return new(min, max,
            Math.Ceiling((sourceWidth + rotatedWidth) / 2) - Math.Floor((sourceWidth - rotatedWidth) / 2),
            Math.Ceiling((sourceHeight + rotatedHeight) / 2) - Math.Floor((sourceHeight - rotatedHeight) / 2));
    }

    /// <summary>Where a local point lands on the minimap, as a fraction of its width and height.</summary>
    public MapPoint ToImageFraction(double x, double y)
    {
        var offsetX = x - (Min.X + Max.X) / 2;
        var offsetY = y - (Min.Y + Max.Y) / 2;
        var rotatedX = offsetX * Cosine - offsetY * Sine;
        var rotatedY = offsetX * Sine + offsetY * Cosine;
        return new((rotatedX + CanvasWidth / 2) / CanvasWidth, (CanvasHeight / 2 - rotatedY) / CanvasHeight);
    }
}

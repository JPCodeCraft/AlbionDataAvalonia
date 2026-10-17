using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Maps;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.ViewModels;

[Flags]
public enum ActivityMapLayers
{
    None = 0, Path = 1, Kills = 2, Loot = 4, Gathering = 8, Deaths = 16, Features = 32,
    All = Path | Kills | Loot | Gathering | Deaths | Features
}

/// <summary>A zone's danger level, which colors its exits.</summary>
public enum MapZoneKind { Blue, Yellow, Red, Black, Other }

public sealed record ActivityMapExit(MapPoint Position, string Label, MapZoneKind Zone);

public sealed record ActivityMapIcon(MapPoint Position, string Label, Bitmap? Image, double Scale = 1);

/// <summary>
/// Everything the map canvas draws for one frame. ViewKey changes when a different zone, run or mode is shown,
/// which resets zoom and panning; the rest can change every frame.
/// </summary>
public sealed record ActivityMapScene(
    string ViewKey,
    MapImageGeometry Geometry,
    Bitmap? Image,
    IReadOnlyList<ActivityPathPoint> Path,
    IReadOnlyList<ActivityMapMarker> Markers,
    IReadOnlyList<ActivityMapExit> Exits,
    IReadOnlyList<ActivityMapIcon> Icons,
    ActivityPathPoint? Position,
    double? Heading,
    bool IsLive,
    DateTime NowUtc,
    double FadeSeconds,
    DateTime? StartUtc,
    DateTime? UntilUtc,
    ActivityMapLayers Layers);

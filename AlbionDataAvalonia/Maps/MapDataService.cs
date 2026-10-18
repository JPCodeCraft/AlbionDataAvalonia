using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Settings;
using Avalonia.Media.Imaging;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Maps;

/// <summary>
/// Loads the AlbionWorld zone catalog, minimaps, minimap icons and zone features from the AFM CDN.
/// Every file is versioned, so a downloaded copy never changes and is kept until the version moves on.
/// </summary>
public sealed class MapDataService : IDisposable
{
    private const string DefaultVersion = "2026-08-31-1";
    private const string CatalogUrl = "https://cdn.albionfreemarket.com/AlbionWorld/albionLocations.json";
    private const string MapsBase = "https://cdn.albionfreemarket.com/AlbionWorld/map";
    private const string FeaturesBase = "https://cdn.albionfreemarket.com/AlbionWorld/map-features";
    private const int CachedImages = 3;
    // A feature on an exit's spot is the exit itself; the exit marker already shows it.
    private const double ExitTolerance = 0.01;
    // Royal outpost icons are a small banner in a large image; the website draws them bigger too.
    private const double OutpostIconScale = 1.65;
    private static readonly HashSet<string> OutpostIcons = ["Outpost_Forest", "Outpost_Highland", "Outpost_Mountain", "Outpost_Steppe", "Outpost_Swamp"];

    private readonly SettingsManager settings;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly SemaphoreSlim downloads = new(3);
    private readonly object sync = new();
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<MapZoneIcon>>> icons = new();
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> iconImages = new();
    private readonly LinkedList<(string File, Task<Bitmap?> Image)> images = new();
    private Task<IReadOnlyDictionary<string, MapZone>>? catalog;
    private bool staleVersionsRemoved;

    public MapDataService(SettingsManager settings)
    {
        this.settings = settings;
    }

    private string Version => string.IsNullOrWhiteSpace(settings.AppSettings.MapDataVersion) ? DefaultVersion : settings.AppSettings.MapDataVersion!;
    private static string CacheRoot => Path.Combine(AppData.LocalPath, "cache", "maps");
    private string VersionDirectory => Path.Combine(CacheRoot, SafeFileName(Version));

    /// <summary>The catalog zone for a captured map id, trying the full id first and then its '@' parts.
    /// A Mists instance has no zone of its own, so its fixed layout is tried last.</summary>
    public async Task<MapZone?> FindZoneAsync(string rawMapId, string? layout = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawMapId)) return null;
        var zones = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        foreach (var candidate in Candidates(rawMapId))
            if (zones.TryGetValue(candidate, out var zone)) return zone;
        return !string.IsNullOrWhiteSpace(layout) && zones.TryGetValue(layout, out var layoutZone) ? layoutZone : null;
    }

    public async Task<string?> ZoneNameAsync(string id, CancellationToken cancellationToken = default) =>
        (await GetCatalogAsync(cancellationToken).ConfigureAwait(false)).TryGetValue(id, out var zone) ? zone.DisplayName : null;

    public async Task<MapZone?> ZoneAsync(string id, CancellationToken cancellationToken = default) =>
        (await GetCatalogAsync(cancellationToken).ConfigureAwait(false)).GetValueOrDefault(id);

    /// <summary>The zone's minimap, or null when the CDN has none. A failed download throws.</summary>
    public Task<Bitmap?> GetImageAsync(MapZone zone)
    {
        lock (sync)
        {
            var cached = images.FirstOrDefault(x => x.File == zone.ImageFile);
            // A failed download is tried again; a missing image is not.
            if (cached.Image is { IsFaulted: true } or { IsCanceled: true }) images.Remove(cached);
            else if (cached.Image is not null)
            {
                images.Remove(cached);
                images.AddFirst(cached);
                return cached.Image;
            }

            var name = Path.GetFileNameWithoutExtension(zone.ImageFile);
            var image = LoadBitmapAsync($"{MapsBase}/images/{Uri.EscapeDataString(name)}.webp", Path.Combine("images", SafeFileName(name) + ".webp"));
            images.AddFirst((zone.ImageFile, image));
            // Decoded open-world minimaps are large, so only the last few stay in memory.
            while (images.Count > CachedImages) images.RemoveLast();
            return image;
        }
    }

    public Task<IReadOnlyList<MapZoneIcon>> GetIconsAsync(MapZone zone) =>
        icons.GetOrAdd(zone.Id, _ => LoadIconsAsync(zone));

    public Task<Bitmap?> GetIconImageAsync(string markerType) =>
        iconImages.GetOrAdd(markerType, LoadIconImageAsync);

    // A map still shows without an icon, so a failed icon download only leaves it out until it is asked for again.
    private async Task<Bitmap?> LoadIconImageAsync(string markerType)
    {
        try
        {
            return await LoadBitmapAsync($"{FeaturesBase}/icons/minimap/{Uri.EscapeDataString(markerType)}.png",
                Path.Combine("icons", SafeFileName(markerType) + ".png")).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException)
        {
            Log.Warning(error, "Could not load map icon {MarkerType}", markerType);
            iconImages.TryRemove(markerType, out _);
            return null;
        }
    }

    private Task<IReadOnlyDictionary<string, MapZone>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (catalog is null || catalog.IsFaulted || catalog.IsCanceled) catalog = LoadCatalogAsync();
            return catalog;
        }
    }

    private async Task<IReadOnlyDictionary<string, MapZone>> LoadCatalogAsync()
    {
        var path = await DownloadAsync(CatalogUrl, "albionLocations.json").ConfigureAwait(false);
        await using var stream = File.OpenRead(path);
        var entries = await JsonSerializer.DeserializeAsync<List<CatalogEntry>>(stream, JsonOptions).ConfigureAwait(false) ?? [];
        var zones = new Dictionary<string, MapZone>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.ImageFile)) continue;
            var min = Point(entry.MinimapBoundsMin) ?? Point(entry.Origin);
            var max = Point(entry.MinimapBoundsMax) ?? (Point(entry.Origin) is { } origin && Point(entry.Size) is { } size
                ? new MapPoint(origin.X + size.X, origin.Y + size.Y) : null);
            if (min is null || max is null) continue;
            var exits = (entry.Exits ?? []).Concat(entry.PortalExits ?? []).Concat(entry.PortalEntrances ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.TargetLocationId) && Point(x.Position) is not null)
                .Select(x => new MapZoneExit(x.TargetLocationId!, Point(x.Position)!.Value))
                .DistinctBy(x => (x.Position, x.TargetId)).ToArray();
            zones[entry.Id] = new MapZone(entry.Id, string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Id : entry.DisplayName,
                entry.ImageFile, entry.MapCategory ?? "", entry.PvpCategory ?? "",
                int.TryParse(entry.Tier, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tier) ? tier : null,
                min.Value, max.Value, exits);
        }

        Log.Information("Loaded {ZoneCount} map zones for map data version {Version}", zones.Count, Version);
        return zones;
    }

    private async Task<IReadOnlyList<MapZoneIcon>> LoadIconsAsync(MapZone zone)
    {
        try
        {
            var path = await DownloadAsync($"{FeaturesBase}/maps/{Uri.EscapeDataString(zone.Id)}.json",
                Path.Combine("features", SafeFileName(zone.Id) + ".json")).ConfigureAwait(false);
            await using var stream = File.OpenRead(path);
            var features = await JsonSerializer.DeserializeAsync<FeatureFile>(stream, JsonOptions).ConfigureAwait(false);
            return (features?.Features ?? [])
                .Where(x => x.Type is "minimap_marker" or "resource_landmark" && !string.IsNullOrWhiteSpace(x.MarkerType)
                    && x.MarkerType != "ClusterExit" && x.X is { } featureX && x.Z is { } featureZ && double.IsFinite(featureX) && double.IsFinite(featureZ)
                    && !zone.Exits.Any(exit => Math.Abs(exit.Position.X - featureX) <= ExitTolerance && Math.Abs(exit.Position.Y - featureZ) <= ExitTolerance))
                .Select(x => new MapZoneIcon(x.MarkerType!, Humanize(string.IsNullOrWhiteSpace(x.Label) ? x.MarkerType! : x.Label!), new(x.X!.Value, x.Z!.Value),
                    OutpostIcons.Contains(x.MarkerType!) ? OutpostIconScale : 1))
                .ToArray();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            Log.Warning(error, "Could not load map features for {ZoneId}", zone.Id);
            icons.TryRemove(zone.Id, out _);
            return [];
        }
    }

    // Null when the CDN has no such file or it can't be decoded; a failed download throws.
    private async Task<Bitmap?> LoadBitmapAsync(string url, string relativePath)
    {
        try
        {
            var path = await DownloadAsync(url, relativePath).ConfigureAwait(false);
            return new Bitmap(path);
        }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            Log.Information("No map image at {Url}", url);
            return null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            Log.Warning(error, "Could not read map image {Url}", url);
            return null;
        }
    }

    // Writes to a temporary file first so a cut download never leaves a broken cache entry.
    private async Task<string> DownloadAsync(string url, string relativePath)
    {
        var path = Path.Combine(VersionDirectory, relativePath);
        if (File.Exists(path)) return path;
        RemoveStaleVersions();
        await downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var versioned = $"{url}{(url.Contains('?') ? '&' : '?')}v={Uri.EscapeDataString(Version)}";
            using var response = await http.GetAsync(versioned, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var temporary = path + ".download";
            await using (var file = File.Create(temporary))
                await response.Content.CopyToAsync(file).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally
        {
            downloads.Release();
        }
    }

    private void RemoveStaleVersions()
    {
        lock (sync)
        {
            if (staleVersionsRemoved) return;
            staleVersionsRemoved = true;
        }

        try
        {
            if (!Directory.Exists(CacheRoot)) return;
            foreach (var directory in Directory.EnumerateDirectories(CacheRoot)
                         .Where(x => !string.Equals(Path.GetFileName(x), SafeFileName(Version), StringComparison.OrdinalIgnoreCase)))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Debug(error, "Could not remove old map data");
        }
    }

    // A generated instance (a random dungeon, the Mists, an island, a hideout) has no catalog zone of its own,
    // and its id can name the zone it sits in ("@HIDEOUT@2306@<guid>"), so only its full id may match.
    private static IEnumerable<string> Candidates(string rawMapId)
    {
        var trimmed = rawMapId.Trim().Trim('"', '\'');
        if (trimmed.Length == 0) yield break;
        yield return trimmed;
        if (IsGeneratedInstance(trimmed)) yield break;
        foreach (var part in trimmed.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
            if (!string.Equals(part, trimmed, StringComparison.Ordinal)) yield return part;
    }

    private static bool IsGeneratedInstance(string id) => id.Contains('@') && AlbionLocations.ResolveActivityLocation(id).Kind is
        ActivityMapKind.RandomDungeon or ActivityMapKind.CorruptedDungeon or ActivityMapKind.Hellgate or ActivityMapKind.Expedition
        or ActivityMapKind.Mists or ActivityMapKind.MistsDungeon or ActivityMapKind.AbyssalDepths or ActivityMapKind.DragonArea
        or ActivityMapKind.Arena or ActivityMapKind.Island or ActivityMapKind.Hideout;

    private static MapPoint? Point(double[]? values) =>
        values is { Length: >= 2 } && double.IsFinite(values[0]) && double.IsFinite(values[1]) ? new MapPoint(values[0], values[1]) : null;

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    // "Outpost_Mountain" and "bankVault" read as "Outpost Mountain" and "Bank Vault".
    private static string Humanize(string value)
    {
        var text = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var c = value[index];
            if (c is '_' or '-') { text.Append(' '); continue; }
            if (index > 0 && char.IsUpper(c) && char.IsLetterOrDigit(value[index - 1]) && !char.IsUpper(value[index - 1])) text.Append(' ');
            text.Append(index == 0 ? char.ToUpperInvariant(c) : c);
        }

        return text.ToString().Trim();
    }

    public void Dispose()
    {
        http.Dispose();
        downloads.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class CatalogEntry
    {
        public string? Id { get; set; }
        public string? ImageFile { get; set; }
        public string? DisplayName { get; set; }
        public string? Tier { get; set; }
        public string? MapCategory { get; set; }
        public string? PvpCategory { get; set; }
        public double[]? Origin { get; set; }
        public double[]? Size { get; set; }
        public double[]? MinimapBoundsMin { get; set; }
        public double[]? MinimapBoundsMax { get; set; }
        public List<CatalogExit>? Exits { get; set; }
        public List<CatalogExit>? PortalExits { get; set; }
        public List<CatalogExit>? PortalEntrances { get; set; }
    }

    private sealed class CatalogExit
    {
        public string? TargetLocationId { get; set; }
        public double[]? Position { get; set; }
    }

    private sealed class FeatureFile
    {
        public List<Feature>? Features { get; set; }
    }

    private sealed class Feature
    {
        public string? Type { get; set; }
        public string? MarkerType { get; set; }
        public string? Label { get; set; }
        public double? X { get; set; }
        public double? Z { get; set; }
    }
}

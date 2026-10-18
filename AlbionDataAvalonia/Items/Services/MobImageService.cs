using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.ReferenceData;
using Avalonia.Media.Imaging;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services;

/// <summary>
/// Mob portraits from the AlbionWorld map features on the AFM CDN. mob-avatars.json names each mob's portrait and
/// the ring drawn around it by the mob's unique name; many mobs share one portrait. Images are kept on disk for
/// 30 days, like item icons, and the lookup is cached like the other reference data.
/// </summary>
public sealed class MobImageService : IDisposable
{
    private const string FeaturesBase = "https://cdn.albionfreemarket.com/AlbionWorld/map-features";
    private const string IndexUrl = FeaturesBase + "/mob-avatars.json";
    private const int MaxConcurrentDownloads = 4;
    private static readonly TimeSpan DiskCacheTtl = TimeSpan.FromDays(30);
    // A lookup that could not be loaded is tried again later, so portraits appear once the CDN has them.
    private static readonly TimeSpan IndexRetryDelay = TimeSpan.FromMinutes(10);
    private static readonly Task<MobPortrait?> NoPortrait = Task.FromResult<MobPortrait?>(null);

    private readonly MobsService mobs;
    private readonly HttpClient httpClient = new();
    private readonly SemaphoreSlim downloadSemaphore = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
    private readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<MobPortrait?>>> portraitCache = new(StringComparer.Ordinal);
    private readonly string diskCachePath = Path.Combine(AppData.LocalPath, "cache", "mob-images");
    private readonly object sync = new();
    private Task<MobAvatarIndex?>? index;
    private DateTime indexLoadedAtUtc;

    public MobImageService(MobsService mobs)
    {
        this.mobs = mobs;
    }

    /// <summary>
    /// The mob's portrait with its ring, or null when the mob has none or it can't be loaded. The mob is its unique
    /// name, its text key ("@MOB_" and the unique name) or, for events that only kept it, its English name.
    /// </summary>
    public Task<MobPortrait?> GetPortraitAsync(string? mobUniqueName)
    {
        if (string.IsNullOrWhiteSpace(mobUniqueName)) return NoPortrait;
        var avatars = GetIndexAsync();
        if (avatars.IsCompletedSuccessfully && avatars.Result is null) return NoPortrait;
        return portraitCache.GetOrAdd(mobUniqueName.Trim(),
            static (name, service) => new Lazy<Task<MobPortrait?>>(() => service.LoadPortraitAsync(name)), this).Value;
    }

    public void Dispose()
    {
        downloadSemaphore.Dispose();
        httpClient.Dispose();
    }

    private Task<MobAvatarIndex?> GetIndexAsync()
    {
        lock (sync)
        {
            if (index is null || index.IsCompleted && index.Result is null && DateTime.UtcNow - indexLoadedAtUtc > IndexRetryDelay)
            {
                indexLoadedAtUtc = DateTime.UtcNow;
                index = LoadIndexAsync();
            }
            return index;
        }
    }

    private static async Task<MobAvatarIndex?> LoadIndexAsync()
    {
        try
        {
            var loaded = await ReferenceDataLoader.Shared.LoadAsync(IndexUrl, ParseIndex).ConfigureAwait(false);
            Log.Information("Loaded portraits for {MobCount} mobs.", loaded.Mobs.Count);
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Mob portraits are unavailable.");
            return null;
        }
    }

    private static MobAvatarIndex ParseIndex(string json)
    {
        var file = JsonSerializer.Deserialize<MobAvatarFile>(json) ?? throw new InvalidDataException("Mob portrait lookup is null.");
        var mobs = (file.Mobs ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value?.Avatar))
            .ToDictionary(x => x.Key, x => x.Value!, StringComparer.Ordinal);
        if (mobs.Count == 0) throw new InvalidDataException("Mob portrait lookup lists no mobs.");
        var path = string.IsNullOrWhiteSpace(file.Base) ? "icons/mobs" : file.Base.Trim('/');
        return new MobAvatarIndex($"{FeaturesBase}/{path}", mobs);
    }

    private async Task<MobPortrait?> LoadPortraitAsync(string mobUniqueName)
    {
        var avatars = await GetIndexAsync().ConfigureAwait(false);
        if (avatars is null)
        {
            // Not cached: the lookup is tried again later.
            portraitCache.TryRemove(mobUniqueName, out _);
            return null;
        }

        if (!avatars.Mobs.TryGetValue(mobUniqueName, out var avatar)
            && !(mobUniqueName.StartsWith("@MOB_", StringComparison.Ordinal) && avatars.Mobs.TryGetValue(mobUniqueName[5..], out avatar))
            && !(mobs.FindUniqueName(mobUniqueName) is { } uniqueName && avatars.Mobs.TryGetValue(uniqueName, out avatar)))
        {
            // A name is looked up again once the mob list has loaded.
            if (!mobs.IsLoaded) portraitCache.TryRemove(mobUniqueName, out _);
            return null;
        }

        var face = GetImageAsync(avatars.BaseUrl, avatar.Avatar!);
        var ring = string.IsNullOrWhiteSpace(avatar.Ring) ? Task.FromResult<Bitmap?>(null) : GetImageAsync(avatars.BaseUrl, avatar.Ring);
        await Task.WhenAll(face, ring).ConfigureAwait(false);
        return face.Result is { } image ? MobPortrait.Create(image, ring.Result) : null;
    }

    private Task<Bitmap?> GetImageAsync(string baseUrl, string name) =>
        imageCache.GetOrAdd(name, static (key, state) => new Lazy<Task<Bitmap?>>(() => state.Service.LoadImageAsync(state.BaseUrl, key)),
            (Service: this, BaseUrl: baseUrl)).Value;

    private async Task<Bitmap?> LoadImageAsync(string baseUrl, string name)
    {
        try
        {
            var cachedImage = await TryLoadFreshDiskCacheAsync(name).ConfigureAwait(false);
            if (cachedImage is not null) return cachedImage;

            await downloadSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = await httpClient.GetByteArrayAsync($"{baseUrl}/{Uri.EscapeDataString(name)}.webp").ConfigureAwait(false);
                await SaveDiskCacheAsync(name, bytes).ConfigureAwait(false);
                return CreateBitmap(bytes);
            }
            finally
            {
                downloadSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to load mob portrait image {Name}", name);
            return null;
        }
    }

    private async Task<Bitmap?> TryLoadFreshDiskCacheAsync(string name)
    {
        var cacheFilePath = GetCacheFilePath(name);
        try
        {
            if (!File.Exists(cacheFilePath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFilePath) > DiskCacheTtl) return null;
            var bytes = await File.ReadAllBytesAsync(cacheFilePath).ConfigureAwait(false);
            return CreateBitmap(bytes);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read cached mob portrait image {Name}", name);
            return null;
        }
    }

    private async Task SaveDiskCacheAsync(string name, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(diskCachePath);
            var cacheFilePath = GetCacheFilePath(name);
            var tempFilePath = $"{cacheFilePath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(tempFilePath, bytes).ConfigureAwait(false);
            File.Move(tempFilePath, cacheFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to save cached mob portrait image {Name}", name);
        }
    }

    private string GetCacheFilePath(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return Path.Combine(diskCachePath, new string(name.ToUpperInvariant().Select(c => invalid.Contains(c) ? '_' : c).ToArray()) + ".webp");
    }

    private static Bitmap CreateBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    private sealed record MobAvatarIndex(string BaseUrl, IReadOnlyDictionary<string, MobAvatar> Mobs);

    private sealed class MobAvatarFile
    {
        [JsonPropertyName("base")]
        public string? Base { get; set; }

        [JsonPropertyName("mobs")]
        public Dictionary<string, MobAvatar?>? Mobs { get; set; }
    }

    private sealed class MobAvatar
    {
        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [JsonPropertyName("ring")]
        public string? Ring { get; set; }
    }
}

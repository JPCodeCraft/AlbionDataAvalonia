using Avalonia.Media.Imaging;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services;

public sealed class ItemImageService : IDisposable
{
    private const string AlbionImageBaseDomain = "https://cdn.albionfreemarket.com";
    private const string AlbionImageItemPath = "/item/";
    private const int MaxConcurrentDownloads = 4;
    private static readonly TimeSpan DiskCacheTtl = TimeSpan.FromDays(30);

    private readonly HttpClient httpClient = new();
    private readonly SemaphoreSlim downloadSemaphore = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
    private readonly ConcurrentDictionary<ItemImageKey, Lazy<Task<Bitmap?>>> imageCache = new();
    private readonly string diskCachePath = Path.Combine(AppData.LocalPath, "cache", "item-images");

    public Task<Bitmap?> GetItemImageAsync(string itemUniqueName, int quality)
    {
        if (string.IsNullOrWhiteSpace(itemUniqueName)
            || string.Equals(itemUniqueName, "Unknown Item", StringComparison.OrdinalIgnoreCase)
            || string.Equals(itemUniqueName, "Unset", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        quality = quality <= 0 ? 1 : quality;
        var key = new ItemImageKey(itemUniqueName, quality);
        return imageCache.GetOrAdd(
            key,
            static (cacheKey, service) => new Lazy<Task<Bitmap?>>(() => service.LoadItemImageAsync(cacheKey)),
            this).Value;
    }

    public void Dispose()
    {
        downloadSemaphore.Dispose();
        httpClient.Dispose();
    }

    private async Task<Bitmap?> LoadItemImageAsync(ItemImageKey key)
    {
        try
        {
            var cachedImage = await TryLoadFreshDiskCacheAsync(key);
            if (cachedImage is not null)
            {
                return cachedImage;
            }

            await downloadSemaphore.WaitAsync();
            try
            {
                var url = $"{AlbionImageBaseDomain}{AlbionImageItemPath}{Uri.EscapeDataString(key.ItemUniqueName)}.png?quality={key.Quality}";
                var bytes = await httpClient.GetByteArrayAsync(url);
                await SaveDiskCacheAsync(key, bytes);
                return CreateBitmap(bytes);
            }
            finally
            {
                downloadSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to load Albion item image. ItemUniqueName={ItemUniqueName} Quality={Quality}", key.ItemUniqueName, key.Quality);
            return null;
        }
    }

    private async Task<Bitmap?> TryLoadFreshDiskCacheAsync(ItemImageKey key)
    {
        var cacheFilePath = GetCacheFilePath(key);
        try
        {
            if (!File.Exists(cacheFilePath))
            {
                return null;
            }

            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(cacheFilePath);
            if (DateTime.UtcNow - lastWriteTimeUtc > DiskCacheTtl)
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(cacheFilePath);
            return CreateBitmap(bytes);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to read cached Albion item image. ItemUniqueName={ItemUniqueName} Quality={Quality}", key.ItemUniqueName, key.Quality);
            return null;
        }
    }

    private async Task SaveDiskCacheAsync(ItemImageKey key, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(diskCachePath);
            var cacheFilePath = GetCacheFilePath(key);
            var tempFilePath = $"{cacheFilePath}.{Guid.NewGuid():N}.tmp";

            await File.WriteAllBytesAsync(tempFilePath, bytes);
            File.Move(tempFilePath, cacheFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to save cached Albion item image. ItemUniqueName={ItemUniqueName} Quality={Quality}", key.ItemUniqueName, key.Quality);
        }
    }

    private string GetCacheFilePath(ItemImageKey key)
    {
        return Path.Combine(diskCachePath, $"{GetCacheFileName(key)}.png");
    }

    private static string GetCacheFileName(ItemImageKey key)
    {
        var normalizedKey = $"{key.ItemUniqueName.Trim().ToUpperInvariant()}|{key.Quality}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedKey));
        return Convert.ToHexString(bytes);
    }

    private static Bitmap CreateBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    private readonly record struct ItemImageKey(string ItemUniqueName, int Quality);
}

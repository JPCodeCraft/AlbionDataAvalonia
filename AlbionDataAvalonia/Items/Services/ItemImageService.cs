using Avalonia.Media.Imaging;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services;

public sealed class ItemImageService : IDisposable
{
    private const string AlbionImageBaseDomain = "https://cdn.albionfreemarket.com";
    private const string AlbionImageItemPath = "/item/";

    private readonly HttpClient httpClient = new();
    private readonly ConcurrentDictionary<ItemImageKey, Lazy<Task<Bitmap?>>> imageCache = new();

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
        httpClient.Dispose();
    }

    private async Task<Bitmap?> LoadItemImageAsync(ItemImageKey key)
    {
        try
        {
            var url = $"{AlbionImageBaseDomain}{AlbionImageItemPath}{Uri.EscapeDataString(key.ItemUniqueName)}.png?quality={key.Quality}";
            var bytes = await httpClient.GetByteArrayAsync(url);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to load Albion item image. ItemUniqueName={ItemUniqueName} Quality={Quality}", key.ItemUniqueName, key.Quality);
            return null;
        }
    }

    private readonly record struct ItemImageKey(string ItemUniqueName, int Quality);
}

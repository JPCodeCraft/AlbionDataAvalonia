using System;
using System.Collections.Concurrent;

namespace AlbionDataAvalonia.Items.Services;

public readonly record struct ItemEstimatedMarketValueKey(int ServerId, int ItemId, int Quality);

public sealed class ItemEstimatedMarketValueService
{
    private readonly ConcurrentDictionary<ItemEstimatedMarketValueKey, long> values = new();
    private readonly object updateLock = new();

    public event Action<ItemEstimatedMarketValueKey>? EstimatedMarketValueChanged;

    public void Update(int serverId, int itemId, int quality, long estimatedMarketValue)
    {
        if (serverId <= 0 || itemId <= 0 || quality <= 0 || estimatedMarketValue <= 0)
        {
            return;
        }

        var key = new ItemEstimatedMarketValueKey(serverId, itemId, quality);
        var changed = false;
        lock (updateLock)
        {
            if (!values.TryGetValue(key, out var existing) || existing != estimatedMarketValue)
            {
                values[key] = estimatedMarketValue;
                changed = true;
            }
        }

        if (changed)
        {
            EstimatedMarketValueChanged?.Invoke(key);
        }
    }

    public long? Get(int serverId, int itemId, int quality)
    {
        if (values.TryGetValue(new ItemEstimatedMarketValueKey(serverId, itemId, quality), out var value))
        {
            return value;
        }

        return null;
    }
}

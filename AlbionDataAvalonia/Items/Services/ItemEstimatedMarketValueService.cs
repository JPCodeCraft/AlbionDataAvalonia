using AlbionDataAvalonia.Gathering.Models;
using System;
using System.Collections.Concurrent;

namespace AlbionDataAvalonia.Items.Services;

public sealed class ItemEstimatedMarketValueService
{
    private readonly ConcurrentDictionary<GatheringItemKey, long> values = new();
    private readonly object updateLock = new();

    public event Action<GatheringItemKey>? EstimatedMarketValueChanged;

    public void Update(int itemId, int quality, long estimatedMarketValue)
    {
        if (itemId <= 0 || quality <= 0 || estimatedMarketValue <= 0)
        {
            return;
        }

        var key = new GatheringItemKey(itemId, quality);
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

    public long? Get(int itemId, int quality)
    {
        if (values.TryGetValue(new GatheringItemKey(itemId, quality), out var value))
        {
            return value;
        }

        return null;
    }
}

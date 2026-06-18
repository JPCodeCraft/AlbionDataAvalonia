using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Items.Services;

public readonly record struct ItemEstimatedMarketValueKey(int ServerId, int ItemId, int Quality);
public readonly record struct ItemEstimatedMarketValueUpdate(int ServerId, int ItemId, int Quality, long EstimatedMarketValue);

public sealed class ItemEstimatedMarketValueService
{
    private readonly ConcurrentDictionary<ItemEstimatedMarketValueKey, long> values = new();
    private readonly object updateLock = new();

    public event Action<IReadOnlyCollection<ItemEstimatedMarketValueKey>>? EstimatedMarketValuesChanged;

    public void Update(int serverId, int itemId, int quality, long estimatedMarketValue)
    {
        UpdateMany(new[] { new ItemEstimatedMarketValueUpdate(serverId, itemId, quality, estimatedMarketValue) });
    }

    public void UpdateMany(IEnumerable<ItemEstimatedMarketValueUpdate> updates)
    {
        var changedKeys = new List<ItemEstimatedMarketValueKey>();
        lock (updateLock)
        {
            foreach (var update in updates)
            {
                if (update.ServerId <= 0 || update.ItemId <= 0 || update.Quality <= 0 || update.EstimatedMarketValue <= 0)
                {
                    continue;
                }

                var key = new ItemEstimatedMarketValueKey(update.ServerId, update.ItemId, update.Quality);
                if (!values.TryGetValue(key, out var existing) || existing != update.EstimatedMarketValue)
                {
                    values[key] = update.EstimatedMarketValue;
                    changedKeys.Add(key);
                }
            }
        }

        if (changedKeys.Count > 0)
        {
            EstimatedMarketValuesChanged?.Invoke(changedKeys.Distinct().ToArray());
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

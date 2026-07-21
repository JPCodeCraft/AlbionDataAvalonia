using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Items.Services;

public readonly record struct ItemEstimatedMarketValueKey(int ServerId, int ItemId, int Quality);
public readonly record struct ItemEstimatedMarketValues(long NormalEmv, long? BlackMarketEmv);
public readonly record struct ItemEstimatedMarketValueUpdate(
    int ServerId,
    int ItemId,
    int Quality,
    long NormalEmv,
    long? BlackMarketEmv = null);

public sealed class ItemEstimatedMarketValueService
{
    private readonly ConcurrentDictionary<ItemEstimatedMarketValueKey, ItemEstimatedMarketValues> values = new();
    private readonly object updateLock = new();

    public event Action<IReadOnlyCollection<ItemEstimatedMarketValueKey>>? EstimatedMarketValuesChanged;

    public void Update(int serverId, int itemId, int quality, long normalEmv, long? blackMarketEmv = null)
    {
        UpdateMany(new[] { new ItemEstimatedMarketValueUpdate(serverId, itemId, quality, normalEmv, blackMarketEmv) });
    }

    public void UpdateMany(IEnumerable<ItemEstimatedMarketValueUpdate> updates)
    {
        var changedKeys = new List<ItemEstimatedMarketValueKey>();
        lock (updateLock)
        {
            foreach (var update in updates)
            {
                if (update.ServerId <= 0
                    || update.ItemId <= 0
                    || update.Quality <= 0
                    || update.NormalEmv <= 0
                    || update.BlackMarketEmv is <= 0)
                {
                    continue;
                }

                var key = new ItemEstimatedMarketValueKey(update.ServerId, update.ItemId, update.Quality);
                values.TryGetValue(key, out var existing);
                var value = new ItemEstimatedMarketValues(
                    update.NormalEmv,
                    update.BlackMarketEmv ?? existing.BlackMarketEmv);
                if (existing != value)
                {
                    values[key] = value;
                    changedKeys.Add(key);
                }
            }
        }

        if (changedKeys.Count > 0)
        {
            EstimatedMarketValuesChanged?.Invoke(changedKeys.Distinct().ToArray());
        }
    }

    public ItemEstimatedMarketValues? Get(int serverId, int itemId, int quality)
    {
        if (values.TryGetValue(new ItemEstimatedMarketValueKey(serverId, itemId, quality), out var value))
        {
            return value;
        }

        return null;
    }
}

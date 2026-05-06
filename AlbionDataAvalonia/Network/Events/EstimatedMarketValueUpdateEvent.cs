using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Events;

public class EstimatedMarketValueUpdateEvent : BaseEvent
{
    public IReadOnlyList<EstimatedMarketValueUpdateEntry> Entries { get; } = Array.Empty<EstimatedMarketValueUpdateEntry>();

    public EstimatedMarketValueUpdateEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            var normalItemIds = parameters.TryGetValue(0, out object? normalItemIdsData)
                ? normalItemIdsData.ToIntArray()
                : Array.Empty<int>();
            var normalEstimatedMarketValues = parameters.TryGetValue(1, out object? normalEstimatedMarketValuesData)
                ? normalEstimatedMarketValuesData.ToLongArray().Select(value => value / 10000).ToArray()
                : Array.Empty<long>();

            if (normalItemIds.Length != normalEstimatedMarketValues.Length)
            {
                Log.Warning(
                    "EstimatedMarketValueUpdateEvent received mismatched normal-quality array lengths: itemIds={ItemIdsLength}, estimatedMarketValues={EstimatedMarketValuesLength}.",
                    normalItemIds.Length,
                    normalEstimatedMarketValues.Length);
                return;
            }

            var explicitItemIds = parameters.TryGetValue(2, out object? explicitItemIdsData)
                ? explicitItemIdsData.ToIntArray()
                : Array.Empty<int>();
            var explicitQualities = parameters.TryGetValue(3, out object? explicitQualitiesData)
                ? explicitQualitiesData.ToIntArray()
                : Array.Empty<int>();
            var explicitEstimatedMarketValues = parameters.TryGetValue(4, out object? explicitEstimatedMarketValuesData)
                ? explicitEstimatedMarketValuesData.ToLongArray().Select(value => value / 10000).ToArray()
                : Array.Empty<long>();

            if (explicitItemIds.Length != explicitQualities.Length || explicitItemIds.Length != explicitEstimatedMarketValues.Length)
            {
                Log.Warning(
                    "EstimatedMarketValueUpdateEvent received mismatched explicit-quality array lengths: itemIds={ItemIdsLength}, qualities={QualitiesLength}, estimatedMarketValues={EstimatedMarketValuesLength}.",
                    explicitItemIds.Length,
                    explicitQualities.Length,
                    explicitEstimatedMarketValues.Length);
                return;
            }

            Entries = Enumerable.Range(0, normalItemIds.Length)
                .Select(index => new EstimatedMarketValueUpdateEntry(normalItemIds[index], 1, normalEstimatedMarketValues[index]))
                .Concat(Enumerable.Range(0, explicitItemIds.Length)
                    .Select(index => new EstimatedMarketValueUpdateEntry(explicitItemIds[index], explicitQualities[index], explicitEstimatedMarketValues[index])))
                .ToArray();

            if (Entries.Count == 0)
            {
                Log.Warning("EstimatedMarketValueUpdateEvent received with no entries.");
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    public sealed class EstimatedMarketValueUpdateEntry
    {
        public int ItemId { get; }

        public int Quality { get; }

        public long EstimatedMarketValue { get; }

        public string ItemUniqueName { get; set; } = "Unknown Item";

        public string ItemUsName { get; set; } = "Unknown Item";

        public EstimatedMarketValueUpdateEntry(int itemId, int quality, long estimatedMarketValue)
        {
            ItemId = itemId;
            Quality = quality;
            EstimatedMarketValue = estimatedMarketValue;
        }
    }
}
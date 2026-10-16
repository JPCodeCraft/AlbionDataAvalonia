using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

/// <summary>Requests missing estimates through the existing batched, cached backend loader.</summary>
public sealed class ActivityItemValuationService(ItemEstimatedMarketValueService values,
    ItemEstimatedMarketValueBackendLoader loader, ActivityPersistence persistence)
{
    public ActivityEvent Observe(ActivityEvent value)
    {
        if (value.Kind is not (ActivityEventKind.Loot or ActivityEventKind.Gathered or ActivityEventKind.FishingReward) ||
            value.EstimatedUnitValue is not null || value.ItemId is not > 0 ||
            value.Quality is not null and not (>= 1 and <= 5) ||
            string.IsNullOrWhiteSpace(value.ItemUniqueName) || !int.TryParse(value.Server, out var server)) return value;
        // A filled journal is worth the full journal minus the empty one; its own price would overstate it.
        if (ActivityJournals.IsFull(value.ItemUniqueName)) return value;
        // Pickups without quality use an explicitly labelled estimate without inventing
        // the quality of the actual item: loot averages qualities 1-4, resources use normal quality.
        var quality = BasisQuality(value);
        var estimate = value.Kind == ActivityEventKind.Loot
            ? LootEstimate(values, server, value.ItemId.Value, value.Quality)
            : values.Get(server, value.ItemId.Value, quality)?.NormalEmv;
        if (estimate is > 0)
            return WithPrice(value, estimate.Value, DateTime.UtcNow, quality);
        loader.QueueMissingEstimatedMarketValue(server, value.ItemId.Value, value.ItemUniqueName, quality > 0 ? quality : null);
        return value;
    }

    public void Request(int server, int itemId, string uniqueName, int quality)
    {
        if (values.Get(server, itemId, quality)?.NormalEmv is not > 0)
            loader.QueueMissingEstimatedMarketValue(server, itemId, uniqueName, quality);
    }

    public void RequestMissing(IEnumerable<ActivityEvent> events)
    {
        var prices = events.Where(x => !x.IsRetracted && x.EstimatedUnitValue is null)
            .Select(Observe).Where(x => x.EstimatedUnitValue is > 0)
            .Select(x => new ActivityLootPrice(x.Server, x.ItemId!.Value, BasisQuality(x),
                x.EstimatedUnitValue!.Value, x.PriceObservedAtUtc ?? DateTime.UtcNow, x.Kind == ActivityEventKind.Loot))
            .DistinctBy(x => (x.Server, x.ItemId, x.Quality, x.LootBasis)).ToArray();
        if (prices.Length > 0) _ = FillAsync(prices);
    }

    /// <summary>Quality used for an estimate; 0 marks the loot average over qualities 1-4.</summary>
    internal static int BasisQuality(ActivityEvent value) => value.Quality ?? (value.Kind == ActivityEventKind.Loot ? 0 : 1);

    // Loot keeps the loot log's valuation: Black Market estimates before normal ones,
    // packet estimates before cached ones, and the average of qualities 1-4 when the
    // pickup does not reveal its quality. Resources keep normal-quality estimates.
    internal static long? LootEstimate(ItemEstimatedMarketValueService values, int server, int itemId, int? quality,
        long? packetBlackMarket = null, long? packetNormal = null)
    {
        if (packetBlackMarket is > 0) return packetBlackMarket;
        var cached = quality is >= 1 and <= 5 ? values.Get(server, itemId, quality.Value) : null;
        if (cached?.BlackMarketEmv is > 0) return cached.Value.BlackMarketEmv;
        if (packetNormal is > 0) return packetNormal;
        if (cached?.NormalEmv is > 0) return cached.Value.NormalEmv;
        if (quality is >= 1 and <= 5) return null;
        var known = Enumerable.Range(1, 4).Select(level => values.Get(server, itemId, level))
            .Select(value => value?.BlackMarketEmv ?? value?.NormalEmv).Where(value => value is > 0)
            .Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : (long)Math.Round(known.Average());
    }

    private async Task FillAsync(ActivityLootPrice[] prices)
    {
        try { await persistence.FillMissingLootPricesAsync(prices).ConfigureAwait(false); }
        catch (Exception error) { Log.Warning(error, "Could not fill activity item estimates."); }
    }

    internal static ActivityEvent WithPrice(ActivityEvent value, long price, DateTime at, int quality)
    {
        var details = value.DetailsJson;
        try
        {
            if (JsonNode.Parse(details) is JsonObject detail)
            {
                detail["ValuationQuality"] = quality;
                details = detail.ToJsonString();
            }
        }
        catch (JsonException) { }
        return value with { EstimatedUnitValue = price, PriceObservedAtUtc = at, DetailsJson = details };
    }
}

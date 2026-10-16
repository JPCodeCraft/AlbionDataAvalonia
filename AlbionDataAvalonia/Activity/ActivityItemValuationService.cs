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
        if (value.Kind == ActivityEventKind.Loot)
        {
            if (LootEstimate(values, server, value.ItemId.Value, value.Quality) is { } loot)
                return WithPrice(value, loot.Price, DateTime.UtcNow, quality, loot.BlackMarket);
        }
        else if (values.Get(server, value.ItemId.Value, quality)?.NormalEmv is > 0 and var emv)
            return WithPrice(value, emv, DateTime.UtcNow, quality);
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
                x.EstimatedUnitValue!.Value, x.PriceObservedAtUtc ?? DateTime.UtcNow, x.Kind == ActivityEventKind.Loot, IsBlackMarketEmv(x) == true))
            .DistinctBy(x => (x.Server, x.ItemId, x.Quality, x.LootBasis)).ToArray();
        if (prices.Length > 0) _ = FillAsync(prices);
    }

    /// <summary>Quality used for an estimate; 0 marks the loot average over qualities 1-4.</summary>
    internal static int BasisQuality(ActivityEvent value) => value.Quality ?? (value.Kind == ActivityEventKind.Loot ? 0 : 1);

    // Loot keeps the loot log's valuation: Black Market EMV before EMV, packet values before
    // cached ones, and the average of qualities 1-4 when the pickup does not reveal its quality.
    // The average uses Black Market EMVs when any quality has one, so it is one kind of EMV.
    // Resources keep normal-quality EMV.
    internal static (long Price, bool BlackMarket)? LootEstimate(ItemEstimatedMarketValueService values, int server, int itemId, int? quality,
        long? packetBlackMarket = null, long? packetNormal = null)
    {
        if (packetBlackMarket is > 0) return (packetBlackMarket.Value, true);
        var cached = quality is >= 1 and <= 5 ? values.Get(server, itemId, quality.Value) : null;
        if (cached?.BlackMarketEmv is > 0) return (cached.Value.BlackMarketEmv.Value, true);
        if (packetNormal is > 0) return (packetNormal.Value, false);
        if (cached?.NormalEmv is > 0) return (cached.Value.NormalEmv, false);
        if (quality is >= 1 and <= 5) return null;
        var known = Enumerable.Range(1, 4).Select(level => values.Get(server, itemId, level)).Where(value => value is not null)
            .Select(value => value!.Value).ToArray();
        var blackMarket = known.Where(value => value.BlackMarketEmv is > 0).Select(value => value.BlackMarketEmv!.Value).ToArray();
        if (blackMarket.Length > 0) return ((long)Math.Round(blackMarket.Average()), true);
        var normal = known.Where(value => value.NormalEmv > 0).Select(value => value.NormalEmv).ToArray();
        return normal.Length == 0 ? null : ((long)Math.Round(normal.Average()), false);
    }

    private async Task FillAsync(ActivityLootPrice[] prices)
    {
        try { await persistence.FillMissingLootPricesAsync(prices).ConfigureAwait(false); }
        catch (Exception error) { Log.Warning(error, "Could not fill activity item estimates."); }
    }

    // A loot price records whether it is a Black Market EMV; other prices are always EMV.
    // A null kind clears it, so a copied price keeps exactly what its original recorded.
    internal static ActivityEvent WithPrice(ActivityEvent value, long price, DateTime at, int quality, bool? blackMarket = null)
    {
        var details = value.DetailsJson;
        try
        {
            if (JsonNode.Parse(details) is JsonObject detail)
            {
                detail["ValuationQuality"] = quality;
                if (blackMarket is { } isBlackMarket) detail["BlackMarketEmv"] = isBlackMarket;
                else detail.Remove("BlackMarketEmv");
                details = detail.ToJsonString();
            }
        }
        catch (JsonException) { }
        return value with { EstimatedUnitValue = price, PriceObservedAtUtc = at, DetailsJson = details };
    }

    // Null for prices saved before the kind was recorded.
    internal static bool? IsBlackMarketEmv(ActivityEvent value)
    {
        try { return JsonNode.Parse(value.DetailsJson)?["BlackMarketEmv"]?.GetValue<bool>(); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return null; }
    }
}

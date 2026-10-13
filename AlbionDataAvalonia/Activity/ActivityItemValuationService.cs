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
        // Broadcast pickups omit quality. Use an explicitly labelled normal-quality
        // estimate without inventing the quality of the actual item.
        var quality = value.Quality is >= 1 and <= 5 ? value.Quality.Value : 1;
        var estimate = values.Get(server, value.ItemId.Value, quality)?.NormalEmv;
        if (estimate is > 0)
            return WithPrice(value, estimate.Value, DateTime.UtcNow, quality);
        loader.QueueMissingEstimatedMarketValue(server, value.ItemId.Value, value.ItemUniqueName, quality);
        return value;
    }

    public void RequestMissing(IEnumerable<ActivityEvent> events)
    {
        var prices = events.Where(x => !x.IsRetracted && x.EstimatedUnitValue is null)
            .Select(Observe).Where(x => x.EstimatedUnitValue is > 0)
            .Select(x => new ActivityLootPrice(x.Server, x.ItemId!.Value, x.Quality ?? 1,
                x.EstimatedUnitValue!.Value, x.PriceObservedAtUtc ?? DateTime.UtcNow))
            .DistinctBy(x => (x.Server, x.ItemId, x.Quality)).ToArray();
        if (prices.Length > 0) _ = FillAsync(prices);
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

using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

/// <summary>Requests missing estimates through the existing batched, cached backend loader.</summary>
public sealed class ActivityItemValuationService(ItemEstimatedMarketValueService values,
    ItemEstimatedMarketValueBackendLoader loader, ActivityPersistence persistence, ItemsIdsService items)
{
    private static int lootEmvChoice;

    /// <summary>The EMV new loot is valued at; the tracker keeps it in step with Settings › Trackers.</summary>
    internal static ActivityLootEmv LootEmvChoice
    {
        get => (ActivityLootEmv)Volatile.Read(ref lootEmvChoice);
        set => Volatile.Write(ref lootEmvChoice, (int)value);
    }

    public ActivityEvent Observe(ActivityEvent value)
    {
        if (value.Kind is not (ActivityEventKind.Loot or ActivityEventKind.Gathered or ActivityEventKind.FishingReward or ActivityEventKind.Trashed) ||
            value.EstimatedUnitValue is not null || value.ItemId is not > 0 ||
            value.Quality is not null and not (>= 1 and <= 5) ||
            string.IsNullOrWhiteSpace(value.ItemUniqueName) || !int.TryParse(value.Server, out var server)) return value;
        // A journal filled while gathering is worth the full journal minus the empty one; its own price would overstate it.
        if (value.Kind == ActivityEventKind.Gathered && ActivityJournals.IsFull(value.ItemUniqueName))
            return ObserveJournal(value, server);
        // Pickups without quality use an explicitly labelled estimate without inventing
        // the quality of the actual item: loot averages qualities 1-4, resources use normal quality.
        var quality = BasisQuality(value);
        if (ValuedAs(value) == ActivityEventKind.Loot)
        {
            if (LootEmvs(values, server, value.ItemId.Value, value.Quality) is { } emvs && emvs.Pick(LootEmvChoice) is { } loot)
                return WithPrice(value, loot.Price, DateTime.UtcNow, quality, loot.BlackMarket, emvs);
        }
        else if (values.Get(server, value.ItemId.Value, quality)?.NormalEmv is > 0 and var emv)
            return WithPrice(value, emv, DateTime.UtcNow, quality);
        loader.QueueMissingEstimatedMarketValue(server, value.ItemId.Value, value.ItemUniqueName, quality > 0 ? quality : null);
        return value;
    }

    private ActivityEvent ObserveJournal(ActivityEvent value, int server)
    {
        if (ActivityJournals.MarketPrice(values, items, server, value.ItemId!.Value, DateTime.UtcNow) is { FullJournalValue: { } full, EmptyJournalValue: { } empty } price)
            return ActivityJournals.WithPrice(value, full, empty, price.ObservedAtUtc);
        Request(server, value.ItemId.Value, value.ItemUniqueName!, 1);
        if (ActivityJournals.EmptyUniqueName(value.ItemUniqueName) is { } emptyName && items.GetItemIdByUniqueName(emptyName) is { } emptyId)
            Request(server, emptyId, emptyName, 1);
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
            .Select(Observe).Where(x => x.EstimatedUnitValue is not null)
            .Select(x => x.Kind == ActivityEventKind.Gathered && ActivityJournals.Values(x) is { } journal
                ? new ActivityLootPrice(x.Server, x.ItemId!.Value, 1, x.EstimatedUnitValue!.Value, x.PriceObservedAtUtc ?? DateTime.UtcNow,
                    FullJournalValue: journal.Full, EmptyJournalValue: journal.Empty)
                : new ActivityLootPrice(x.Server, x.ItemId!.Value, BasisQuality(x),
                    x.EstimatedUnitValue!.Value, x.PriceObservedAtUtc ?? DateTime.UtcNow, ValuedAs(x) == ActivityEventKind.Loot, IsBlackMarketEmv(x) == true,
                    Emvs: LootEmvsOf(x)))
            .Where(x => x.UnitValue > 0 || x.FullJournalValue is not null)
            .DistinctBy(x => (x.Server, x.ItemId, x.Quality, x.LootBasis, x.FullJournalValue is not null)).ToArray();
        if (prices.Length > 0) _ = FillAsync(prices);
    }

    /// <summary>Quality used for an estimate; 0 marks the loot average over qualities 1-4.</summary>
    internal static int BasisQuality(ActivityEvent value) => value.Quality ?? (ValuedAs(value) == ActivityEventKind.Loot ? 0 : 1);

    /// <summary>The kind whose valuation an item uses: a trashed item is valued as what it was counted as.</summary>
    internal static ActivityEventKind ValuedAs(ActivityEvent value) => value.Kind != ActivityEventKind.Trashed ? value.Kind
        : Enum.TryParse<ActivityEventKind>(value.LootSourceKind, out var kind) ? kind : ActivityEventKind.Loot;

    // Loot keeps both EMVs and is valued at the chosen one (LootEmvChoice), or the other when the item lacks it:
    // packet values before cached ones, and the averages of qualities 1-4 when the pickup does not reveal its quality.
    // Resources keep normal-quality EMV.
    internal static ActivityLootEmvs? LootEmvs(ItemEstimatedMarketValueService values, int server, int itemId, int? quality,
        long? packetBlackMarket = null, long? packetNormal = null)
    {
        var cached = quality is >= 1 and <= 5 ? values.Get(server, itemId, quality.Value) : null;
        var royal = packetNormal is > 0 ? packetNormal : cached?.NormalEmv is > 0 ? cached.Value.NormalEmv : null;
        var blackMarket = packetBlackMarket is > 0 ? packetBlackMarket : cached?.BlackMarketEmv is > 0 ? cached.Value.BlackMarketEmv : null;
        if (royal is null && blackMarket is null && quality is not (>= 1 and <= 5))
        {
            var known = Enumerable.Range(1, 4).Select(level => values.Get(server, itemId, level)).Where(value => value is not null)
                .Select(value => value!.Value).ToArray();
            royal = Average(known.Where(value => value.NormalEmv > 0).Select(value => value.NormalEmv));
            blackMarket = Average(known.Where(value => value.BlackMarketEmv is > 0).Select(value => value.BlackMarketEmv!.Value));
        }
        return royal is null && blackMarket is null ? null : new ActivityLootEmvs(royal, blackMarket);

        static long? Average(IEnumerable<long> prices) => prices.ToArray() is { Length: > 0 } known ? (long)Math.Round(known.Average()) : null;
    }

    /// <summary>The loot price at the chosen EMV, with both EMVs behind it.</summary>
    internal static (long Price, bool BlackMarket, ActivityLootEmvs Emvs)? LootEstimate(ItemEstimatedMarketValueService values, int server, int itemId,
        int? quality, long? packetBlackMarket = null, long? packetNormal = null) =>
        LootEmvs(values, server, itemId, quality, packetBlackMarket, packetNormal) is { } emvs && emvs.Pick(LootEmvChoice) is { } pick
            ? (pick.Price, pick.BlackMarket, emvs) : null;

    private async Task FillAsync(ActivityLootPrice[] prices)
    {
        try { await persistence.FillMissingLootPricesAsync(prices).ConfigureAwait(false); }
        catch (Exception error) { Log.Warning(error, "Could not fill activity item estimates."); }
    }

    // A loot price records whether it is a Black Market EMV, and both EMVs it was picked from; other prices are always EMV.
    // A null kind or pair clears it, so a copied price keeps exactly what its original recorded.
    internal static ActivityEvent WithPrice(ActivityEvent value, long price, DateTime at, int quality, bool? blackMarket = null,
        ActivityLootEmvs? emvs = null)
    {
        var details = value.DetailsJson;
        try
        {
            if (JsonNode.Parse(details) is JsonObject detail)
            {
                detail["ValuationQuality"] = quality;
                if (blackMarket is { } isBlackMarket) detail["BlackMarketEmv"] = isBlackMarket;
                else detail.Remove("BlackMarketEmv");
                if (emvs is { HasAny: true } pair)
                {
                    var saved = new JsonObject();
                    if (pair.Royal is > 0) saved["Royal"] = pair.Royal.Value;
                    if (pair.BlackMarket is > 0) saved["BlackMarket"] = pair.BlackMarket.Value;
                    detail[LootEmvsKey] = saved;
                }
                else detail.Remove(LootEmvsKey);
                details = detail.ToJsonString();
            }
        }
        catch (JsonException) { }
        return value with { EstimatedUnitValue = price, PriceObservedAtUtc = at, DetailsJson = details };
    }

    internal const string LootEmvsKey = "LootEmvs";
    internal const string RoyalEmvPath = "$.LootEmvs.Royal";
    internal const string BlackMarketEmvPath = "$.LootEmvs.BlackMarket";

    /// <summary>Both EMVs a loot pickup was priced from; null for pickups saved before they were kept.</summary>
    internal static ActivityLootEmvs? LootEmvsOf(ActivityEvent value)
    {
        try
        {
            if (JsonNode.Parse(value.DetailsJson)?[LootEmvsKey] is not JsonObject saved) return null;
            var emvs = new ActivityLootEmvs(Positive(saved["Royal"]), Positive(saved["BlackMarket"]));
            return emvs.HasAny ? emvs : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return null; }

        static long? Positive(JsonNode? node) => node?.GetValue<long>() is > 0 and var price ? price : null;
    }

    // Null for prices saved before the kind was recorded.
    internal static bool? IsBlackMarketEmv(ActivityEvent value)
    {
        try { return JsonNode.Parse(value.DetailsJson)?["BlackMarketEmv"]?.GetValue<bool>(); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return null; }
    }
}

using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlbionDataAvalonia.Activity;

/// <summary>Gathering journals: "T4_JOURNAL_WOOD_EMPTY" fills into "T4_JOURNAL_WOOD_FULL".</summary>
internal static class ActivityJournals
{
    public static bool IsFull(string? uniqueName) => uniqueName is not null &&
        uniqueName.Contains("_JOURNAL_", StringComparison.OrdinalIgnoreCase) &&
        uniqueName.EndsWith("_FULL", StringComparison.OrdinalIgnoreCase);

    public static bool IsEmpty(string? uniqueName) => uniqueName is not null &&
        uniqueName.Contains("_JOURNAL_", StringComparison.OrdinalIgnoreCase) &&
        uniqueName.EndsWith("_EMPTY", StringComparison.OrdinalIgnoreCase);

    public static string? EmptyUniqueName(string? fullUniqueName) =>
        IsFull(fullUniqueName) ? fullUniqueName![..^"_FULL".Length] + "_EMPTY" : null;

    public static string? FullUniqueName(string? emptyUniqueName) =>
        IsEmpty(emptyUniqueName) ? emptyUniqueName![..^"_EMPTY".Length] + "_FULL" : null;

    // A journal filled while gathering is worth the full journal minus the empty one it replaced. Either
    // journal's id finds the pair; the price exists once the EMV cache has both.
    public static ActivityLootPrice? MarketPrice(ItemEstimatedMarketValueService values, ItemsIdsService items, int server, int itemId, DateTime at)
    {
        var name = items.GetItemById(itemId).UniqueName;
        var fullName = IsFull(name) ? name : FullUniqueName(name);
        if (fullName is null || items.GetItemIdByUniqueName(fullName) is not { } fullId ||
            EmptyUniqueName(fullName) is not { } emptyName || items.GetItemIdByUniqueName(emptyName) is not { } emptyId) return null;
        if (values.Get(server, fullId, 1)?.NormalEmv is not { } full || full <= 0 ||
            values.Get(server, emptyId, 1)?.NormalEmv is not { } empty || empty <= 0) return null;
        return new(server.ToString(CultureInfo.InvariantCulture), fullId, 1, Math.Max(0, full - empty), at,
            FullJournalValue: full, EmptyJournalValue: empty);
    }

    public static ActivityEvent WithPrice(ActivityEvent value, long full, long empty, DateTime at)
    {
        var details = value.DetailsJson;
        try
        {
            if (JsonNode.Parse(details) is JsonObject detail)
            {
                detail[nameof(ActivityResourceDetails.FullJournalUnitValue)] = full;
                detail[nameof(ActivityResourceDetails.EmptyJournalUnitValue)] = empty;
                detail[nameof(ActivityResourceDetails.ValueBasis)] = "Market";
                detail["ValuationQuality"] = 1;
                details = detail.ToJsonString();
            }
        }
        catch (JsonException) { }
        return value with { EstimatedUnitValue = Math.Max(0, full - empty), PriceObservedAtUtc = at, DetailsJson = details };
    }

    // A priced filled journal's full and empty unit EMVs.
    public static (long Full, long Empty)? Values(ActivityEvent value)
    {
        if (value.EstimatedUnitValue is null || !IsFull(value.ItemUniqueName)) return null;
        try
        {
            var details = JsonSerializer.Deserialize<ActivityResourceDetails>(value.DetailsJson);
            return details is { FullJournalUnitValue: { } full, EmptyJournalUnitValue: { } empty }
                ? (full, empty) : null;
        }
        catch (JsonException) { return null; }
    }
}

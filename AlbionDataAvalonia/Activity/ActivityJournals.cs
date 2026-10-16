using AlbionDataAvalonia.Activity.Models;
using System;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

/// <summary>Gathering journals: "T4_JOURNAL_WOOD_EMPTY" fills into "T4_JOURNAL_WOOD_FULL".</summary>
internal static class ActivityJournals
{
    public static bool IsFull(string? uniqueName) => uniqueName is not null &&
        uniqueName.Contains("_JOURNAL_", StringComparison.OrdinalIgnoreCase) &&
        uniqueName.EndsWith("_FULL", StringComparison.OrdinalIgnoreCase);

    public static string? EmptyUniqueName(string? fullUniqueName) =>
        IsFull(fullUniqueName) ? fullUniqueName![..^"_FULL".Length] + "_EMPTY" : null;

    // A priced filled journal's full and empty unit values, and whether they are market prices.
    public static (long Full, long Empty, bool Market)? Values(ActivityEvent value)
    {
        if (value.EstimatedUnitValue is null || !IsFull(value.ItemUniqueName)) return null;
        try
        {
            var details = JsonSerializer.Deserialize<ActivityResourceDetails>(value.DetailsJson);
            return details is { FullJournalUnitValue: { } full, EmptyJournalUnitValue: { } empty }
                ? (full, empty, details.ValueBasis == "Market") : null;
        }
        catch (JsonException) { return null; }
    }
}

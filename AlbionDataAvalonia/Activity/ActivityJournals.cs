using System;

namespace AlbionDataAvalonia.Activity;

/// <summary>Gathering journals: "T4_JOURNAL_WOOD_EMPTY" fills into "T4_JOURNAL_WOOD_FULL".</summary>
internal static class ActivityJournals
{
    public static bool IsFull(string? uniqueName) => uniqueName is not null &&
        uniqueName.Contains("_JOURNAL_", StringComparison.OrdinalIgnoreCase) &&
        uniqueName.EndsWith("_FULL", StringComparison.OrdinalIgnoreCase);

    public static string? EmptyUniqueName(string? fullUniqueName) =>
        IsFull(fullUniqueName) ? fullUniqueName![..^"_FULL".Length] + "_EMPTY" : null;
}

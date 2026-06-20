using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public sealed record CleanupCountOption(string Label, DateTime CutoffUtc, int Count)
{
    public override string ToString()
    {
        return $"{Label} ({Count:N0})";
    }
}

public sealed record CleanupPreview(int TotalCount, IReadOnlyList<CleanupCountOption> Options);

public static class CleanupThresholds
{
    public static IReadOnlyList<(string Label, DateTime CutoffUtc)> Create(DateTime nowUtc)
    {
        return
        [
            ("Older than 6 months", nowUtc.AddMonths(-6)),
            ("Older than 3 months", nowUtc.AddMonths(-3)),
            ("Older than 1 month", nowUtc.AddMonths(-1)),
            ("Older than 15 days", nowUtc.AddDays(-15)),
            ("Everything", DateTime.MaxValue)
        ];
    }
}

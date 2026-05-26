using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Gathering.Models;

public readonly record struct GatheringItemKey(int ItemId, int Quality);

public sealed record GatheringSummaryRow(
    int ItemId,
    int Quality,
    string ItemUniqueName,
    string ItemName,
    long Amount,
    long? EstimatedMarketValue,
    long? TotalEstimatedMarketValue,
    double AmountPerMinute,
    double AmountPerHour,
    long? SilverPerHour);

public sealed record GatheringBucketRow(
    DateTime BucketStartedAtUtc,
    long Amount,
    long? TotalEstimatedMarketValue,
    long? SilverPerHour);

public sealed record GatheringTrackerSnapshot(
    bool IsPaused,
    bool HasLocalPlayer,
    bool HasActiveSession,
    DateTime SessionStartedAtUtc,
    TimeSpan ActiveElapsed,
    long TotalAmount,
    long TotalEstimatedMarketValue,
    IReadOnlyList<GatheringSummaryRow> SummaryRows,
    IReadOnlyList<GatheringBucketRow> BucketRows)
{
    public static GatheringTrackerSnapshot Empty() => new(
        false,
        false,
        false,
        DateTime.UtcNow,
        TimeSpan.Zero,
        0,
        0,
        Array.Empty<GatheringSummaryRow>(),
        Array.Empty<GatheringBucketRow>());
}

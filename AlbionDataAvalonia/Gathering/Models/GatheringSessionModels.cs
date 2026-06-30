using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Gathering.Models;

public enum GatheringSessionSource
{
    Unknown = 0,
    Gathering = 1,
    Fishing = 2,
    Mixed = 3
}

[Index(nameof(EndedAtUtc))]
public sealed class GatheringCompletedSession
{
    public Guid Id { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public long ActiveElapsedSeconds { get; set; }
    public long TotalAmount { get; set; }
    public long TotalEstimatedMarketValue { get; set; }
    public long SilverPerHour { get; set; }
    public int? AlbionServerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public GatheringSessionSource Source { get; set; }
    public List<GatheringCompletedSessionItem> Items { get; set; } = new();
}

public sealed class GatheringCompletedSessionItem
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public GatheringCompletedSession? Session { get; set; }
    public int ItemId { get; set; }
    public int Quality { get; set; }
    public string ItemUniqueName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public long Amount { get; set; }
    public long? EstimatedMarketValue { get; set; }
    public long? TotalEstimatedMarketValue { get; set; }
    public GatheringSessionSource Source { get; set; }
}

[Index(nameof(SessionId), IsUnique = true)]
[Index(nameof(UpdatedAtUtc))]
public sealed class GatheringUnfinishedSessionCheckpoint
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsPaused { get; set; }
    public GatheringSessionSource Source { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed record GatheringSessionCheckpoint(
    Guid SessionId,
    DateTime StartedAtUtc,
    DateTime LastActivityAtUtc,
    DateTime UpdatedAtUtc,
    bool IsPaused,
    GatheringSessionSource Source,
    GatheringSessionCheckpointPayload Payload);

public sealed record GatheringSessionCheckpointPayload(
    List<GatheringSessionCheckpointItem> Items,
    List<GatheringSessionCheckpointPauseInterval> PauseIntervals,
    int? AlbionServerId,
    string? PlayerName);

public sealed record GatheringSessionCheckpointItem(
    int ItemId,
    int Quality,
    string ItemUniqueName,
    string ItemName,
    long Amount,
    long? EstimatedMarketValue,
    GatheringSessionSource Source);

public sealed record GatheringSessionCheckpointPauseInterval(
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc);

public sealed record GatheringCompletedSessionSnapshot(
    Guid Id,
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    DateTime LastActivityAtUtc,
    TimeSpan ActiveElapsed,
    long TotalAmount,
    long TotalEstimatedMarketValue,
    long SilverPerHour,
    int? AlbionServerId,
    string PlayerName,
    GatheringSessionSource Source,
    IReadOnlyList<GatheringCompletedSessionItemSnapshot> Items);

public sealed record GatheringCompletedSessionItemSnapshot(
    int ItemId,
    int Quality,
    string ItemUniqueName,
    string ItemName,
    long Amount,
    long? EstimatedMarketValue,
    long? TotalEstimatedMarketValue,
    GatheringSessionSource Source);

public sealed record GatheringCompletedSessionSummary(
    Guid Id,
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    DateTime LastActivityAtUtc,
    TimeSpan ActiveElapsed,
    long TotalAmount,
    long TotalEstimatedMarketValue,
    long SilverPerHour,
    int? AlbionServerId,
    string PlayerName,
    GatheringSessionSource Source);

public sealed record GatheringCompletedSessionDetails(
    GatheringCompletedSessionSummary Summary,
    IReadOnlyList<GatheringCompletedSessionItemSnapshot> Items);

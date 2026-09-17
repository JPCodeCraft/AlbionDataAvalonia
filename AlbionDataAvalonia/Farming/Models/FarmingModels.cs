using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Farming.Models;

public abstract record FarmingContext
{
    public int ServerId { get; init; }
    public string CharacterId { get; init; } = string.Empty;
    public string CharacterName { get; init; } = string.Empty;
    public string IslandId { get; init; } = string.Empty;
}

public sealed record FarmingIslandObservation : FarmingContext
{
    public DateTime ObservedAt { get; init; }
    public string? OwnerName { get; init; }
    public string? IslandName { get; init; }
    public string? HomeCluster { get; init; }
    public string? LayoutId { get; init; }
}

public sealed record FarmingObjectObservation : FarmingContext
{
    public DateTime ObservedAt { get; init; }
    public DateTime? OccupantObservedAt { get; init; }
    public string ObjectId { get; init; } = string.Empty;
    public string UniqueName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    // The building instance owning this slot, not merely its coordinates.
    public string? PlotObjectId { get; init; }
    public double PositionX { get; init; }
    public double PositionY { get; init; }
    public double? Rotation { get; init; }
    public bool Removed { get; init; }
    // A timer expiry hides the plot provisionally; a fresh observation can restore it.
    public bool? RemovalAssumed { get; init; }
    public FarmingObjectState? State { get; init; }
}

public sealed record FarmingObjectState
{
    public double AnimalGrowthSeconds { get; init; }
    public DateTime? AnimalGrowthUpdatedAt { get; init; }
    public double CropGrowthSeconds { get; init; }
    public DateTime? CropGrowthUpdatedAt { get; init; }
    public double? ProductProgressSeconds { get; init; }
    public DateTime? ProductProgressUpdatedAt { get; init; }
    public bool IsMature { get; init; }
    public List<FarmingNutrition> Nutrition { get; init; } = [];
    public double DurationMultiplier { get; init; } = 1;
    // The observing character's status; null for older or pre-Join snapshots.
    public bool? HasPremium { get; init; }
    public DateTime? LastBoostAt { get; init; }
    public int BoostCount { get; init; }

    // Optional confirmed action timings. Null means unknown; deadlines must already
    // account for any game conditions that pause or block the action. See ../README.md.
    public bool? ProductReady { get; init; }
    public DateTime? ProductReadyAt { get; init; }
    public bool? NurtureReady { get; init; }
    public DateTime? NextNurtureAt { get; init; }
}

public sealed record FarmingNutrition
{
    public double Amount { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record FarmingPickup : FarmingContext
{
    public string EventId { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string? SourceObjectId { get; init; }
    public List<FarmingPickupItem> Items { get; init; } = [];
}

public sealed record FarmingPickupItem
{
    public string UniqueName { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

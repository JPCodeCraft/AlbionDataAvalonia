using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Loot.Models;

public enum LootSourceKind
{
    Unknown,
    Mob,
    Player,
    Chest
}

public sealed record LootRecord(
    Guid Id,
    DateTime PickedUpAtUtc,
    string PlayerName,
    bool WasPartyMemberAtPickup,
    LootSourceKind SourceKind,
    string SourceName,
    string LocationName,
    long? ItemObjectId,
    int ItemId,
    string ItemUniqueName,
    string ItemName,
    int Quality,
    int Amount,
    long? EstimatedMarketValue,
    long? TotalEstimatedMarketValue);

public sealed record LootTrackerSnapshot(
    bool IsDisabled,
    bool IsPaused,
    bool HasLocalPlayer,
    IReadOnlyList<LootRecord> Records);

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
    string PlayerAllianceName,
    string PlayerGuildName,
    bool? WasPartyMemberAtPickup,
    LootSourceKind SourceKind,
    string SourceName,
    string SourceAllianceName,
    string SourceGuildName,
    int? ServerId,
    string LocationName,
    string LocationId,
    long? ItemObjectId,
    int ItemId,
    string ItemUniqueName,
    string ItemName,
    int? Quality,
    long Amount,
    long? EstimatedMarketValue,
    long? TotalEstimatedMarketValue);

public sealed record LootTrackerSnapshot(
    bool IsDisabled,
    bool IsPaused,
    bool HasLocalPlayer,
    IReadOnlyList<LootRecord> Records);

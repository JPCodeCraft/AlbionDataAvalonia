using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Legendary.Models;

public enum LegendaryItemLocationKind
{
    Unknown = 0,
    Inventory = 1,
    Bank = 2,
    GuildVault = 3,
    Container = 4
}

[Index(nameof(AlbionServerId), nameof(ObjectId))]
[Index(nameof(AlbionServerId), nameof(SoulId), IsUnique = true)]
[Index(nameof(LastSeenAtUtc))]
public sealed class LegendaryItem
{
    public Guid Id { get; set; }
    public int AlbionServerId { get; set; }
    public long ObjectId { get; set; }
    public Guid? SoulId { get; set; }
    public string? SoulName { get; set; }
    public int? Era { get; set; }
    public long? PvPFameGained { get; set; }
    public long? AttunementSpent { get; set; }
    public int ItemIndex { get; set; }
    public string ItemUniqueName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? CrafterName { get; set; }
    public int Quantity { get; set; }
    public long CurrentDurability { get; set; }
    public long EstimatedMarketValue { get; set; }
    public int Quality { get; set; }
    public bool HasItemDetails { get; set; }
    public bool HasLegendaryDetails { get; set; }
    public string? AttunedToPlayerName { get; set; }
    public long? Attunement { get; set; }
    public double? Strain { get; set; }
    public string SeenByPlayerName { get; set; } = string.Empty;
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public LegendaryItemLocationKind LocationKind { get; set; }
    public string RawLocationId { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public long? ContainerObjectId { get; set; }
    public Guid? ContainerId { get; set; }
    public Guid? PrivateContainerId { get; set; }
    public string ContainerName { get; set; } = string.Empty;
    public string ContainerIcon { get; set; } = string.Empty;
    public int? ContainerColor { get; set; }
    public List<LegendaryItemTrait> Traits { get; set; } = new();
}

[Index(nameof(LegendaryItemId), nameof(Position), IsUnique = true)]
public sealed class LegendaryItemTrait
{
    public Guid Id { get; set; }
    public Guid LegendaryItemId { get; set; }
    public LegendaryItem? LegendaryItem { get; set; }
    public int Position { get; set; }
    public string TraitId { get; set; } = string.Empty;
    public double Value { get; set; }
}

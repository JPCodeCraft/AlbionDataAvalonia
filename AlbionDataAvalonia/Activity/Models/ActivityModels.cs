using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Activity.Models;

public enum ActivityScope { Personal, Party, AllObserved }
public enum ActorRelationship { Unknown, Self, Party, Other }
public enum ActivityActorKind { Unknown, Player, Mob, Environment }
public enum ActivityEvidence { Confirmed, Inferred, Supplemental }
public enum ActivityFameCategory { Unknown = 0, Pve = 1, Gathering = 2, Fishing = 3, Pvp = 4 }
public enum ActivityEventKind
{
    Damage, Healing, Fame, Silver, Loot, MobKilled, PlayerKilled, Death,
    Knockdown, Location, CaptureGap, OutcomeEvidence,
    Gathered, FishingReward, LegacyGatheringAggregate, LegacyImportMarker,
    PartyJoined, PartyLeft
}
[Flags]
public enum ActivityTags { None = 0, Gathering = 1, Pve = 2, Pvp = 4 }
[Flags]
public enum ActivityCategories
{
    None = 0, Gathering = 1, Fishing = 2, Pve = 4, Pvp = 8, Other = 16,
    Loot = 32, Fame = 64, Silver = 128, Outcomes = 256, Combat = 512, All = 1023
}
[Flags]
public enum ActivityItemSources { None = 0, Loot = 1, Gathering = 2, Fishing = 4, LegacyMixed = 8, All = 15 }
public enum ActivityResourceMethod { Unknown, Gathering, Fishing, Mixed }
public enum ActivityState { Open, Closed, Interrupted }
public enum ActivitySessionKind
{
    Roaming, Dungeon, Corrupted, Hellgate, Mists, MistsDungeon, Expedition,
    AbyssalDepths, Arena, DragonArea, StaticDungeon, Unknown
}
public enum ActivityEncounterKind { Unknown, Pve, Pvp, Mixed, Healing }
public enum ActivityEndReason
{
    None, Inactivity, MapTransition, Finished, Disabled, CaptureGap,
    CharacterChanged, Death, Interrupted
}

// One sitting of play. It opens at login, counts from its first run or from Start session, and ends
// at its last activity after 30 minutes without activity (unless started with Start session), End
// session, a character switch, recording turning off, or the app closing without reopening within
// 30 minutes. Runs belong to one.
public sealed record ActivityPlaySession
{
    public Guid Id { get; init; }
    public int Revision { get; init; } = 1;
    public string Server { get; init; } = "";
    public string CharacterId { get; init; } = "";
    public string CharacterName { get; init; } = "";
    public DateTime StartedAtUtc { get; init; }
    public DateTime LastActivityAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; init; }
    public ActivityState State { get; init; }
    public ActivityEndReason EndReason { get; init; }
}

// A run: one dungeon, Mists visit, open-world stretch and so on, inside a play session.
// Immutable snapshots are safe to pass from packet handlers to the persistence worker.
public sealed record ActivitySession
{
    public Guid Id { get; init; }
    public int Revision { get; init; } = 1;
    public Guid? PlaySessionId { get; init; }
    public string Server { get; init; } = "";
    public string CharacterId { get; init; } = "";
    public string CharacterName { get; init; } = "";
    public ActivitySessionKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string RootMapId { get; init; } = "";
    public string LocationName { get; init; } = "";
    public DateTime StartedAtUtc { get; init; }
    public DateTime LastRelevantAtUtc { get; init; }
    public DateTime LastObservedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public DateTime? ObservationStartedAtUtc { get; init; }
    public double ObservedDurationSeconds { get; init; }
    public ActivityState State { get; init; }
    public ActivityEndReason EndReason { get; init; }
    public bool HasLocalDeath { get; init; }
    public bool HasInferredGrouping { get; init; }
    public bool IsPartial { get; init; }
    public string KnownInstancesJson { get; init; } = "[]";
    public string VisitsJson { get; init; } = "[]";
    public string PersonalSummaryJson { get; init; } = "{}";
    public string PartySummaryJson { get; init; } = "{}";
    public string ObservedSummaryJson { get; init; } = "{}";
}

// Roster marks members already present when the session opened rather than a join seen live.
public sealed record ActivityPartyDetails(Guid UserGuid, bool Roster, int PartySize)
{
    public static ActivityPartyDetails? Parse(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<ActivityPartyDetails>(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }
}

public sealed record ActivityVisit(string RawMapId, string LocationName, DateTime StartedAtUtc,
    DateTime? EndedAtUtc, string? InstanceId = null, ActivityMapMetadata? Metadata = null);

// Layout is a Mists instance's fixed master layout, which names its minimap.
public sealed record ActivityMapMetadata(int? Tier = null, string? ZoneType = null,
    string? Mode = null, string? Faction = null, int? Enchantment = null,
    string? Rarity = null, string? OriginMapId = null, string? Layout = null)
{
    public ActivityMapMetadata Merge(ActivityMapMetadata? newer) => newer is null ? this : new(
        newer.Tier ?? Tier, newer.ZoneType ?? ZoneType, newer.Mode ?? Mode,
        newer.Faction ?? Faction, newer.Enchantment ?? Enchantment,
        newer.Rarity ?? Rarity, newer.OriginMapId ?? OriginMapId, newer.Layout ?? Layout);
}

public sealed record ActivityEncounter
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public int Revision { get; init; } = 1;
    public int Number { get; init; }
    public string RawMapId { get; init; } = "";
    public string LocationName { get; init; } = "";
    public DateTime StartedAtUtc { get; init; }
    public DateTime LastCombatAtUtc { get; init; }
    public DateTime LastObservedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public ActivityState State { get; init; }
    public ActivityEndReason EndReason { get; init; }
    public ActivityEncounterKind Kind { get; init; }
    public string PersonalSummaryJson { get; init; } = "{}";
    public string PartySummaryJson { get; init; } = "{}";
    public string ObservedSummaryJson { get; init; } = "{}";
    public double CombatDurationSeconds => Math.Max(1, (LastCombatAtUtc - StartedAtUtc).TotalSeconds);
}

public sealed record ActivityEvent
{
    public Guid Id { get; init; }
    public int Revision { get; init; } = 1;
    public string? OriginKey { get; init; }
    public Guid? SessionId { get; init; }
    public Guid? EncounterId { get; init; }
    public string Server { get; init; } = "";
    public string CharacterId { get; init; } = "";
    public DateTime CapturedAtUtc { get; init; }
    public string CharacterName { get; init; } = "";
    public long CaptureOrder { get; init; }
    public long CaptureGeneration { get; init; }
    public long MapEpoch { get; init; }
    public long? GameTimestamp { get; init; }
    public ActivityEventKind Kind { get; init; }
    public ActivityEvidence Evidence { get; init; }
    public bool IsRetracted { get; init; }
    public string RawMapId { get; init; } = "";
    public string LocationName { get; init; } = "";
    public string MapKind { get; init; } = "";
    public string? InstanceId { get; init; }
    public string? ParentMapId { get; init; }
    public string? SourceActorId { get; init; }
    public string SourceName { get; init; } = "";
    public string SourceGuild { get; init; } = "";
    public string SourceAlliance { get; init; } = "";
    public ActivityActorKind SourceKind { get; init; }
    public ActorRelationship SourceRelationship { get; init; }
    public string? TargetActorId { get; init; }
    public string TargetName { get; init; } = "";
    public string TargetGuild { get; init; } = "";
    public string TargetAlliance { get; init; } = "";
    public ActivityActorKind TargetKind { get; init; }
    public ActorRelationship TargetRelationship { get; init; }
    // Amount is a positive reported damage/healing/fame magnitude, never a rounded UI value.
    public double Amount { get; init; }
    public long SilverRaw { get; init; }
    public bool HasKnownNetSilver { get; init; }
    public bool ParticipatedSelf { get; init; }
    public bool ParticipatedParty { get; init; }
    public string? MobUniqueName { get; init; }
    public int? MobIndex { get; init; }
    public string? ItemUniqueName { get; init; }
    public string ItemName { get; init; } = "";
    public int? ItemId { get; init; }
    public int? Quality { get; init; }
    public long Quantity { get; init; }
    public long? EstimatedUnitValue { get; init; }
    public DateTime? PriceObservedAtUtc { get; init; }
    public string LootSourceName { get; init; } = "";
    public string LootSourceKind { get; init; } = "";
    public int PayloadVersion { get; init; } = 1;
    public string DetailsJson { get; init; } = "{}";
    // Where the local character stood on RawMapId, for kills, deaths, pickups, catches and map entries.
    public double? PositionX { get; init; }
    public double? PositionY { get; init; }
}

public sealed record ActivitySummary
{
    public int TagVersion { get; init; }
    public long GatheringEvents { get; init; }
    public long PveEvents { get; init; }
    public long PvpEvents { get; init; }
    public long GatheringItems { get; init; }
    public long FishingItems { get; init; }
    public long UnclassifiedResourceItems { get; init; }
    public long GatheringValue { get; init; }
    public long FishingValue { get; init; }
    public long UnclassifiedResourceValue { get; init; }
    public long MissingResourcePriceItems { get; init; }
    public long LegacyAggregateEvents { get; init; }
    public double DamageDealt { get; init; }
    public double DamageReceived { get; init; }
    public double HealingDone { get; init; }
    public double HealingReceived { get; init; }
    public double Fame { get; init; }
    public double PveFame { get; init; }
    public double GatheringFame { get; init; }
    public double FishingFame { get; init; }
    public double PvpFame { get; init; }
    public double? UnknownFameAmount { get; init; }
    public long SilverRaw { get; init; }
    public long UnknownSilverEvents { get; init; }
    public long LootValue { get; init; }
    public long LootItems { get; init; }
    public long MissingPriceItems { get; init; }
    public long MobKills { get; init; }
    public long PlayerKills { get; init; }
    public long Deaths { get; init; }
    public long Knockdowns { get; init; }
    // Included in PlayerKills and Deaths: deaths reported without the lethal flag, such as in arenas.
    public long NonLethalPlayerKills { get; init; }
    public long NonLethalDeaths { get; init; }
    public double Silver => SilverRaw / 10000d;
    public double CombatFame => PveFame + PvpFame;
    public double UnclassifiedFame => Math.Max(0, UnknownFameAmount ?? Fame - PveFame - GatheringFame - FishingFame - PvpFame);
    public long MissingLootPriceCount => MissingPriceItems;
    public long UnknownSilverCount => UnknownSilverEvents;
    public long ResourceItems => GatheringItems + FishingItems + UnclassifiedResourceItems;
    public long ResourceValue => GatheringValue + FishingValue + UnclassifiedResourceValue;
    public bool IsLegacyAggregate => LegacyAggregateEvents > 0;
    public ActivityTags Tags => (GatheringEvents > 0 ? ActivityTags.Gathering : ActivityTags.None) |
        (PveEvents > 0 ? ActivityTags.Pve : ActivityTags.None) | (PvpEvents > 0 ? ActivityTags.Pvp : ActivityTags.None);
}

public sealed record ActivityQuery
{
    public Guid? PlaySessionId { get; init; }
    public Guid? SessionId { get; init; }
    public Guid? EncounterId { get; init; }
    public ActivityScope Scope { get; init; } = ActivityScope.Personal;
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? Server { get; init; }
    public string? CharacterId { get; init; }
    public string? CharacterName { get; init; }
    public bool MatchCharacterNameExactly { get; init; }
    public string? ActorKey { get; init; }
    public string? Location { get; init; }
    public ActivitySessionKind? SessionKind { get; init; }
    public ActivityEventKind? Kind { get; init; }
    public string? Search { get; init; }
    public bool LootOnly { get; init; }
    public bool ItemsOnly { get; init; }
    public ActivityItemSources ItemSources { get; init; } = ActivityItemSources.All;
    public ActivityTags ContainsTags { get; init; }
    public ActivityCategories ShowCategories { get; init; } = ActivityCategories.All;
}

public sealed record ActivityPage<T>(IReadOnlyList<T> Items, bool HasMore);
public enum ActivityChartMetric
{
    DamageDealt, DamageReceived, HealingDone, HealingReceived, Fame, Silver, LootValue,
    GatheringItems, FishingItems, ResourceItems, ResourceValue,
    CombatFame, PveFame, GatheringFame, FishingFame, PvpFame, ResourceFame, UnclassifiedFame
}
public sealed record ActivityActorTotal(string ActorId, string Name, ActorRelationship Relationship,
    double DamageDealt, double DamageReceived, double HealingDone, double HealingReceived,
    double Fame = 0, long SilverRaw = 0, long LootItems = 0, long LootValue = 0,
    long GatheredItems = 0, long GatheredValue = 0, long Deaths = 0, long NonLethalDeaths = 0);
public sealed record ActivityMobTotal(string MobId, string Name, long Kills, long InferredKills = 0);
public sealed record ActivityRecordingStatus(bool IsSuspended, int PendingChanges, string? Error);
// LootBasis prices value loot only; quality 0 then fills loot whose quality is unknown.
// A journal price fills journals filled while gathering, and carries the full and empty EMVs behind it.
public sealed record ActivityLootPrice(string Server, int ItemId, int Quality, long UnitValue, DateTime ObservedAtUtc,
    bool LootBasis = false, bool BlackMarket = false, long? FullJournalValue = null, long? EmptyJournalValue = null);
public sealed record ActivityRates(double DurationSeconds, double DamagePerSecond,
    double HealingPerSecond, double FamePerHour, double SilverPerHour, double LootValuePerHour,
    double DamageReceivedPerSecond, double HealingReceivedPerSecond, double CombatDurationSeconds = 0,
    double ResourceItemsPerHour = 0, double ResourceValuePerHour = 0);

// Filled journals also total the full and empty journal EMVs behind their known value.
public sealed record ActivityResourceTotal(int? ItemId, int? Quality, string? UniqueName, string Name,
    ActivityResourceMethod Source, long Quantity, long KnownValue, long MissingPriceItems,
    long FullJournalValue = 0, long EmptyJournalValue = 0);
public sealed record ActivityImportResult(int ImportedSessions, int SkippedSessions, IReadOnlyList<string> Errors);
public sealed record ActivityResourceDetails
{
    public long? ResourceObjectId { get; init; }
    public long? StandardAmount { get; init; }
    public long? GatheringBonusAmount { get; init; }
    public long? PremiumBonusAmount { get; init; }
    public string? FishingAttemptId { get; init; }
    public long? InventoryItemObjectId { get; init; }
    public string? Proof { get; init; }
    public DateTime? FinishAtUtc { get; init; }
    public string? AcquisitionOriginKey { get; init; }
    public string Timing { get; init; } = "Exact";
    public ActivityResourceMethod Method { get; init; }
    public string? ResourceFamily { get; init; }
    public int? Tier { get; init; }
    public int? Enchantment { get; init; }
    public IReadOnlyDictionary<string, string>? ComponentStates { get; init; }
    public ActivityLegacyImportInfo? Legacy { get; init; }
    // A filled journal's value: the full and empty journal EMVs, and whether they came from the
    // EMV cache ("Market") or the game's item packets ("GameValue").
    public long? FullJournalUnitValue { get; init; }
    public long? EmptyJournalUnitValue { get; init; }
    public string? ValueBasis { get; init; }
}
public sealed record ActivityLegacyImportInfo
{
    public Guid SourceSessionId { get; init; }
    public Guid? SourceItemId { get; init; }
    public Guid? CheckpointId { get; init; }
    public int? CheckpointItemIndex { get; init; }
    public long? RecordedTotalValue { get; init; }
    public long OriginalTotalAmount { get; init; }
    public long OriginalTotalValue { get; init; }
    public long OriginalSilverPerHour { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime EndedAtUtc { get; init; }
    public bool IsCheckpoint { get; init; }
    public bool HasMismatch { get; init; }
    public string Coverage { get; init; } = "Session totals only; event times, location, character GUID, combat, fame and cash were not recorded.";
}

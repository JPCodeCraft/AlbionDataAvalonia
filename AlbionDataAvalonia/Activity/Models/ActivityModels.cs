using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Activity.Models;

public enum ActivityScope { Personal, Party, AllObserved }
public enum ActorRelationship { Unknown, Self, Party, Other }
public enum ActivityActorKind { Unknown, Player, Mob, Environment }
public enum ActivityEvidence { Confirmed, Inferred, Supplemental }
public enum ActivityEventKind
{
    Damage, Healing, Fame, Silver, Loot, MobKilled, PlayerKilled, Death,
    Knockdown, Location, CaptureGap, OutcomeEvidence
}
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

// Immutable snapshots are safe to pass from packet handlers to the persistence worker.
public sealed record ActivitySession
{
    public Guid Id { get; init; }
    public int Revision { get; init; } = 1;
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

public sealed record ActivityVisit(string RawMapId, string LocationName, DateTime StartedAtUtc,
    DateTime? EndedAtUtc, string? InstanceId = null);

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
}

public sealed record ActivitySummary
{
    public double DamageDealt { get; init; }
    public double DamageReceived { get; init; }
    public double HealingDone { get; init; }
    public double HealingReceived { get; init; }
    public double Fame { get; init; }
    public long SilverRaw { get; init; }
    public long UnknownSilverEvents { get; init; }
    public long LootValue { get; init; }
    public long LootItems { get; init; }
    public long MissingPriceItems { get; init; }
    public long MobKills { get; init; }
    public long PlayerKills { get; init; }
    public long Deaths { get; init; }
    public long Knockdowns { get; init; }
    public double Silver => SilverRaw / 10000d;
    public double UnclassifiedFame => Fame;
    public long MissingLootPriceCount => MissingPriceItems;
    public long UnknownSilverCount => UnknownSilverEvents;
}

public sealed record ActivityQuery
{
    public Guid? SessionId { get; init; }
    public Guid? EncounterId { get; init; }
    public ActivityScope Scope { get; init; } = ActivityScope.Personal;
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? Server { get; init; }
    public string? CharacterId { get; init; }
    public string? CharacterName { get; init; }
    public string? ActorKey { get; init; }
    public string? Location { get; init; }
    public ActivitySessionKind? SessionKind { get; init; }
    public ActivityEventKind? Kind { get; init; }
    public string? Search { get; init; }
    public bool LootOnly { get; init; }
}

public sealed record ActivityPage<T>(IReadOnlyList<T> Items, bool HasMore);
public enum ActivityChartMetric { DamageDealt, DamageReceived, HealingDone, HealingReceived, Fame, Silver, LootValue }
public sealed record ActivityChartPoint(DateTime CapturedAtUtc, double Value);
public sealed record ActivityActorTotal(string ActorId, string Name, ActorRelationship Relationship,
    double DamageDealt, double DamageReceived, double HealingDone, double HealingReceived);
public sealed record ActivityMobTotal(string MobId, string Name, long Kills, long InferredKills = 0);
public sealed record ActivityRecordingStatus(bool IsSuspended, int PendingChanges, string? Error);
public sealed record ActivityLootPrice(string Server, int ItemId, int Quality, long UnitValue, DateTime ObservedAtUtc);
public sealed record ActivityRates(double DurationSeconds, double DamagePerSecond,
    double HealingPerSecond, double FamePerHour, double SilverPerHour, double LootValuePerHour,
    double DamageReceivedPerSecond, double HealingReceivedPerSecond, double CombatDurationSeconds = 0);

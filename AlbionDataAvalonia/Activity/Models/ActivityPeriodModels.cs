using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Activity.Models;

public enum ActivityPeriodMode { CurrentSession, SinceAppStarted, LastHours }

public sealed record ActivityOwner(string Server, string CharacterId, string CharacterName);
public sealed record ActivityCurrentContext(ActivityOwner? Owner, ActivitySession? Session, ActivityEncounter? Encounter);
public sealed record ActivityObservationInterval(DateTime StartedAtUtc, DateTime? EndedAtUtc);
public sealed record ActivityCombatInterval(Guid EncounterId, DateTime StartedAtUtc, DateTime EndedAtUtc);

// All period totals and interval metadata describe one persistence snapshot.
public sealed record ActivityPeriodSnapshot(long Version, ActivitySummary Summary,
    IReadOnlyList<ActivityResourceTotal> Resources, IReadOnlyList<ActivityObservationInterval> ObservationIntervals,
    IReadOnlyList<ActivityCombatInterval> CombatIntervals, DateTime? NextEventExpiryUtc,
    bool IsPartial, bool HasExcludedImports);

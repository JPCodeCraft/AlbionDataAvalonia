using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Activity.Models;

public enum ActivityPeriodMode { PlaySession, CurrentRun, LastHours }

public sealed record ActivityOwner(string Server, string CharacterId, string CharacterName);
// Session is the open run. PlaySession is null while no play session is running.
public sealed record ActivityCurrentContext(ActivityOwner? Owner, ActivitySession? Session, ActivityEncounter? Encounter,
    ActivityPlaySession? PlaySession = null);
// RunId groups a run's visits, so play time adds only the gaps between runs, not capture gaps inside one.
public sealed record ActivityObservationInterval(DateTime StartedAtUtc, DateTime? EndedAtUtc, Guid? RunId = null);
public sealed record ActivityCombatInterval(Guid EncounterId, DateTime StartedAtUtc, DateTime EndedAtUtc);

// All period totals and interval metadata describe one persistence snapshot.
public sealed record ActivityPeriodSnapshot(long Version, ActivitySummary Summary,
    IReadOnlyList<ActivityResourceTotal> Resources, IReadOnlyList<ActivityObservationInterval> ObservationIntervals,
    IReadOnlyList<ActivityCombatInterval> CombatIntervals, DateTime? NextEventExpiryUtc,
    bool IsPartial, bool HasExcludedImports);

using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Combat.Models;

public enum CombatChangeKind
{
    Damage,
    Healing
}

public enum CombatEntityRole
{
    PartyPlayer = 0,
    Player = 1,
    Mob = 2,
    Unknown = 3
}

public sealed record CombatEntitySnapshot(
    string EntityKey,
    long? ObjectId,
    Guid? Guid,
    string Name,
    CombatEntityRole Role,
    bool IsPartyMember,
    bool IsLocalPlayer);

public sealed record CombatHealthEvent(
    long SourceObjectId,
    long TargetObjectId,
    long Amount,
    CombatChangeKind Kind,
    double NewHealthValue,
    long? GameTimeMilliseconds)
{
    public static bool TryCreate(
        long sourceObjectId,
        long targetObjectId,
        double healthChange,
        double newHealthValue,
        long? gameTimeMilliseconds,
        out CombatHealthEvent healthEvent)
    {
        healthEvent = default!;
        if (healthChange == 0)
        {
            return false;
        }

        var amount = (long)Math.Round(Math.Abs(healthChange), MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            return false;
        }

        healthEvent = new CombatHealthEvent(
            sourceObjectId,
            targetObjectId,
            amount,
            healthChange < 0 ? CombatChangeKind.Damage : CombatChangeKind.Healing,
            newHealthValue,
            gameTimeMilliseconds);
        return true;
    }
}

public sealed record CombatPlayerSummary(
    string EntityKey,
    string Name,
    CombatEntityRole Role,
    long DamageDealt,
    long DamageReceived,
    long HealingDone,
    long HealingReceived);

public sealed record CombatTimeBucketPoint(
    int Index,
    TimeSpan StartOffset,
    TimeSpan EndOffset,
    long DamageDealt,
    long DamageReceived,
    long HealingDone,
    long HealingReceived,
    IReadOnlyList<CombatParticipantBucketTotals> PlayerTotals);

public sealed record CombatParticipantBucketTotals(
    string EntityKey,
    long DamageDealt,
    long DamageReceived,
    long HealingDone,
    long HealingReceived);

public sealed record CombatEncounterSnapshot(
    string EncounterKey,
    int EncounterNumber,
    bool IsActive,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    TimeSpan Elapsed,
    long TotalDamageDealt,
    long TotalDamageReceived,
    long TotalHealingDone,
    long TotalHealingReceived,
    IReadOnlyList<CombatPlayerSummary> Players,
    IReadOnlyList<CombatTimeBucketPoint> TimeBuckets);

public sealed record CombatTrackerSnapshot(
    bool IsEncounterActive,
    bool IsPaused,
    DateTime? EncounterStartedAtUtc,
    DateTime? EncounterEndedAtUtc,
    TimeSpan Elapsed,
    int PartyMemberCount,
    bool HasLocalPlayer,
    CombatEntitySnapshot? LocalPlayer,
    IReadOnlyList<CombatEntitySnapshot> TrackedEntities,
    IReadOnlyList<CombatEncounterSnapshot> Encounters)
{
    public static CombatTrackerSnapshot Empty() => new(
        false,
        false,
        null,
        null,
        TimeSpan.Zero,
        0,
        false,
        null,
        Array.Empty<CombatEntitySnapshot>(),
        Array.Empty<CombatEncounterSnapshot>());
}

public sealed record CombatTrackerStatistics(
    int RetainedEncounterCount,
    int RetentionLimit,
    int KnownEntityCount,
    int TimeBucketCount,
    int ParticipantTotalCount,
    long EstimatedHistoryBytes);

public sealed class CombatParticipantTotals
{
    public long DamageDealt { get; set; }
    public long DamageReceived { get; set; }
    public long HealingDone { get; set; }
    public long HealingReceived { get; set; }

    public void Add(CombatParticipantTotals other)
    {
        DamageDealt += other.DamageDealt;
        DamageReceived += other.DamageReceived;
        HealingDone += other.HealingDone;
        HealingReceived += other.HealingReceived;
    }
}

public sealed class CombatTimeBucket
{
    public CombatTimeBucket(int index, TimeSpan startOffset, TimeSpan endOffset)
    {
        Index = index;
        StartOffset = startOffset;
        EndOffset = endOffset;
    }

    public int Index { get; }
    public TimeSpan StartOffset { get; }
    public TimeSpan EndOffset { get; }
    public Dictionary<string, CombatParticipantTotals> PlayerTotals { get; } = new();

    public void AddDamage(string sourceKey, string targetKey, long amount)
    {
        GetTotals(sourceKey).DamageDealt += amount;
        GetTotals(targetKey).DamageReceived += amount;
    }

    public void AddHealing(string sourceKey, string targetKey, long amount)
    {
        GetTotals(sourceKey).HealingDone += amount;
        GetTotals(targetKey).HealingReceived += amount;
    }

    private CombatParticipantTotals GetTotals(string entityKey)
    {
        if (!PlayerTotals.TryGetValue(entityKey, out var totals))
        {
            totals = new CombatParticipantTotals();
            PlayerTotals[entityKey] = totals;
        }

        return totals;
    }
}

public static class CombatTimeBucketExtensions
{
    public static Dictionary<string, CombatParticipantTotals> FoldTotals(this IEnumerable<CombatTimeBucket> buckets)
    {
        var totals = new Dictionary<string, CombatParticipantTotals>();

        foreach (var bucket in buckets)
        {
            foreach (var (entityKey, bucketTotals) in bucket.PlayerTotals)
            {
                if (!totals.TryGetValue(entityKey, out var aggregate))
                {
                    aggregate = new CombatParticipantTotals();
                    totals[entityKey] = aggregate;
                }

                aggregate.Add(bucketTotals);
            }
        }

        return totals;
    }

    public static long TotalDamageDealt(this CombatTimeBucket bucket) => bucket.PlayerTotals.Values.Sum(x => x.DamageDealt);
    public static long TotalDamageReceived(this CombatTimeBucket bucket) => bucket.PlayerTotals.Values.Sum(x => x.DamageReceived);
    public static long TotalHealingDone(this CombatTimeBucket bucket) => bucket.PlayerTotals.Values.Sum(x => x.HealingDone);
    public static long TotalHealingReceived(this CombatTimeBucket bucket) => bucket.PlayerTotals.Values.Sum(x => x.HealingReceived);
}

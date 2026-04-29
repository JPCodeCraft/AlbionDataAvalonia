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
    int? SpellId,
    double NewHealthValue,
    long? GameTimeMilliseconds)
{
    public static bool TryCreate(
        long sourceObjectId,
        long targetObjectId,
        double healthChange,
        double newHealthValue,
        int causingSpellIndex,
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
            causingSpellIndex > 0 ? causingSpellIndex : null,
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

public sealed record CombatBreakdownRow(
    string PlayerEntityKey,
    string PlayerName,
    string OtherEntityKey,
    string OtherName,
    string SpellKey,
    string SpellLabel,
    long Amount);

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
    IReadOnlyList<CombatBreakdownRow> DamageDealt,
    IReadOnlyList<CombatBreakdownRow> DamageReceived,
    IReadOnlyList<CombatBreakdownRow> HealingDone,
    IReadOnlyList<CombatBreakdownRow> HealingReceived,
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
    public Dictionary<string, Dictionary<string, Dictionary<string, long>>> DamageDealt { get; } = new();
    public Dictionary<string, Dictionary<string, Dictionary<string, long>>> DamageReceived { get; } = new();
    public Dictionary<string, Dictionary<string, Dictionary<string, long>>> HealingDone { get; } = new();
    public Dictionary<string, Dictionary<string, Dictionary<string, long>>> HealingReceived { get; } = new();

    public void AddDamage(string sourceKey, string targetKey, string spellKey, long amount)
    {
        GetTotals(sourceKey).DamageDealt += amount;
        GetTotals(targetKey).DamageReceived += amount;
        AddBreakdown(DamageDealt, sourceKey, targetKey, spellKey, amount);
        AddBreakdown(DamageReceived, targetKey, sourceKey, spellKey, amount);
    }

    public void AddHealing(string sourceKey, string targetKey, string spellKey, long amount)
    {
        GetTotals(sourceKey).HealingDone += amount;
        GetTotals(targetKey).HealingReceived += amount;
        AddBreakdown(HealingDone, sourceKey, targetKey, spellKey, amount);
        AddBreakdown(HealingReceived, targetKey, sourceKey, spellKey, amount);
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

    private static void AddBreakdown(
        Dictionary<string, Dictionary<string, Dictionary<string, long>>> root,
        string playerKey,
        string otherKey,
        string spellKey,
        long amount)
    {
        if (!root.TryGetValue(playerKey, out var byOther))
        {
            byOther = new Dictionary<string, Dictionary<string, long>>();
            root[playerKey] = byOther;
        }

        if (!byOther.TryGetValue(otherKey, out var bySpell))
        {
            bySpell = new Dictionary<string, long>();
            byOther[otherKey] = bySpell;
        }

        bySpell.TryGetValue(spellKey, out var current);
        bySpell[spellKey] = current + amount;
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

    public static Dictionary<string, Dictionary<string, Dictionary<string, long>>> FoldBreakdowns(
        this IEnumerable<CombatTimeBucket> buckets,
        Func<CombatTimeBucket, Dictionary<string, Dictionary<string, Dictionary<string, long>>>> selector)
    {
        var totals = new Dictionary<string, Dictionary<string, Dictionary<string, long>>>();

        foreach (var bucket in buckets)
        {
            foreach (var (playerKey, byOther) in selector(bucket))
            {
                foreach (var (otherKey, bySpell) in byOther)
                {
                    foreach (var (spellKey, amount) in bySpell)
                    {
                        Add(totals, playerKey, otherKey, spellKey, amount);
                    }
                }
            }
        }

        return totals;
    }

    private static void Add(
        Dictionary<string, Dictionary<string, Dictionary<string, long>>> root,
        string playerKey,
        string otherKey,
        string spellKey,
        long amount)
    {
        if (!root.TryGetValue(playerKey, out var byOther))
        {
            byOther = new Dictionary<string, Dictionary<string, long>>();
            root[playerKey] = byOther;
        }

        if (!byOther.TryGetValue(otherKey, out var bySpell))
        {
            bySpell = new Dictionary<string, long>();
            byOther[otherKey] = bySpell;
        }

        bySpell.TryGetValue(spellKey, out var current);
        bySpell[spellKey] = current + amount;
    }

    public static long TotalDamageDealt(this CombatTimeBucket bucket) => bucket.PlayerTotals.Values.Sum(x => x.DamageDealt);
    public static long TotalDamageReceived(this CombatTimeBucket bucket) => bucket.PlayerTotals.Values.Sum(x => x.DamageReceived);
    public static long TotalHealingDone(this CombatTimeBucket bucket) => bucket.PlayerTotals.Values.Sum(x => x.HealingDone);
    public static long TotalHealingReceived(this CombatTimeBucket bucket) => bucket.PlayerTotals.Values.Sum(x => x.HealingReceived);
}

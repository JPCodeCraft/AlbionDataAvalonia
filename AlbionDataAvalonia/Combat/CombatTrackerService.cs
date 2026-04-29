using AlbionDataAvalonia.Combat.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Combat;

public sealed class CombatTrackerService
{
    public static readonly TimeSpan FixedBucketSize = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GameTimeBackwardResetThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GameTimeReceiveDriftResetThreshold = TimeSpan.FromSeconds(30);

    private readonly object sync = new();
    private readonly Dictionary<string, CombatTrackedEntity> entitiesByKey = new();
    private readonly Dictionary<long, CombatTrackedEntity> entitiesByObjectId = new();
    private readonly Dictionary<Guid, CombatTrackedEntity> entitiesByGuid = new();
    private readonly List<CombatEncounter> encounters = new();
    private readonly Dictionary<string, bool> combatStates = new();

    private CombatTrackedEntity? localEntity;
    private CombatEncounter? activeEncounter;
    private int nextEncounterNumber = 1;
    private long? anchorGameTimeMilliseconds;
    private DateTime? anchorUtc;
    private bool anchorFromTimeSync;

    public event Action<CombatTrackerSnapshot>? SnapshotChanged;

    public CombatTrackerSnapshot CurrentSnapshot
    {
        get
        {
            lock (sync)
            {
                return BuildSnapshot(DateTime.UtcNow);
            }
        }
    }

    public void SetLocalPlayer(long objectId, Guid? guid, string name)
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            if (objectId <= 0)
            {
                if (localEntity is not null)
                {
                    localEntity.IsLocalPlayer = false;
                    localEntity.IsPartyMember = false;
                }

                localEntity = null;
            }
            else
            {
                localEntity = AddOrUpdateEntity(objectId, guid, name, CombatEntityKind.Player);
                localEntity.IsLocalPlayer = true;
                localEntity.IsPartyMember = true;
            }

            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void AddOrUpdatePlayer(long? objectId, Guid? guid, string name)
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            AddOrUpdateEntity(objectId, guid, name, CombatEntityKind.Player);
            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void AddOrUpdateMob(long objectId, int? mobIndex, string? mobName = null)
    {
        if (objectId <= 0)
        {
            return;
        }

        CombatTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (entitiesByObjectId.TryGetValue(objectId, out var existingEntity)
                && (existingEntity.IsLocalPlayer || existingEntity.IsPartyMember))
            {
                return;
            }

            var previousName = existingEntity?.Name;
            var previousRole = existingEntity is null ? CombatEntityRole.Unknown : GetEntityRole(existingEntity);
            var entity = AddOrUpdateEntity(objectId, null, GetMobName(mobIndex, mobName), CombatEntityKind.Mob);
            if (!string.Equals(previousName, entity.Name, StringComparison.Ordinal) || previousRole != GetEntityRole(entity))
            {
                snapshot = BuildSnapshot(DateTime.UtcNow);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void SetPartySnapshot(IReadOnlyDictionary<Guid, string> partyMembers)
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            foreach (var entity in entitiesByKey.Values)
            {
                entity.IsPartyMember = false;
            }

            if (localEntity is not null)
            {
                localEntity.IsPartyMember = true;
            }

            foreach (var (guid, name) in partyMembers)
            {
                if (guid == Guid.Empty)
                {
                    continue;
                }

                var entity = AddOrUpdateEntity(null, guid, name, CombatEntityKind.Player);
                entity.IsPartyMember = true;
            }

            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void AddPartyMember(Guid guid, string name)
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            if (guid != Guid.Empty)
            {
                var entity = AddOrUpdateEntity(null, guid, name, CombatEntityKind.Player);
                entity.IsPartyMember = true;
            }

            if (localEntity is not null)
            {
                localEntity.IsPartyMember = true;
            }

            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void RemovePartyMember(Guid? guid)
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            if (guid is { } value && value != Guid.Empty && entitiesByGuid.TryGetValue(value, out var entity) && entity != localEntity)
            {
                entity.IsPartyMember = false;
            }

            if (localEntity is not null)
            {
                localEntity.IsPartyMember = true;
            }

            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void DisbandParty()
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            foreach (var entity in entitiesByKey.Values)
            {
                entity.IsPartyMember = false;
            }

            if (localEntity is not null)
            {
                localEntity.IsPartyMember = true;
            }

            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void Record(CombatHealthEvent healthEvent, DateTime receivedAtUtc)
    {
        RecordBatch(new[] { healthEvent }, receivedAtUtc);
    }

    public void RecordBatch(IEnumerable<CombatHealthEvent> healthEvents, DateTime receivedAtUtc)
    {
        var events = healthEvents
            .Where(x => x.Amount > 0)
            .ToArray();
        if (events.Length == 0)
        {
            return;
        }

        CombatTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (localEntity is null)
            {
                return;
            }

            UpdateGameTimeAnchor(events, receivedAtUtc);

            var recordedAny = false;
            foreach (var healthEvent in events)
            {
                recordedAny |= RecordHealthEvent(healthEvent, ResolveHealthEventTimeUtc(healthEvent, receivedAtUtc));
            }

            if (recordedAny)
            {
                snapshot = BuildSnapshot(receivedAtUtc);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void UpdateGameTimeAnchorFromTimeSync(long gameTimeMilliseconds, DateTime receivedAtUtc)
    {
        lock (sync)
        {
            anchorGameTimeMilliseconds = gameTimeMilliseconds;
            anchorUtc = receivedAtUtc;
            anchorFromTimeSync = true;
        }
    }

    public void UpdateCombatState(long objectId, bool inActiveCombat, bool inPassiveCombat, DateTime receivedAtUtc)
    {
        CombatTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (localEntity is null)
            {
                return;
            }

            if (!entitiesByObjectId.TryGetValue(objectId, out var entity) || !IsTracked(entity))
            {
                return;
            }

            var isInCombat = inActiveCombat || inPassiveCombat;

            if (isInCombat)
            {
                StartActiveEncounter(receivedAtUtc);
                combatStates[entity.EntityKey] = true;
            }
            else
            {
                combatStates[entity.EntityKey] = false;
                if (activeEncounter is not null && !AnyTrackedEntityInCombat())
                {
                    activeEncounter.IsActive = false;
                    activeEncounter.EndedAtUtc = receivedAtUtc;
                    activeEncounter = null;
                }
            }

            snapshot = BuildSnapshot(receivedAtUtc);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void Reset()
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            encounters.Clear();
            combatStates.Clear();
            activeEncounter = null;
            nextEncounterNumber = 1;
            anchorGameTimeMilliseconds = null;
            anchorUtc = null;
            anchorFromTimeSync = false;
            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    private bool RecordHealthEvent(CombatHealthEvent healthEvent, DateTime eventUtc)
    {
        var source = GetOrCreateEntityByObjectId(healthEvent.SourceObjectId);
        var target = GetOrCreateEntityByObjectId(healthEvent.TargetObjectId);

        if (!IsTracked(source) && !IsTracked(target))
        {
            return false;
        }

        var encounter = EnsureEncounterForHealthEvent(eventUtc);
        if (encounter == activeEncounter && encounter.Buckets.Count == 0 && eventUtc < encounter.StartedAtUtc)
        {
            encounter.StartedAtUtc = eventUtc;
        }

        var bucket = GetBucket(encounter, eventUtc);
        var spellKey = GetSpellKey(healthEvent.SpellId);

        if (healthEvent.Kind == CombatChangeKind.Damage)
        {
            bucket.AddDamage(source.EntityKey, target.EntityKey, spellKey, healthEvent.Amount);
        }
        else
        {
            bucket.AddHealing(source.EntityKey, target.EntityKey, spellKey, healthEvent.Amount);
        }

        return true;
    }

    private void UpdateGameTimeAnchor(IReadOnlyCollection<CombatHealthEvent> healthEvents, DateTime receivedAtUtc)
    {
        var gameTimeMilliseconds = healthEvents
            .Select(x => x.GameTimeMilliseconds)
            .Where(x => x.HasValue)
            .Select(x => x.GetValueOrDefault())
            .ToArray();
        if (gameTimeMilliseconds.Length == 0)
        {
            return;
        }

        var latestGameTimeMilliseconds = gameTimeMilliseconds.Max();
        if (anchorFromTimeSync)
        {
            return;
        }

        if (ShouldResetGameTimeAnchor(latestGameTimeMilliseconds, receivedAtUtc))
        {
            anchorGameTimeMilliseconds = latestGameTimeMilliseconds;
            anchorUtc = receivedAtUtc;
            anchorFromTimeSync = false;
        }
    }

    private bool ShouldResetGameTimeAnchor(long latestGameTimeMilliseconds, DateTime receivedAtUtc)
    {
        if (anchorGameTimeMilliseconds is not { } currentAnchorGameTimeMilliseconds || anchorUtc is not { } currentAnchorUtc)
        {
            return true;
        }

        if (latestGameTimeMilliseconds < currentAnchorGameTimeMilliseconds - (long)GameTimeBackwardResetThreshold.TotalMilliseconds)
        {
            return true;
        }

        var resolvedUtc = currentAnchorUtc.AddMilliseconds(latestGameTimeMilliseconds - currentAnchorGameTimeMilliseconds);
        return (resolvedUtc - receivedAtUtc).Duration() > GameTimeReceiveDriftResetThreshold;
    }

    private DateTime ResolveHealthEventTimeUtc(CombatHealthEvent healthEvent, DateTime fallbackUtc)
    {
        if (healthEvent.GameTimeMilliseconds is not { } gameTimeMilliseconds
            || anchorGameTimeMilliseconds is not { } currentAnchorGameTimeMilliseconds
            || anchorUtc is not { } currentAnchorUtc)
        {
            return fallbackUtc;
        }

        return currentAnchorUtc.AddMilliseconds(gameTimeMilliseconds - currentAnchorGameTimeMilliseconds);
    }

    private CombatEncounter EnsureEncounterForHealthEvent(DateTime receivedAtUtc)
    {
        if (activeEncounter is not null)
        {
            return activeEncounter;
        }

        if (encounters.Count > 0)
        {
            return encounters[^1];
        }

        return StartActiveEncounter(receivedAtUtc);
    }

    private CombatEncounter StartActiveEncounter(DateTime receivedAtUtc)
    {
        if (activeEncounter is not null)
        {
            return activeEncounter;
        }

        var encounterNumber = nextEncounterNumber++;
        activeEncounter = new CombatEncounter(
            $"encounter:{encounterNumber}",
            encounterNumber,
            receivedAtUtc);
        encounters.Add(activeEncounter);
        return activeEncounter;
    }

    private CombatTimeBucket GetBucket(CombatEncounter encounter, DateTime receivedAtUtc)
    {
        var elapsed = receivedAtUtc - encounter.StartedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var bucketIndex = (int)Math.Floor(elapsed.TotalSeconds / FixedBucketSize.TotalSeconds);
        while (encounter.Buckets.Count <= bucketIndex)
        {
            var index = encounter.Buckets.Count;
            var start = TimeSpan.FromTicks(FixedBucketSize.Ticks * index);
            encounter.Buckets.Add(new CombatTimeBucket(index, start, start + FixedBucketSize));
        }

        return encounter.Buckets[bucketIndex];
    }

    private bool AnyTrackedEntityInCombat()
    {
        foreach (var (entityKey, inCombat) in combatStates)
        {
            if (!inCombat)
            {
                continue;
            }

            if (entitiesByKey.TryGetValue(entityKey, out var entity) && IsTracked(entity))
            {
                return true;
            }
        }

        return false;
    }

    private CombatTrackerSnapshot BuildSnapshot(DateTime nowUtc)
    {
        var trackedEntities = entitiesByKey.Values
            .Where(IsTracked)
            .Select(CreateEntitySnapshot)
            .OrderByDescending(x => x.IsLocalPlayer)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var localPlayer = localEntity is null ? null : CreateEntitySnapshot(localEntity);
        var encounterSnapshots = encounters
            .Select(x => BuildEncounterSnapshot(x, nowUtc))
            .OrderByDescending(x => x.StartedAtUtc)
            .ToArray();
        var displayEncounter = activeEncounter is not null
            ? encounterSnapshots.FirstOrDefault(x => x.EncounterKey == activeEncounter.EncounterKey)
            : encounterSnapshots.FirstOrDefault();

        return new CombatTrackerSnapshot(
            displayEncounter?.IsActive ?? false,
            displayEncounter?.StartedAtUtc,
            displayEncounter?.EndedAtUtc,
            displayEncounter?.Elapsed ?? TimeSpan.Zero,
            entitiesByKey.Values.Count(IsTracked),
            localEntity is not null,
            localPlayer,
            trackedEntities,
            encounterSnapshots);
    }

    private CombatEncounterSnapshot BuildEncounterSnapshot(CombatEncounter encounter, DateTime nowUtc)
    {
        var elapsed = (encounter.EndedAtUtc ?? nowUtc) - encounter.StartedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var bucketPoints = encounter.Buckets
            .Select(x => new CombatTimeBucketPoint(
                x.Index,
                x.StartOffset,
                x.EndOffset,
                x.TotalDamageDealt(),
                x.TotalDamageReceived(),
                x.TotalHealingDone(),
                x.TotalHealingReceived(),
                x.PlayerTotals
                    .Select(player => new CombatParticipantBucketTotals(
                        player.Key,
                        player.Value.DamageDealt,
                        player.Value.DamageReceived,
                        player.Value.HealingDone,
                        player.Value.HealingReceived))
                    .ToArray()))
            .ToArray();

        return new CombatEncounterSnapshot(
            encounter.EncounterKey,
            encounter.EncounterNumber,
            encounter.IsActive,
            encounter.StartedAtUtc,
            encounter.EndedAtUtc,
            elapsed,
            bucketPoints.Sum(x => x.DamageDealt),
            bucketPoints.Sum(x => x.DamageReceived),
            bucketPoints.Sum(x => x.HealingDone),
            bucketPoints.Sum(x => x.HealingReceived),
            BuildPlayerSummaries(encounter.Buckets),
            BuildBreakdownRows(encounter.Buckets.FoldBreakdowns(x => x.DamageDealt)),
            BuildBreakdownRows(encounter.Buckets.FoldBreakdowns(x => x.DamageReceived)),
            BuildBreakdownRows(encounter.Buckets.FoldBreakdowns(x => x.HealingDone)),
            BuildBreakdownRows(encounter.Buckets.FoldBreakdowns(x => x.HealingReceived)),
            bucketPoints);
    }

    private IReadOnlyList<CombatPlayerSummary> BuildPlayerSummaries(IEnumerable<CombatTimeBucket> encounterBuckets)
    {
        return encounterBuckets
            .FoldTotals()
            .Select(x => new CombatPlayerSummary(
                x.Key,
                GetEntityName(x.Key),
                GetEntityRole(x.Key),
                x.Value.DamageDealt,
                x.Value.DamageReceived,
                x.Value.HealingDone,
                x.Value.HealingReceived))
            .Where(x => x.DamageDealt > 0 || x.DamageReceived > 0 || x.HealingDone > 0 || x.HealingReceived > 0)
            .OrderByDescending(x => x.DamageDealt)
            .ThenByDescending(x => x.HealingDone)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CombatEntitySnapshot CreateEntitySnapshot(CombatTrackedEntity entity)
    {
        return new CombatEntitySnapshot(
            entity.EntityKey,
            entity.ObjectId,
            entity.Guid,
            string.IsNullOrWhiteSpace(entity.Name) ? entity.EntityKey : entity.Name,
            GetEntityRole(entity),
            entity.IsPartyMember,
            entity.IsLocalPlayer);
    }

    private IReadOnlyList<CombatBreakdownRow> BuildBreakdownRows(Dictionary<string, Dictionary<string, Dictionary<string, long>>> breakdown)
    {
        return breakdown
            .SelectMany(player => player.Value.SelectMany(other => other.Value.Select(spell => new CombatBreakdownRow(
                player.Key,
                GetEntityName(player.Key),
                other.Key,
                GetEntityName(other.Key),
                spell.Key,
                GetSpellLabel(spell.Key),
                spell.Value))))
            .Where(x => x.Amount > 0)
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => x.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.OtherName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private CombatTrackedEntity AddOrUpdateEntity(long? objectId, Guid? guid, string? name, CombatEntityKind? kind = null)
    {
        CombatTrackedEntity? entity = null;

        if (guid is { } guidValue && guidValue != Guid.Empty)
        {
            entitiesByGuid.TryGetValue(guidValue, out entity);
        }

        if (entity is null && objectId is { } objectIdValue)
        {
            entitiesByObjectId.TryGetValue(objectIdValue, out entity);
        }

        if (entity is null)
        {
            var entityKey = guid is { } newGuid && newGuid != Guid.Empty
                ? $"guid:{newGuid:N}"
                : objectId is { } newObjectId
                    ? $"object:{newObjectId}"
                    : $"entity:{Guid.NewGuid():N}";
            entity = new CombatTrackedEntity(entityKey);
            entitiesByKey[entityKey] = entity;
        }

        if (objectId is { } resolvedObjectId)
        {
            entity.ObjectId = resolvedObjectId;
            entitiesByObjectId[resolvedObjectId] = entity;
        }

        if (guid is { } resolvedGuid && resolvedGuid != Guid.Empty)
        {
            entity.Guid = resolvedGuid;
            entitiesByGuid[resolvedGuid] = entity;
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            entity.Name = name;
        }
        else if (string.IsNullOrWhiteSpace(entity.Name) && entity.ObjectId is not null)
        {
            entity.Name = "Unknown entity";
        }

        if (kind is { } resolvedKind)
        {
            entity.Kind = resolvedKind;
        }

        return entity;
    }

    private CombatTrackedEntity GetOrCreateEntityByObjectId(long objectId)
    {
        if (entitiesByObjectId.TryGetValue(objectId, out var entity))
        {
            return entity;
        }

        return AddOrUpdateEntity(objectId, null, null);
    }

    private static bool IsTracked(CombatTrackedEntity entity)
    {
        return entity.IsLocalPlayer || entity.IsPartyMember;
    }

    private string GetEntityName(string entityKey)
    {
        return entitiesByKey.TryGetValue(entityKey, out var entity) && !string.IsNullOrWhiteSpace(entity.Name)
            ? entity.Name
            : "Unknown entity";
    }

    private CombatEntityRole GetEntityRole(string entityKey)
    {
        return entitiesByKey.TryGetValue(entityKey, out var entity)
            ? GetEntityRole(entity)
            : CombatEntityRole.Unknown;
    }

    private static CombatEntityRole GetEntityRole(CombatTrackedEntity entity)
    {
        if (entity.IsPartyMember || entity.IsLocalPlayer)
        {
            return CombatEntityRole.PartyPlayer;
        }

        return entity.Kind switch
        {
            CombatEntityKind.Player => CombatEntityRole.Player,
            CombatEntityKind.Mob => CombatEntityRole.Mob,
            _ => CombatEntityRole.Unknown
        };
    }

    private static string GetSpellKey(int? spellId)
    {
        return spellId is > 0 ? $"spell:{spellId.Value}" : "no-spell";
    }

    private static string GetSpellLabel(string spellKey)
    {
        if (spellKey == "no-spell")
        {
            return "No spell";
        }

        return spellKey.StartsWith("spell:", StringComparison.Ordinal)
            ? $"Spell {spellKey.Substring("spell:".Length)}"
            : spellKey;
    }

    private static string GetMobName(int? mobIndex, string? mobName)
    {
        if (!string.IsNullOrWhiteSpace(mobName))
        {
            return mobName;
        }

        return mobIndex is > 0
            ? $"Mob {mobIndex.Value}"
            : "Mob";
    }

    private sealed class CombatEncounter
    {
        public CombatEncounter(string encounterKey, int encounterNumber, DateTime startedAtUtc)
        {
            EncounterKey = encounterKey;
            EncounterNumber = encounterNumber;
            StartedAtUtc = startedAtUtc;
        }

        public string EncounterKey { get; }
        public int EncounterNumber { get; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? EndedAtUtc { get; set; }
        public bool IsActive { get; set; } = true;
        public List<CombatTimeBucket> Buckets { get; } = new();
    }

    private sealed class CombatTrackedEntity
    {
        public CombatTrackedEntity(string entityKey)
        {
            EntityKey = entityKey;
        }

        public string EntityKey { get; }
        public long? ObjectId { get; set; }
        public Guid? Guid { get; set; }
        public string Name { get; set; } = string.Empty;
        public CombatEntityKind Kind { get; set; } = CombatEntityKind.Unknown;
        public bool IsPartyMember { get; set; }
        public bool IsLocalPlayer { get; set; }
    }

    private enum CombatEntityKind
    {
        Unknown,
        Player,
        Mob
    }
}

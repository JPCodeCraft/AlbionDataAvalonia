using AlbionDataAvalonia.Combat.Models;
using AlbionDataAvalonia.Settings;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace AlbionDataAvalonia.Combat;

public sealed class CombatTrackerService : IDisposable
{
    public static readonly TimeSpan FixedBucketSize = TimeSpan.FromSeconds(1);

    // Conservative 64-bit .NET managed-heap estimates used only for the Settings UI.
    // They are not exact profiler measurements: the estimate starts from a typical
    // object header/method-table cost, 8-byte references, primitive field sizes, and
    // rounded-up collection shell/entry costs. Encounter/entity/bucket values include
    // their scalar fields and references; list/dictionary values estimate collection
    // containers and stored entries; strings use a base object cost plus two bytes
    // per character. The numbers are intentionally rounded up so the UI reports a
    // useful approximate size without claiming exact per-object heap attribution.
    private const long EstimatedEncounterBytes = 176;
    private const long EstimatedBucketBytes = 224;
    private const long EstimatedParticipantTotalEntryBytes = 128;
    private const long EstimatedEntityBytes = 192;
    private const long EstimatedListBytes = 56;
    private const long EstimatedDictionaryBytes = 96;
    private const long EstimatedDictionaryEntryBytes = 48;
    private const long EstimatedStringBytes = 24;
    private static readonly TimeSpan UnreferencedEntityRetention = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan UnreferencedEntityPruneInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GameTimeBackwardResetThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GameTimeReceiveDriftResetThreshold = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CombatEncounterIdleTimeout = TimeSpan.FromSeconds(7);

    private readonly object sync = new();
    private readonly Dictionary<string, CombatTrackedEntity> entitiesByKey = new();
    private readonly Dictionary<long, CombatTrackedEntity> entitiesByObjectId = new();
    private readonly Dictionary<Guid, CombatTrackedEntity> entitiesByGuid = new();
    private readonly List<CombatEncounter> encounters = new();
    private readonly Dictionary<string, bool> combatStates = new();
    private readonly SettingsManager settingsManager;
    private Timer? encounterIdleTimer;

    private CombatTrackedEntity? localEntity;
    private CombatEncounter? activeEncounter;
    private int nextEncounterNumber = 1;
    private long? anchorGameTimeMilliseconds;
    private DateTime? anchorUtc;
    private DateTime lastUnreferencedEntityPruneUtc = DateTime.MinValue;
    private bool anchorFromTimeSync;
    private bool isDisabled;
    private bool isPaused;
    private bool startNewEncounterAfterPause;

    public event Action<CombatTrackerSnapshot>? SnapshotChanged;

    public CombatTrackerService(SettingsManager settingsManager)
    {
        this.settingsManager = settingsManager;
        isDisabled = settingsManager.UserSettings.DisableCombatTracker;
        settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;

        if (isDisabled)
        {
            FullResetCore();
        }
    }

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

    public bool IsPaused
    {
        get
        {
            lock (sync)
            {
                return isPaused;
            }
        }
    }

    public void Dispose()
    {
        settingsManager.UserSettings.PropertyChanged -= OnUserSettingsPropertyChanged;
        lock (sync)
        {
            StopEncounterIdleTimer();
        }
    }

    public CombatTrackerStatistics GetStatistics()
    {
        lock (sync)
        {
            return BuildStatistics();
        }
    }

    public void SetLocalPlayer(long objectId, Guid? guid, string name)
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            if (isDisabled)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
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

            PruneUnreferencedEntitiesIfDue(nowUtc);
            snapshot = BuildSnapshot(nowUtc);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void AddOrUpdatePlayer(long? objectId, Guid? guid, string name)
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            if (isDisabled)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            AddOrUpdateEntity(objectId, guid, name, CombatEntityKind.Player);
            PruneUnreferencedEntitiesIfDue(nowUtc);
            snapshot = BuildSnapshot(nowUtc);
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
            if (isDisabled)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (entitiesByObjectId.TryGetValue(objectId, out var existingEntity)
                && (existingEntity.IsLocalPlayer || existingEntity.IsPartyMember))
            {
                return;
            }

            var previousName = existingEntity?.Name;
            var previousRole = existingEntity is null ? CombatEntityRole.Unknown : GetEntityRole(existingEntity);
            var entity = AddOrUpdateEntity(objectId, null, GetMobName(mobIndex, mobName), CombatEntityKind.Mob);
            PruneUnreferencedEntitiesIfDue(nowUtc);
            if (!string.Equals(previousName, entity.Name, StringComparison.Ordinal) || previousRole != GetEntityRole(entity))
            {
                snapshot = BuildSnapshot(nowUtc);
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
            if (isDisabled)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
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

            PruneUnreferencedEntitiesIfDue(nowUtc);
            snapshot = BuildSnapshot(nowUtc);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void AddPartyMember(Guid guid, string name)
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            if (isDisabled)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (guid != Guid.Empty)
            {
                var entity = AddOrUpdateEntity(null, guid, name, CombatEntityKind.Player);
                entity.IsPartyMember = true;
            }

            if (localEntity is not null)
            {
                localEntity.IsPartyMember = true;
            }

            PruneUnreferencedEntitiesIfDue(nowUtc);
            snapshot = BuildSnapshot(nowUtc);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void RemovePartyMember(Guid? guid)
    {
        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            if (isDisabled)
            {
                return;
            }

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
            if (isDisabled)
            {
                return;
            }

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
            if (isDisabled)
            {
                return;
            }

            if (localEntity is null)
            {
                return;
            }

            if (isPaused)
            {
                TrackHealthEventEntities(events, receivedAtUtc);
                PruneUnreferencedEntitiesIfDue(receivedAtUtc);
                return;
            }

            var closedIdleEncounter = EndActiveEncounterIfIdle(receivedAtUtc);
            UpdateGameTimeAnchor(events, receivedAtUtc);

            var recordedAny = false;
            foreach (var healthEvent in events)
            {
                recordedAny |= RecordHealthEvent(
                    healthEvent,
                    ResolveHealthEventTimeUtc(healthEvent, receivedAtUtc),
                    receivedAtUtc);
            }

            if (recordedAny || closedIdleEncounter)
            {
                PruneUnreferencedEntitiesIfDue(receivedAtUtc);
                snapshot = BuildSnapshot(receivedAtUtc);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void SetPaused(bool paused)
    {
        CombatTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (isDisabled)
            {
                isPaused = false;
                return;
            }

            if (isPaused == paused)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            isPaused = paused;
            if (paused)
            {
                EndActiveEncounter(nowUtc);
                combatStates.Clear();
                startNewEncounterAfterPause = true;
            }

            snapshot = BuildSnapshot(nowUtc);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }

        Log.Information(paused
            ? "Combat tracker paused; health changes will not be counted."
            : "Combat tracker resumed.");
    }

    public void UpdateGameTimeAnchorFromTimeSync(long gameTimeMilliseconds, DateTime receivedAtUtc)
    {
        lock (sync)
        {
            if (isDisabled)
            {
                return;
            }

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
            if (isDisabled)
            {
                return;
            }

            if (localEntity is null)
            {
                return;
            }

            if (isPaused)
            {
                return;
            }

            if (!entitiesByObjectId.TryGetValue(objectId, out var entity) || !IsTracked(entity))
            {
                return;
            }

            var isInCombat = inActiveCombat || inPassiveCombat;
            EndActiveEncounterIfIdle(receivedAtUtc);

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
                    EndActiveEncounter(receivedAtUtc);
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
            if (isDisabled)
            {
                FullResetCore();
            }
            else
            {
                ResetEncounterDataCore();
            }

            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.DisableCombatTracker)
            && e.PropertyName != nameof(UserSettings.CombatEncounterRetentionLimit))
        {
            return;
        }

        CombatTrackerSnapshot snapshot;
        lock (sync)
        {
            if (e.PropertyName == nameof(UserSettings.DisableCombatTracker))
            {
                var disableCombatTracker = settingsManager.UserSettings.DisableCombatTracker;
                if (isDisabled == disableCombatTracker)
                {
                    return;
                }

                isDisabled = disableCombatTracker;
                if (isDisabled)
                {
                    FullResetCore();
                    Log.Information("Combat tracker disabled; all combat tracker data was reset.");
                }
                else
                {
                    Log.Information("Combat tracker enabled.");
                }
            }
            else if (e.PropertyName == nameof(UserSettings.CombatEncounterRetentionLimit))
            {
                PruneRetainedEncounters();
            }

            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    private void ResetEncounterDataCore()
    {
        encounters.Clear();
        combatStates.Clear();
        activeEncounter = null;
        nextEncounterNumber = 1;
        anchorGameTimeMilliseconds = null;
        anchorUtc = null;
        lastUnreferencedEntityPruneUtc = DateTime.MinValue;
        anchorFromTimeSync = false;
        startNewEncounterAfterPause = false;
        StopEncounterIdleTimer();
    }

    private void FullResetCore()
    {
        ResetEncounterDataCore();
        entitiesByKey.Clear();
        entitiesByObjectId.Clear();
        entitiesByGuid.Clear();
        localEntity = null;
        isPaused = false;
    }

    private bool RecordHealthEvent(CombatHealthEvent healthEvent, DateTime eventUtc, DateTime seenAtUtc)
    {
        var source = GetOrCreateEntityByObjectId(healthEvent.SourceObjectId, seenAtUtc);
        var target = GetOrCreateEntityByObjectId(healthEvent.TargetObjectId, seenAtUtc);

        if (!IsTracked(source) && !IsTracked(target))
        {
            return false;
        }

        var encounter = EnsureEncounterForHealthEvent(eventUtc, seenAtUtc);
        if (encounter == activeEncounter && encounter.Buckets.Count == 0 && eventUtc < encounter.StartedAtUtc)
        {
            encounter.StartedAtUtc = eventUtc;
        }

        var bucket = GetBucket(encounter, eventUtc);

        if (healthEvent.Kind == CombatChangeKind.Damage)
        {
            bucket.AddDamage(source.EntityKey, target.EntityKey, healthEvent.Amount);
        }
        else
        {
            bucket.AddHealing(source.EntityKey, target.EntityKey, healthEvent.Amount);
        }

        if (encounter == activeEncounter)
        {
            encounter.LastIdleResetAtUtc = seenAtUtc;
            ScheduleEncounterIdleTimer(encounter);
        }

        return true;
    }

    private void TrackHealthEventEntities(IEnumerable<CombatHealthEvent> healthEvents, DateTime seenAtUtc)
    {
        foreach (var healthEvent in healthEvents)
        {
            GetOrCreateEntityByObjectId(healthEvent.SourceObjectId, seenAtUtc);
            GetOrCreateEntityByObjectId(healthEvent.TargetObjectId, seenAtUtc);
        }
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

    private CombatEncounter EnsureEncounterForHealthEvent(DateTime eventUtc, DateTime receivedAtUtc)
    {
        if (activeEncounter is not null)
        {
            return activeEncounter;
        }

        if (!startNewEncounterAfterPause && encounters.Count > 0)
        {
            var latestEncounter = encounters[^1];
            if (latestEncounter.IsActive
                || latestEncounter.EndedAtUtc is { } endedAtUtc
                    && (!latestEncounter.EndedByIdleTimeout || receivedAtUtc <= endedAtUtc)
                    && eventUtc <= endedAtUtc)
            {
                return latestEncounter;
            }
        }

        return StartActiveEncounter(eventUtc, receivedAtUtc);
    }

    private CombatEncounter StartActiveEncounter(DateTime startedAtUtc, DateTime? idleResetAtUtc = null)
    {
        if (activeEncounter is not null)
        {
            return activeEncounter;
        }

        var encounterNumber = nextEncounterNumber++;
        activeEncounter = new CombatEncounter(
            $"encounter:{encounterNumber}",
            encounterNumber,
            startedAtUtc,
            idleResetAtUtc ?? startedAtUtc);
        encounters.Add(activeEncounter);
        startNewEncounterAfterPause = false;
        PruneRetainedEncounters();
        ScheduleEncounterIdleTimer(activeEncounter);
        return activeEncounter;
    }

    private void EndActiveEncounter(DateTime endedAtUtc, bool endedByIdleTimeout = false)
    {
        if (activeEncounter is null)
        {
            return;
        }

        activeEncounter.IsActive = false;
        activeEncounter.EndedAtUtc = endedAtUtc;
        activeEncounter.EndedByIdleTimeout = endedByIdleTimeout;
        activeEncounter = null;
        StopEncounterIdleTimer();
    }

    private void ScheduleEncounterIdleTimer(CombatEncounter encounter)
    {
        if (!ReferenceEquals(encounter, activeEncounter) || !encounter.IsActive)
        {
            return;
        }

        var idleDeadlineUtc = encounter.LastIdleResetAtUtc + CombatEncounterIdleTimeout;
        var dueTime = idleDeadlineUtc - DateTime.UtcNow;
        if (dueTime < TimeSpan.Zero)
        {
            dueTime = TimeSpan.Zero;
        }

        encounterIdleTimer ??= new Timer(OnEncounterIdleTimerElapsed);
        encounterIdleTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
    }

    private void StopEncounterIdleTimer()
    {
        encounterIdleTimer?.Dispose();
        encounterIdleTimer = null;
    }

    private void OnEncounterIdleTimerElapsed(object? state)
    {
        CombatTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            var nowUtc = DateTime.UtcNow;
            if (EndActiveEncounterIfIdle(nowUtc))
            {
                snapshot = BuildSnapshot(nowUtc);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private bool EndActiveEncounterIfIdle(DateTime nowUtc)
    {
        if (activeEncounter is null || isDisabled || isPaused)
        {
            StopEncounterIdleTimer();
            return false;
        }

        var idleDeadlineUtc = activeEncounter.LastIdleResetAtUtc + CombatEncounterIdleTimeout;
        if (nowUtc < idleDeadlineUtc)
        {
            ScheduleEncounterIdleTimer(activeEncounter);
            return false;
        }

        EndActiveEncounter(idleDeadlineUtc, endedByIdleTimeout: true);
        combatStates.Clear();
        return true;
    }

    private void PruneRetainedEncounters()
    {
        var maxRetainedEncounters = GetMaxRetainedEncounters();
        if (encounters.Count <= maxRetainedEncounters)
        {
            return;
        }

        var removedAny = false;
        while (encounters.Count > maxRetainedEncounters)
        {
            var oldestEndedEncounterIndex = encounters.FindIndex(x => x != activeEncounter && !x.IsActive);
            if (oldestEndedEncounterIndex < 0)
            {
                break;
            }

            var removedEncounter = encounters[oldestEndedEncounterIndex];
            encounters.RemoveAt(oldestEndedEncounterIndex);
            Log.Debug(
                "Pruned combat encounter {EncounterKey} #{EncounterNumber}. Started={StartedAtUtc} Ended={EndedAtUtc} Buckets={BucketCount}",
                removedEncounter.EncounterKey,
                removedEncounter.EncounterNumber,
                removedEncounter.StartedAtUtc,
                removedEncounter.EndedAtUtc,
                removedEncounter.Buckets.Count);
            removedAny = true;
        }

        if (removedAny)
        {
            PruneUnreferencedEntities(DateTime.UtcNow);
        }
    }

    private void PruneUnreferencedEntitiesIfDue(DateTime nowUtc)
    {
        if (lastUnreferencedEntityPruneUtc != DateTime.MinValue
            && nowUtc - lastUnreferencedEntityPruneUtc < UnreferencedEntityPruneInterval)
        {
            return;
        }

        lastUnreferencedEntityPruneUtc = nowUtc;
        PruneUnreferencedEntities(nowUtc);
    }

    private void PruneUnreferencedEntities(DateTime nowUtc)
    {
        var referencedEntityKeys = GetReferencedEntityKeys();
        var entityKeysToRemove = entitiesByKey
            .Where(x => !x.Value.IsLocalPlayer
                && !x.Value.IsPartyMember
                && !referencedEntityKeys.Contains(x.Key)
                && nowUtc - x.Value.LastSeenAtUtc > UnreferencedEntityRetention)
            .Select(x => x.Key)
            .ToArray();

        foreach (var entityKey in entityKeysToRemove)
        {
            if (entitiesByKey.TryGetValue(entityKey, out var entity))
            {
                Log.Debug(
                    "Pruned stale unreferenced combat entity {EntityKey}. Name={Name} Kind={Kind} ObjectId={ObjectId} Guid={Guid} LastSeenAtUtc={LastSeenAtUtc}",
                    entity.EntityKey,
                    string.IsNullOrWhiteSpace(entity.Name) ? null : entity.Name,
                    entity.Kind,
                    entity.ObjectId,
                    entity.Guid,
                    entity.LastSeenAtUtc);
                RemoveEntity(entity);
            }
        }
    }

    private HashSet<string> GetReferencedEntityKeys()
    {
        var referencedEntityKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var encounter in encounters)
        {
            foreach (var bucket in encounter.Buckets)
            {
                foreach (var entityKey in bucket.PlayerTotals.Keys)
                {
                    referencedEntityKeys.Add(entityKey);
                }
            }
        }

        return referencedEntityKeys;
    }

    private int GetMaxRetainedEncounters()
    {
        return Math.Clamp(
            settingsManager.UserSettings.CombatEncounterRetentionLimit,
            UserSettings.MinCombatEncounterRetentionLimit,
            UserSettings.MaxCombatEncounterRetentionLimit);
    }

    private CombatTrackerStatistics BuildStatistics()
    {
        var timeBucketCount = 0;
        var participantTotalCount = 0;
        var estimatedHistoryBytes = 0L;

        estimatedHistoryBytes += encounters.Count * (EstimatedEncounterBytes + EstimatedListBytes);
        foreach (var encounter in encounters)
        {
            estimatedHistoryBytes += EstimateStringSize(encounter.EncounterKey);
            estimatedHistoryBytes += encounter.Buckets.Count * EstimatedDictionaryEntryBytes;
            timeBucketCount += encounter.Buckets.Count;

            foreach (var bucket in encounter.Buckets)
            {
                var bucketParticipantTotalCount = bucket.PlayerTotals.Count;
                participantTotalCount += bucketParticipantTotalCount;
                estimatedHistoryBytes += EstimatedBucketBytes
                    + EstimatedDictionaryBytes
                    + bucketParticipantTotalCount * EstimatedParticipantTotalEntryBytes;
            }
        }

        estimatedHistoryBytes += entitiesByKey.Count * EstimatedDictionaryEntryBytes;
        estimatedHistoryBytes += entitiesByObjectId.Count * EstimatedDictionaryEntryBytes;
        estimatedHistoryBytes += entitiesByGuid.Count * EstimatedDictionaryEntryBytes;
        estimatedHistoryBytes += entitiesByKey.Values.Sum(entity =>
            EstimatedEntityBytes
            + EstimateStringSize(entity.EntityKey)
            + EstimateStringSize(entity.Name));

        return new CombatTrackerStatistics(
            encounters.Count,
            GetMaxRetainedEncounters(),
            entitiesByKey.Count,
            timeBucketCount,
            participantTotalCount,
            estimatedHistoryBytes);
    }

    private static long EstimateStringSize(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? 0
            : EstimatedStringBytes + value.Length * sizeof(char);
    }

    private void RemoveEntity(CombatTrackedEntity entity)
    {
        entitiesByKey.Remove(entity.EntityKey);
        combatStates.Remove(entity.EntityKey);

        if (entity.ObjectId is { } objectId
            && entitiesByObjectId.TryGetValue(objectId, out var entityByObjectId)
            && ReferenceEquals(entityByObjectId, entity))
        {
            entitiesByObjectId.Remove(objectId);
        }

        if (entity.Guid is { } guid
            && entitiesByGuid.TryGetValue(guid, out var entityByGuid)
            && ReferenceEquals(entityByGuid, entity))
        {
            entitiesByGuid.Remove(guid);
        }
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
            isPaused,
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

    private CombatTrackedEntity AddOrUpdateEntity(long? objectId, Guid? guid, string? name, CombatEntityKind? kind = null, DateTime? seenAtUtc = null)
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

        entity.LastSeenAtUtc = seenAtUtc ?? DateTime.UtcNow;

        return entity;
    }

    private CombatTrackedEntity GetOrCreateEntityByObjectId(long objectId, DateTime seenAtUtc)
    {
        if (entitiesByObjectId.TryGetValue(objectId, out var entity))
        {
            entity.LastSeenAtUtc = seenAtUtc;
            return entity;
        }

        return AddOrUpdateEntity(objectId, null, null, seenAtUtc: seenAtUtc);
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
        public CombatEncounter(string encounterKey, int encounterNumber, DateTime startedAtUtc, DateTime idleResetAtUtc)
        {
            EncounterKey = encounterKey;
            EncounterNumber = encounterNumber;
            StartedAtUtc = startedAtUtc;
            LastIdleResetAtUtc = idleResetAtUtc;
        }

        public string EncounterKey { get; }
        public int EncounterNumber { get; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime LastIdleResetAtUtc { get; set; }
        public DateTime? EndedAtUtc { get; set; }
        public bool EndedByIdleTimeout { get; set; }
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
        public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
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

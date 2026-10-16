using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Players;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

/// <summary>One capture-time policy for the journal, sessions and their encounters.</summary>
public sealed partial class ActivityTrackerService : IDisposable
{
    private static readonly TimeSpan EncounterIdle = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan ReentryGrace = TimeSpan.FromMinutes(2);
    // A capture restart within this time continues on the same map and character.
    private static readonly TimeSpan CaptureResumeGrace = TimeSpan.FromMinutes(1);
    private readonly object sync = new();
    private readonly PlayerState player;
    private readonly SettingsManager settings;
    private readonly ActivityPersistence persistence;
    private readonly ActivityActorContext actors;
    private readonly ActivityLootCollector loot;
    private readonly ActivityGatheringCollector gathering;
    private readonly ActivityFameCollector fameCollector;
    private readonly ActivityItemValuationService valuations;
    private readonly PlayerIdentityService identities;
    private readonly Timer timer;
    private readonly Dictionary<string, DateTime> origins = new(StringComparer.Ordinal);
    private readonly Queue<string> originOrder = new();
    private readonly Dictionary<long, MobParticipation> mobParticipation = new();
    private DateTime lastCacheSweep;
    private readonly List<PendingTerminal> pendingTerminals = new();
    private readonly Dictionary<Guid, ActivitySession> deferredSessions = new();
    private readonly Dictionary<Guid, ActivityEncounter> deferredEncounters = new();
    private ActivityEvent? deferredGap;
    private string? connectionId;
    private PhotonMessageIdentity? connectionIdentity;
    private bool reentryConfirmed;
    private bool partialEntry;
    private ActivityLocationDescriptor? map;
    private DateTime mapEnteredAt;
    private ActivitySession? session;
    private ActivityEncounter? encounter;
    // A wall-clock timeout is provisional until captured traffic confirms the gap.
    // Keep only the latest header so a delayed packet can retain its original fight.
    private ActivityEncounter? timedOutEncounter;
    private ActivitySession? resumable;
    private readonly List<ActivityVisit> visits = new();
    private readonly HashSet<string> knownInstances = new(StringComparer.OrdinalIgnoreCase);
    private int encounterNumber;
    private long captureGeneration;
    private long mapEpoch;
    private long captureOrder;
    private bool enabled;
    private bool awaitingContext = true;
    private bool captureSuspended = true;
    private bool storageSuspended;
    // A database pause keeps map, actor and inventory context; recording resumes in place.
    private bool storageGap;
    // A short capture interruption keeps its context; observation reopens with the next packet.
    private bool captureGap;
    private DateTime? captureStoppedAt;
    private bool disposed;
    private bool headersDirty;
    private DateTime lastHeaderSave;

    public ActivityTrackerService(PlayerState player, PartyTrackerService party, MobsService mobs,
        ItemEstimatedMarketValueService values, ItemsIdsService items, SettingsManager settings, ActivityPersistence persistence,
        AchievementsService achievements, ActivityItemValuationService valuations, PlayerIdentityService identities)
    {
        this.player = player;
        this.identities = identities;
        this.settings = settings;
        this.persistence = persistence;
        this.valuations = valuations;
        partyRoster = party;
        enabled = settings.UserSettings.ActivityRecordingEnabled;
        actors = new ActivityActorContext(party, mobs);
        loot = new ActivityLootCollector(this, values, items);
        gathering = new ActivityGatheringCollector(this, loot, values, items);
        fameCollector = new ActivityFameCollector(achievements);
        timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        settings.UserSettings.PropertyChanged += SettingsChanged;
        persistence.StatusChanged += StorageChanged;
        persistence.SessionDeleted += SessionDeleted;
        persistence.UnassignedLootDeleted += UnassignedLootDeleted;
        player.OnPlayerStateChanged += PeriodOwnerChanged;
        identities.IdentityChanged += IdentityChanged;
    }

    public event EventHandler? Changed;
    public Guid? CurrentSessionId { get { lock (sync) return session?.Id; } }
    public Guid? CurrentEncounterId { get { lock (sync) return encounter?.Id; } }
    public bool IsRecordingEnabled { get { lock (sync) return enabled; } }
    public bool IsAwaitingContext { get { lock (sync) return awaitingContext; } }
    public bool IsCaptureSuspended { get { lock (sync) return captureSuspended; } }
    public string StatusText
    {
        get
        {
            lock (sync)
            {
                if (storageSuspended) return "Recording suspended: database backlog. Accepted events are pending.";
                if (!enabled) return "Recording disabled. Saved history remains available.";
                if (captureSuspended) return "Capture stopped.";
                if (awaitingContext) return "Waiting for character and map confirmation. Nearby loot can still be observed.";
                return encounter is not null ? "Recording encounter and session activity."
                    : session is not null ? "Recording session activity." : "Ready. A session counts from your first activity.";
            }
        }
    }

    public void SetRecordingEnabled(bool value)
    {
        settings.UserSettings.ActivityRecordingEnabled = value;
        lock (sync)
        {
            if (enabled == value) return;
            // Pickups already proven before recording stops still belong to the closing session.
            if (!value) ResolvePending(DateTime.UtcNow);
            enabled = value;
            if (value && map is not null) { mapEnteredAt = DateTime.UtcNow; partialEntry = true; }
            if (value) playOpenedAt = DateTime.UtcNow;
            if (!value) ClosePlaySession(DateTime.UtcNow, ActivityEndReason.Disabled);
            loot.ResetRecordingCorrelations();
            locations.ClearPending();
            ClearPendingActorMetadata();
            gathering.Reset();
            fameCollector.Reset();
            mobParticipation.Clear();
            pendingTerminals.Clear();
            SaveHeaders();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OnCaptureReset()
    {
        lock (sync)
        {
            var now = DateTime.UtcNow;
            // An adapter change or traffic recovery restarts capture on the same map. After a
            // longer stop the map or character may have changed, so wait for the next join.
            if (captureStoppedAt is { } stopped && now - stopped > CaptureResumeGrace) Suspend(now);
            else PauseCapture(now);
            captureStoppedAt = null;
            captureGeneration++;
            origins.Clear();
            originOrder.Clear();
            // The restarted parser identifies its connections anew; the next join binds them again.
            connectionId = null;
            connectionIdentity = null;
            captureSuspended = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OnCaptureSuspended()
    {
        lock (sync)
        {
            PauseCapture(DateTime.UtcNow);
            captureSuspended = true;
            captureStoppedAt ??= DateTime.UtcNow;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Keeps map, character and inventory context; only in-flight correlations are dropped.
    private void PauseCapture(DateTime at)
    {
        ResolvePending(at);
        ClearPendingActorMetadata();
        timedOutEncounter = null;
        loot.ResetCandidates();
        gathering.Reset();
        fameCollector.Reset();
        mobParticipation.Clear();
        pendingTerminals.Clear();
        if (captureGap || awaitingContext || !enabled || session is null) return;
        RecordGap(at, "Suspended");
        captureGap = true;
    }

    private void ResumeAfterCaptureGap(DateTime at)
    {
        captureGap = false;
        if (session is null || !enabled || awaitingContext || map is null || storageGap || session.ObservationStartedAtUtc is not null) return;
        OpenObservation(at);
        headersDirty = true;
    }

    // Closes the encounter and observation and marks the session partial at an explicit gap.
    private void RecordGap(DateTime at, string state)
    {
        var gap = Stamp(ActivityEventKind.CaptureGap, at, null) with
        { EncounterId = null, DetailsJson = JsonSerializer.Serialize(new { State = state }) };
        CloseEncounter(at, ActivityEndReason.CaptureGap);
        if (!persistence.Create(gap)) deferredGap = gap;
        CloseObservation(at);
        session = session! with { Revision = session.Revision + 1, LastObservedAtUtc = at, IsPartial = true };
        headersDirty = true;
        SaveHeaders();
    }

    private void Suspend(DateTime at)
    {
        ResolvePending(at);
        ClearPendingActorMetadata();
        timedOutEncounter = null;
        if (!captureGap && !awaitingContext && enabled && session is not null) RecordGap(at, "Suspended");
        captureGap = false;
        captureSuspended = true;
        awaitingContext = true;
        actors.Suspend();
        map = null;
        mapMetadata = new();
        locationObservation = null;
        locations.Reset();
        loot.Reset();
        gathering.Reset();
        fameCollector.Reset();
        mobParticipation.Clear();
        pendingTerminals.Clear();
    }

    public void Observe(BaseEvent packet)
    {
        lock (sync)
        {
            if (disposed || !Accept(packet.MessageIdentity?.OriginKey, packet.GetType().Name, packet.CapturedAt)) return;
            var at = Utc(packet.CapturedAt);
            // Item baselines can precede Join on the next connection. Cache only;
            // Join confirms their connection and inventory membership before use.
            loot.ObservePendingInventoryMetadata(packet, at);
            if (packet is NewJournalItemEvent journalItem) gathering.NoteGameValue(journalItem.Item);
            else if (packet is NewSimpleItemEvent simpleItem) gathering.NoteGameValue(simpleItem.Item);
            ObservePendingActorMetadata(packet, at);
            if (!awaitingContext && (at < mapEnteredAt || !SameConnection(packet.ConnectionId, packet.MessageIdentity)))
            { gathering.Reset(); fameCollector.Reset(); return; }
            Expire(at);
            if (captureGap) ResumeAfterCaptureGap(at);
            switch (packet)
            {
                case NewRandomDungeonExitEvent entrance when HasConfirmedContext:
                    locations.Observe(entrance, map!.RawLocationId, at);
                    break;
                case NewCharacterEvent character:
                    actors.Character(character);
                    // A member first listed without a name is recorded once a nearby character names it.
                    if (partyHasUnnamedMembers) SyncParty(at, Origin(packet, "Party"));
                    break;
                case PartyJoinedEvent or PartyPlayerJoinedEvent or PartyPlayerLeftEvent or PartyDisbandedEvent
                    or PartyOnClusterPartyJoinedEvent or PartySetRoleFlagEvent:
                    SyncParty(at, Origin(packet, "Party"));
                    break;
                case NewMobEvent mob when mob.ObjectId is > 0:
                    actors.Mob(mob.ObjectId.Value, mob.MobIndex, mob.CurrentHealth);
                    ResolvePendingTerminals(mob.ObjectId.Value, at);
                    break;
                case LeaveEvent leave:
                    actors.Leave(leave.userObjectId, at);
                    locations.Leave(leave.userObjectId, at);
                    // A local Leave may be a map transition, never a confirmed logout/death.
                    break;
                case HealthUpdateEvent health when CanRecord && health.IsValid:
                    Health(health.SourceObjectId, health.AffectedObjectId, health.HealthChange,
                        health.NewHealthState, health.ResultingHealth, health.SpellIndex,
                        health.GameTimeMilliseconds, at, Origin(packet, "Health", 0));
                    break;
                case HealthUpdatesEvent health when CanRecord:
                    foreach (var entry in health.HealthUpdates.Where(x => x.IsValid))
                        Health(entry.SourceObjectId, entry.AffectedObjectId, entry.HealthChange,
                            entry.NewHealthState, entry.ResultingHealth, entry.SpellIndex,
                            entry.GameTimeMilliseconds, at, Origin(packet, "Health", entry.EntryIndex));
                    break;
                case AchievementProgressInfoEvent progress when CanRecord:
                    if (Tracked(actors.Resolve(progress.ActorObjectId))) fameCollector.Observe(progress, at);
                    break;
                case UpdateFameEvent fame when CanRecord:
                    RecordFame(fame, at);
                    break;
                case TakeSilverEvent silver when CanRecord: Silver(silver, at); break;
                case DiedEvent death when CanRecord: Death(death, at); break;
                case KnockedDownEvent knockdown when CanRecord: Knockdown(knockdown, at); break;
                case KilledPlayerEvent kill when CanRecord:
                    RecordKilledPlayerEvidence(kill, at);
                    if (fameCollector.ObserveKill(kill, at) is { } pvpAward
                        && !persistence.Amend(pvpAward))
                    { storageSuspended = true; fameCollector.Reset(); }
                    break;
                case PartySilverGainedEvent silver when CanRecord:
                    var recipient = actors.Resolve(silver.TargetObjectId);
                    if (!Tracked(recipient)) break;
                    Persist(WithSource(Stamp(ActivityEventKind.OutcomeEvidence, at, Origin(packet, "PartySilver")), recipient) with
                    {
                        Evidence = ActivityEvidence.Supplemental,
                        DetailsJson = JsonSerializer.Serialize(new
                        { Signal = "PartySilver", silver.TargetObjectId, silver.NetRaw, silver.GrossRaw })
                    });
                    break;
                case HarvestFinishedEvent harvest when CanRecord:
                    gathering.Observe(harvest, at);
                    break;
                case RewardGrantedEvent reward when CanRecord:
                    gathering.Observe(reward, at);
                    break;
                case JournalGotFullEvent journal when CanRecord:
                    gathering.Observe(journal, at);
                    break;
                default:
                    if (enabled && !storageSuspended) loot.Observe(packet, at);
                    else if (HasConfirmedContext) loot.Observe(packet, at, record: false);
                    break;
            }
            if (headersDirty && at - lastHeaderSave >= TimeSpan.FromSeconds(1)) SaveHeaders();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Observe(BaseOperation packet)
    {
        lock (sync)
        {
            if (disposed || !Accept(packet.MessageIdentity?.OriginKey, packet.GetType().Name, packet.CapturedAt)) return;
            var at = Utc(packet.CapturedAt);
            if (packet is JoinRequest) { BeginActorJoin(at); fameCollector.Reset(); }
            if (packet is not JoinResponse && !awaitingContext && (at < mapEnteredAt || !SameConnection(packet.ConnectionId, packet.MessageIdentity)))
            { gathering.Reset(); fameCollector.Reset(); return; }
            Expire(at);
            if (captureGap && packet is not JoinResponse) ResumeAfterCaptureGap(at);
            if (packet is JoinResponse join && join.ReturnCode == 0
                && join.userObjectId > 0 && !string.IsNullOrWhiteSpace(join.RawLocationId)) Join(join, at);
            else if (packet is JoinResponse) { locations.ClearPending(); ClearPendingActorMetadata(); }
            else if (packet is ChangeClusterResponse transition && HasConfirmedContext)
                ObserveLocationTransition(transition, at);
            else if (packet is ExitEnterStartRequest entrance && HasConfirmedContext)
                locations.Observe(entrance, at);
            else if (CanRecord)
            {
                gathering.Observe(packet, at);
                loot.Observe(packet, at);
            }
            else if (HasConfirmedContext) loot.Observe(packet, at, record: false);
            if (headersDirty) SaveHeaders();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ResolvePending(DateTime at)
    {
        loot.ResolvePending(at);
        gathering.ResolvePending(at);
    }

    private bool HasConfirmedContext => !captureSuspended && !awaitingContext && map is not null && actors.HasIdentity;
    private bool CanRecord => enabled && !storageSuspended && HasConfirmedContext;

    private void Join(JoinResponse packet, DateTime at)
    {
        // Pickups and catches waiting on their correlation window belong to the map being left.
        ResolvePending(at);
        var next = packet.ActivityLocation;
        var previous = map;
        // An unrecognised server IP (VPN, routing tools) keeps the last known server, else 0.
        var nextServer = player.AlbionServer?.Id ?? actors.ServerId ?? 0;
        captureGap = false;
        var sameCharacter = actors.HasIdentity && actors.ServerId == nextServer
            && actors.CharacterId == (packet.userGuid?.ToString() ?? "")
            && (actors.CharacterId.Length > 0 || actors.CharacterName == packet.playerName);
        var nextMetadata = locations.Join(packet, next, previous?.RawLocationId, sameCharacter, at);
        var switched = session is not null && (session.Server != nextServer.ToString(CultureInfo.InvariantCulture)
            || session.CharacterId != (packet.userGuid?.ToString() ?? "")
            || session.CharacterId.Length == 0 && session.CharacterName != packet.playerName);
        CloseEncounter(at, switched ? ActivityEndReason.CharacterChanged : ActivityEndReason.MapTransition);
        if (switched) ClosePlaySession(at, ActivityEndReason.CharacterChanged);
        else if (session is not null)
        {
            if (IsInstance(next.Kind))
            {
                if (session.Kind == ActivitySessionKind.Roaming || !Compatible(next, previous))
                    CloseSession(at, session.HasLocalDeath ? ActivityEndReason.Death : ActivityEndReason.MapTransition);
            }
            else if (session.Kind != ActivitySessionKind.Roaming)
            {
                var ended = CloseSession(at, session.HasLocalDeath ? ActivityEndReason.Death : ActivityEndReason.MapTransition);
                resumable = ended is { HasLocalDeath: false } ? ended : null;
            }
            else if (IsHub(next.Kind)) CloseSession(at, ActivityEndReason.MapTransition);
        }

        mapEpoch++;
        connectionId = packet.ConnectionId;
        connectionIdentity = packet.MessageIdentity;
        actors.Join(packet, nextServer, mapEpoch);
        ReplayPendingActorMetadata(packet, at);
        confirmedOwner = actors.HasIdentity ? new ActivityOwner(actors.ServerId!.Value.ToString(CultureInfo.InvariantCulture),
            actors.CharacterId, actors.CharacterName) : null;
        OpenPlaySessionFor(confirmedOwner, at);
        map = next;
        mapMetadata = nextMetadata;
        locationObservation = null;
        if (sameCharacter) RestoreLocationMetadata();
        mapEnteredAt = at;
        partialEntry = !enabled;
        awaitingContext = !actors.HasIdentity;
        captureSuspended = false;
        reentryConfirmed = resumable is { ClosedAtUtc: { } exited } old && at - exited <= ReentryGrace
            && old.Server == nextServer.ToString(CultureInfo.InvariantCulture)
            && old.CharacterId == (packet.userGuid?.ToString() ?? "") && !old.HasLocalDeath
            && JsonSerializer.Deserialize<string[]>(old.KnownInstancesJson)?.Contains(InstanceKey(next)) == true;
        loot.Join(packet, at);
        gathering.Reset();
        fameCollector.Reset();
        mobParticipation.Clear();
        pendingTerminals.Clear();

        if (session is not null && enabled)
        {
            CloseObservation(at);
            OpenObservation(at);
            TrackInstance(next);
            Persist(LocationEvent(at, Origin(packet, "Location")));
            headersDirty = true;
        }
    }

    private bool Compatible(ActivityLocationDescriptor next, ActivityLocationDescriptor? previous)
    {
        if (session is null) return false;
        var key = InstanceKey(next);
        if (knownInstances.Contains(key)) return true;
        if (!MultiFloor(next.Kind) || previous?.Kind != next.Kind) return false;
        var parent = next.SourceClusterId ?? next.ParentClusterId;
        if (!string.IsNullOrWhiteSpace(parent))
            return string.Equals(parent, session.RootMapId, StringComparison.OrdinalIgnoreCase)
                || visits.Any(x => string.Equals(x.RawMapId, parent, StringComparison.OrdinalIgnoreCase));
        session = session with { HasInferredGrouping = true, Revision = session.Revision + 1 };
        return true;
    }

    // Every recorded activity passes here: it opens a run when needed and keeps the play session alive.
    private void EnsureSession(DateTime at)
    {
        EnsureRun(at);
        if (session is not null) MarkPlayActivity(at);
    }

    private void EnsureRun(DateTime at)
    {
        if (session is not null || !CanRecord || map is null) return;
        var visitStartedAt = at;
        if (reentryConfirmed && resumable is { ClosedAtUtc: { } exited } old
            && mapEnteredAt - exited <= ReentryGrace && old.Server == actors.ServerId?.ToString(CultureInfo.InvariantCulture)
            && old.CharacterId == actors.CharacterId && !old.HasLocalDeath
            && JsonSerializer.Deserialize<string[]>(old.KnownInstancesJson)?.Contains(InstanceKey(map)) == true)
        {
            session = old with
            {
                Revision = old.Revision + 2,
                PlaySessionId = ActivatePlaySession(mapEnteredAt, at)?.Id ?? old.PlaySessionId,
                State = ActivityState.Open,
                ClosedAtUtc = null,
                EndReason = ActivityEndReason.None,
                ObservationStartedAtUtc = mapEnteredAt
            };
            visits.Clear();
            visits.AddRange(JsonSerializer.Deserialize<ActivityVisit[]>(old.VisitsJson) ?? []);
            knownInstances.Clear();
            foreach (var value in JsonSerializer.Deserialize<string[]>(old.KnownInstancesJson) ?? []) knownInstances.Add(value);
            RestoreLocationMetadata();
            visits.Add(new ActivityVisit(map.RawLocationId, map.DisplayName, mapEnteredAt, null, map.InstanceId?.ToString(), mapMetadata));
            visitStartedAt = mapEnteredAt;
            // Existing encounter numbers remain monotonic after a resumed visit.
        }
        else
        {
            visits.Clear();
            knownInstances.Clear();
            encounterNumber = 0;
            // An instance run counts from its entrance, but never from before its play session opened.
            var entered = mapEnteredAt > playOpenedAt ? mapEnteredAt : playOpenedAt;
            var start = IsInstance(map.Kind) && entered < at ? entered : at;
            visitStartedAt = start;
            session = new ActivitySession
            {
                Id = Guid.NewGuid(),
                PlaySessionId = ActivatePlaySession(start, at)?.Id,
                Server = actors.ServerId?.ToString(CultureInfo.InvariantCulture) ?? "",
                CharacterId = actors.CharacterId,
                CharacterName = actors.CharacterName,
                Kind = SessionKind(map.Kind),
                Title = $"{(IsInstance(map.Kind) ? map.Kind.ToString() : "Activity")} in {map.DisplayName}",
                RootMapId = map.SourceClusterId ?? map.ParentClusterId ?? map.RawLocationId,
                LocationName = map.DisplayName,
                StartedAtUtc = start,
                LastRelevantAtUtc = at,
                LastObservedAtUtc = at,
                ObservationStartedAtUtc = start,
                State = ActivityState.Open,
                IsPartial = partialEntry
            };
            visits.Add(new ActivityVisit(map.RawLocationId, map.DisplayName, start, null, map.InstanceId?.ToString(), mapMetadata));
        }
        resumable = null;
        reentryConfirmed = false;
        TrackInstance(map);
        SaveHeaders();
        Persist(LocationEvent(visitStartedAt, null));
        SyncParty(visitStartedAt, null);
    }

    private void TrackInstance(ActivityLocationDescriptor value)
    {
        if (session is null) return;
        if (IsInstance(value.Kind)) knownInstances.Add(InstanceKey(value));
        session = session with
        {
            Revision = session.Revision + 1,
            KnownInstancesJson = JsonSerializer.Serialize(knownInstances),
            VisitsJson = JsonSerializer.Serialize(visits)
        };
        headersDirty = true;
    }

    private void OpenObservation(DateTime at)
    {
        if (session is null || map is null) return;
        session = session with { ObservationStartedAtUtc = at, Revision = session.Revision + 1 };
        visits.Add(new ActivityVisit(map.RawLocationId, map.DisplayName, at, null, map.InstanceId?.ToString(), mapMetadata));
    }

    private void CloseObservation(DateTime at)
    {
        if (session is null) return;
        var elapsed = session.ObservationStartedAtUtc is { } started ? Math.Max(0, (at - started).TotalSeconds) : 0;
        if (visits.Count > 0 && visits[^1].EndedAtUtc is null) visits[^1] = visits[^1] with { EndedAtUtc = at };
        session = session with
        {
            Revision = session.Revision + 1,
            ObservationStartedAtUtc = null,
            ObservedDurationSeconds = session.ObservedDurationSeconds + elapsed,
            VisitsJson = JsonSerializer.Serialize(visits)
        };
    }

    private void TouchRelevant(DateTime at)
    {
        if (session is null) return;
        resumable = null;
        session = session with
        {
            Revision = session.Revision + 1,
            LastRelevantAtUtc = Max(session.LastRelevantAtUtc, at),
            LastObservedAtUtc = Max(session.LastObservedAtUtc, at)
        };
        headersDirty = true;
    }

    private void Health(long? sourceId, long targetId, double delta, PacketFieldState healthState,
        double? resultingHealth, int? spell, long? gameTime, DateTime at, string? origin)
    {
        if (!double.IsFinite(delta) || targetId <= 0) return;
        // A dead player who acts, or is healed, has respawned or been revived.
        actors.Revive(sourceId);
        if (delta > 0) actors.Revive(targetId);
        var source = actors.Resolve(sourceId);
        var target = actors.Resolve(targetId);
        var relevant = delta != 0 && (Tracked(source) || Tracked(target));
        var terminal = healthState == PacketFieldState.Valid && resultingHealth == 0
            || delta < 0 && healthState == PacketFieldState.Missing;
        if (!relevant && !(terminal && mobParticipation.ContainsKey(targetId))) return;
        if (relevant)
        {
            EnsureSession(at);
            TouchRelevant(at);
        }
        var combat = relevant && (delta < 0 || sourceId is > 0);
        if (combat) EnsureEncounter(at, delta < 0 ? Classify(source, target) : ActivityEncounterKind.Healing);
        var observation = WithTarget(WithSource(Stamp(delta < 0 ? ActivityEventKind.Damage : delta > 0
            ? ActivityEventKind.Healing : ActivityEventKind.OutcomeEvidence,
            at, origin), source), target) with
        {
            EncounterId = combat ? encounter?.Id : null,
            Amount = Math.Abs(delta),
            GameTimestamp = gameTime,
            DetailsJson = JsonSerializer.Serialize(new
            {
                SignedDelta = delta,
                ResultingHealth = resultingHealth,
                ResultingHealthState = healthState.ToString(),
                SpellIndex = spell,
                SourceObjectId = sourceId,
                TargetObjectId = targetId
            })
        };
        if (!relevant && mobParticipation.TryGetValue(targetId, out var prior)) observation = observation with { EncounterId = prior.EncounterId };
        Persist(observation);
        if (delta < 0 && Tracked(source) && !actors.IsTerminal(targetId)
            && target.Kind is ActivityActorKind.Mob or ActivityActorKind.Unknown)
        {
            if (!mobParticipation.TryGetValue(targetId, out var participation)
                || target.LifetimeId != Guid.Empty && participation.LifetimeId != Guid.Empty && participation.LifetimeId != target.LifetimeId)
                participation = new MobParticipation(target.LifetimeId, session?.Id, encounter?.Id, false, false, false, new(), at);
            participation.Participants[source.Id] = source;
            mobParticipation[targetId] = participation with
            {
                LifetimeId = target.LifetimeId,
                EncounterId = encounter?.Id ?? participation.EncounterId,
                Self = participation.Self || source.Relationship == ActorRelationship.Self,
                Party = true,
                LastObservedAtUtc = Max(participation.LastObservedAtUtc, at)
            };
        }
        if (terminal && mobParticipation.TryGetValue(targetId, out var eligible) && !eligible.Terminal)
        {
            if (target.Kind == ActivityActorKind.Mob) MobKilled(observation, targetId, target, eligible, healthState);
            else if (target.Kind == ActivityActorKind.Unknown)
                pendingTerminals.Add(new PendingTerminal(targetId, observation,
                    eligible with { Participants = new Dictionary<string, ActivityActor>(eligible.Participants) }, healthState, at.AddSeconds(2)));
        }
    }

    private void EnsureEncounter(DateTime at, ActivityEncounterKind kind)
    {
        if (session is null || map is null) return;
        if (encounter is null)
        {
            timedOutEncounter = null;
            encounter = new ActivityEncounter
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                Number = ++encounterNumber,
                RawMapId = map.RawLocationId,
                LocationName = map.DisplayName,
                StartedAtUtc = at,
                LastCombatAtUtc = at,
                LastObservedAtUtc = at,
                State = ActivityState.Open,
                Kind = kind
            };
            SaveHeaders();
        }
        else
        {
            encounter = encounter with
            {
                Revision = encounter.Revision + 1,
                LastCombatAtUtc = Max(encounter.LastCombatAtUtc, at),
                LastObservedAtUtc = Max(encounter.LastObservedAtUtc, at),
                Kind = Combine(encounter.Kind, kind)
            };
            headersDirty = true;
        }
    }

    private void MobKilled(ActivityEvent health, long objectId, ActivityActor mob, MobParticipation participation, PacketFieldState state)
    {
        if (participation.Terminal || health.SessionId != participation.SessionId || participation.SessionId is null
            || participation.LifetimeId != Guid.Empty && participation.LifetimeId != mob.LifetimeId) return;
        mobParticipation[objectId] = participation with
        { Terminal = true, LifetimeId = mob.LifetimeId, LastObservedAtUtc = Max(participation.LastObservedAtUtc, health.CapturedAtUtc) };
        actors.MarkTerminal(objectId, health.CapturedAtUtc);
        Persist(WithTarget(health with
        {
            Id = Guid.NewGuid(),
            OriginKey = health.OriginKey is { } key ? key + ":MobKilled" : null,
            Kind = ActivityEventKind.MobKilled,
            Amount = 0,
            EncounterId = participation.EncounterId,
            Evidence = state == PacketFieldState.Missing ? ActivityEvidence.Inferred : ActivityEvidence.Confirmed,
            ParticipatedSelf = participation.Self,
            ParticipatedParty = participation.Party,
            MobIndex = mob.MobIndex,
            MobUniqueName = mob.MobUniqueName,
            DetailsJson = JsonSerializer.Serialize(new
            {
                SourceHealthEventId = health.Id,
                MobLifetimeId = mob.LifetimeId,
                TerminalEvidence = state == PacketFieldState.Missing ? "MissingResultingHealth" : "ZeroHealth",
                Participants = participation.Participants.Values.Take(40).ToArray()
            })
        }, mob));
        if (!participation.Self && !participation.Party) return;
        foreach (var award in fameCollector.ObserveMobKill(health.CapturedAtUtc))
            if (!persistence.Amend(award)) { storageSuspended = true; fameCollector.Reset(); return; }
    }

    private void ResolvePendingTerminals(long objectId, DateTime at)
    {
        var mob = actors.Resolve(objectId);
        if (mob.Kind != ActivityActorKind.Mob) return;
        foreach (var candidate in pendingTerminals.Where(x => x.ObjectId == objectId && x.ExpiresAt >= at).ToArray())
        {
            var current = mobParticipation.GetValueOrDefault(objectId);
            if (current is not null && !current.Terminal)
            {
                var amended = WithTarget(candidate.Health, mob) with { Revision = candidate.Health.Revision + 1 };
                persistence.Amend(amended);
                if (candidate.Participation.LifetimeId == Guid.Empty || candidate.Participation.LifetimeId == mob.LifetimeId)
                    MobKilled(amended, objectId, mob, candidate.Participation, candidate.HealthState);
            }
            pendingTerminals.Remove(candidate);
        }
    }

    private void Silver(TakeSilverEvent value, DateTime at)
    {
        var picker = actors.Resolve(value.ObjectId);
        if (!Tracked(picker) || value.GrossRaw is null) return;
        EnsureSession(at);
        TouchRelevant(at);
        // Omitted taxes mean no tax for every tracked picker, as for the local player.
        var netKnown = value.NetRaw is not null;
        Persist(WithSource(Stamp(ActivityEventKind.Silver, at, Origin(value, "Silver")), picker) with
        {
            GameTimestamp = value.TimeStamp,
            SilverRaw = netKnown ? value.NetRaw!.Value : 0,
            HasKnownNetSilver = netKnown,
            DetailsJson = JsonSerializer.Serialize(new
            {
                value.GrossRaw,
                value.ClusterTaxRaw,
                value.GuildTaxRaw,
                value.AlliancePenaltyRaw,
                NetRaw = netKnown ? value.NetRaw : null,
                value.GrossState,
                value.ClusterTaxState,
                value.GuildTaxState,
                value.AlliancePenaltyState,
                value.TargetEntityId
            })
        });
    }

    private void Death(DiedEvent value, DateTime at)
    {
        var killer = actors.Resolve(value.KillerObjectId, value.KillerName, value.KillerGuild);
        var victim = actors.Resolve(value.VictimObjectId, value.VictimName, value.VictimGuild);
        // A name alone cannot establish that an otherwise unbound killer/victim is a player.
        if (killer.LifetimeId == Guid.Empty && killer.CharacterId is null && !Tracked(killer))
            killer = killer with { Kind = ActivityActorKind.Unknown };
        if (victim.LifetimeId == Guid.Empty && victim.CharacterId is null && !Tracked(victim))
            victim = victim with { Kind = ActivityActorKind.Unknown };
        if (!Tracked(killer) && !Tracked(victim)) return;
        EnsureSession(at);
        TouchRelevant(at);
        // Every Died packet is a death; key 17 marks it lethal. Arena deaths omit the flag and are
        // counted as non-lethal deaths. A repeat packet for a victim already dead is only evidence.
        var repeated = actors.IsTerminal(value.VictimObjectId);
        var lethal = value.IsLethal == true;
        var observation = WithTarget(WithSource(Stamp(repeated ? ActivityEventKind.OutcomeEvidence : ActivityEventKind.Death,
            at, Origin(value, "Died")), killer), victim) with
        {
            Evidence = repeated ? ActivityEvidence.Supplemental : ActivityEvidence.Confirmed,
            DetailsJson = JsonSerializer.Serialize(new { Signal = "Died", value.IsLethal, value.LethalityState, Lethal = lethal ? 1 : 0 })
        };
        Persist(observation);
        RememberUnidentifiedDeath(value, observation);
        if (lethal && victim.Relationship == ActorRelationship.Self && session is not null)
        { session = session with { Revision = session.Revision + 1, HasLocalDeath = true }; headersDirty = true; }
        if (!repeated && value.VictimObjectId is { } objectId) actors.MarkTerminal(objectId, at);
    }

    private void Knockdown(KnockedDownEvent value, DateTime at)
    {
        var source = actors.Resolve(value.KillerObjectId, value.KillerName);
        var target = actors.Resolve(value.VictimObjectId, value.VictimName);
        if (!Tracked(source) && !Tracked(target)) return;
        EnsureSession(at);
        TouchRelevant(at);
        Persist(WithTarget(WithSource(Stamp(ActivityEventKind.Knockdown, at, Origin(value, "Knockdown")), source), target));
    }

    private void OutcomeEvidence(BaseEvent packet, long? sourceId, long? targetId, string? targetName, DateTime at, string signal)
    {
        var source = actors.Resolve(sourceId);
        var target = actors.Resolve(targetId, targetName);
        if (!Tracked(source) && !Tracked(target)) return;
        Persist(WithTarget(WithSource(Stamp(ActivityEventKind.OutcomeEvidence, at, Origin(packet, signal)), source), target) with
        { Evidence = ActivityEvidence.Supplemental, DetailsJson = JsonSerializer.Serialize(new { Signal = signal }) });
    }

    private void RecordFame(UpdateFameEvent packet, DateTime at)
    {
        if (packet.ActorObjectIdState == PacketFieldState.Invalid) { fameCollector.Reset(); return; }
        var actor = packet.ActorObjectId is { } actorId ? actors.Resolve(actorId) : actors.Local;
        var match = fameCollector.Match(packet, packet.ActorObjectId, at);
        var award = ActivityFameCollector.Award(packet);
        if (!Tracked(actor) || !(award is > 0 || packet.RawFixedPointComponents.GetValueOrDefault((byte)2) is > 0)) return;
        EnsureSession(at);
        TouchRelevant(at);
        var observation = WithSource(Stamp(ActivityEventKind.Fame, at, Origin(packet, "Fame")), actor) with
        {
            Amount = award ?? 0,
            Evidence = award is null ? ActivityEvidence.Supplemental : ActivityEvidence.Confirmed,
            PayloadVersion = 4,
            DetailsJson = JsonSerializer.Serialize(new
            {
                FameCategory = match.Category,
                Category = match.Category.ToString(),
                ClassificationSource = match.Category == ActivityFameCategory.Unknown ? null : "AchievementProgress",
                match.AchievementIndexes,
                packet.ActorObjectId,
                packet.ActorObjectIdState,
                packet.TotalPlayerFame,
                packet.FameWithZoneMultiplier,
                packet.ZoneFame,
                packet.Multiplier,
                packet.PremiumFame,
                packet.SatchelFame,
                packet.BonusFactor,
                packet.ComponentStates,
                packet.RawFixedPointComponents,
                packet.ReportedBonusIncrement,
                packet.QuickLearnFame,
                packet.QuickLearnSilverCost,
                UsesEnhancedAward = packet.QuickLearnFame is not null,
                OrdinaryAward = packet.ObservedAward,
                CalculationVersion = 5,
                Award = award
            })
        };
        Persist(fameCollector.ObserveUnmatchedAward(packet, match, fameCollector.ObserveAward(packet, observation)));
    }

    internal ActivityEvent Stamp(ActivityEventKind kind, DateTime at, string? origin) => new()
    {
        Id = Guid.NewGuid(),
        OriginKey = origin,
        SessionId = awaitingContext ? null : session?.Id,
        EncounterId = awaitingContext ? null : encounter?.Id,
        Server = actors.HasIdentity ? actors.ServerId?.ToString(CultureInfo.InvariantCulture) ?? "" : "",
        CharacterId = actors.HasIdentity ? actors.CharacterId : "",
        CharacterName = actors.HasIdentity ? actors.CharacterName : "",
        CapturedAtUtc = at,
        CaptureOrder = ++captureOrder,
        CaptureGeneration = captureGeneration,
        MapEpoch = mapEpoch,
        Kind = kind,
        Evidence = ActivityEvidence.Confirmed,
        RawMapId = map?.RawLocationId ?? "",
        LocationName = map?.DisplayName ?? "Unknown",
        MapKind = map?.Kind.ToString() ?? "Unknown",
        InstanceId = map?.InstanceId?.ToString(),
        ParentMapId = map?.SourceClusterId ?? map?.ParentClusterId
    };

    internal ActivityActor ResolveActor(long? objectId, string? name = null)
    {
        var actor = actors.Resolve(objectId, name);
        return CanRecord ? actor : actor with
        {
            Id = $"unbound:{captureGeneration}:{actor.Name}",
            CharacterId = null,
            // Without map confirmation only the party roster can classify a named player.
            Relationship = actors.RosterRelationship(name)
        };
    }
    internal ActivityActor LocalActor => actors.Local;
    internal int? ServerId => actors.HasIdentity ? actors.ServerId : null;
    internal bool HasContext => CanRecord;
    internal long MapEpoch => mapEpoch;
    internal ActivityEvent RecordLoot(ActivityEvent value, bool hasConfirmedPickupEvidence = false, bool preserveEncounterAssociation = false)
    {
        // Pickups seen before the character is confirmed still need their server for prices and identities.
        if (value.Server.Length == 0 && player.AlbionServer?.Id is int server and > 0)
            value = value with { Server = server.ToString(CultureInfo.InvariantCulture) };
        value = valuations.Observe(FillAffiliations(value));
        if (Tracked(value.SourceRelationship) && CanRecord &&
            (value.SessionId is null || value.SessionId == session?.Id) &&
            (value.Evidence == ActivityEvidence.Confirmed || hasConfirmedPickupEvidence))
        {
            EnsureSession(value.CapturedAtUtc);
            TouchRelevant(value.CapturedAtUtc);
            if (value.SessionId is null) value = value with
            {
                SessionId = session?.Id,
                EncounterId = preserveEncounterAssociation ? value.EncounterId : encounter?.Id
            };
        }
        Persist(value);
        return value;
    }
    internal ActivityEvent AmendLoot(ActivityEvent value)
    {
        value = valuations.Observe(FillAffiliations(value));
        persistence.Amend(value);
        return value;
    }

    // The identity directory outlives map bindings, so it supplies guild and alliance
    // for looters and looted players that are no longer, or not yet, in view.
    internal ActivityEvent FillAffiliations(ActivityEvent value)
    {
        if (value.Kind != ActivityEventKind.Loot) return value;
        int? server = int.TryParse(value.Server, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        if (value.SourceKind == ActivityActorKind.Player && (value.SourceGuild.Length == 0 || value.SourceAlliance.Length == 0)
            && identities.TryGetByName(server, value.SourceName, out var looter))
            value = value with
            {
                SourceGuild = value.SourceGuild.Length == 0 ? looter.GuildName : value.SourceGuild,
                SourceAlliance = value.SourceAlliance.Length == 0 ? looter.AllianceName : value.SourceAlliance
            };
        if (string.Equals(value.LootSourceKind, "Player", StringComparison.Ordinal)
            && (value.TargetGuild.Length == 0 || value.TargetAlliance.Length == 0)
            && identities.TryGetByName(server, value.LootSourceName, out var owner))
            value = value with
            {
                TargetGuild = value.TargetGuild.Length == 0 ? owner.GuildName : value.TargetGuild,
                TargetAlliance = value.TargetAlliance.Length == 0 ? owner.AllianceName : value.TargetAlliance
            };
        return value;
    }

    private void IdentityChanged(PlayerIdentitySnapshot identity)
    {
        if (string.IsNullOrWhiteSpace(identity.GuildName) && string.IsNullOrWhiteSpace(identity.AllianceName)) return;
        Collect(() => loot.RefreshAffiliations(identity.PlayerName));
    }

    internal ActivityActor? ResolvePartyRecipient(IReadOnlyCollection<Guid> playerGuids)
        => actors.PartyRecipientName(playerGuids) is { } name ? ResolveActor(null, name) : null;
    internal void RecordResource(ActivityEvent value)
    {
        if (!CanRecord || value.Evidence != ActivityEvidence.Confirmed || !Tracked(value.SourceRelationship)
            || value.MapEpoch != mapEpoch || value.SessionId is { } originalSession && originalSession != session?.Id) return;
        value = valuations.Observe(value);
        EnsureSession(value.CapturedAtUtc);
        // Fishing confirmation can trail a later first combat event. The
        // first confirmed roaming observation still determines session entry.
        if (value.SessionId is null && session is { Kind: ActivitySessionKind.Roaming } current
            && value.CapturedAtUtc < current.StartedAtUtc && value.CapturedAtUtc >= mapEnteredAt
            && current.ObservationStartedAtUtc == current.StartedAtUtc && visits.Count == 1)
        {
            visits[0] = visits[0] with { StartedAtUtc = value.CapturedAtUtc };
            session = current with
            {
                Revision = current.Revision + 1,
                StartedAtUtc = value.CapturedAtUtc,
                ObservationStartedAtUtc = value.CapturedAtUtc,
                VisitsJson = JsonSerializer.Serialize(visits)
            };
            headersDirty = true;
        }
        TouchRelevant(value.CapturedAtUtc);
        // Enrichment retains the reward's original encounter association. A
        // gathering award neither opens combat nor refreshes its inactivity.
        Persist(value with { SessionId = value.SessionId ?? session?.Id });
    }
    internal void RetractLoot(Guid id, int revision) => persistence.Retract(id, revision);
    internal void RequestEstimate(int itemId, string uniqueName, int quality)
    {
        if (ServerId is { } server) valuations.Request(server, itemId, uniqueName, quality);
    }
    internal Task FillLootPricesAsync(IReadOnlyCollection<ActivityLootPrice> prices) => persistence.FillMissingLootPricesAsync(prices);
    internal void Collect(Action action) { lock (sync) { if (!disposed) action(); } }
    internal void ObserveInventoryRearrangement(RequestPacket packet)
    {
        lock (sync)
        {
            var at = Utc(packet.CapturedAt);
            if (disposed || !CanRecord || at < mapEnteredAt || !SameConnection(packet.ConnectionId, packet.MessageIdentity)) return;
            gathering.InventoryIntent(at);
            loot.DisruptLocalAcquisition(at);
        }
    }
    internal static ActivityEvent WithSource(ActivityEvent value, ActivityActor actor) => value with
    {
        SourceActorId = actor.Id,
        SourceName = actor.Name,
        SourceGuild = actor.Guild,
        SourceAlliance = actor.Alliance,
        SourceKind = actor.Kind,
        SourceRelationship = actor.Relationship
    };
    internal static ActivityEvent WithTarget(ActivityEvent value, ActivityActor actor) => value with
    {
        TargetActorId = actor.Id,
        TargetName = actor.Name,
        TargetGuild = actor.Guild,
        TargetAlliance = actor.Alliance,
        TargetKind = actor.Kind,
        TargetRelationship = actor.Relationship
    };

    private void Persist(ActivityEvent value)
    {
        if (!persistence.Create(value)) { storageSuspended = true; gathering.Reset(); fameCollector.Reset(); }
        if (session is not null && value.SessionId == session.Id)
        {
            session = session with
            {
                Revision = session.Revision + 1,
                LastObservedAtUtc = Max(session.LastObservedAtUtc, value.CapturedAtUtc)
            }; headersDirty = true;
        }
    }

    private void CloseEncounter(DateTime at, ActivityEndReason reason, bool provisional = false)
    {
        timedOutEncounter = null;
        if (encounter is null) return;
        var closed = encounter with
        {
            Revision = encounter.Revision + 1,
            State = reason == ActivityEndReason.Interrupted ? ActivityState.Interrupted : ActivityState.Closed,
            ClosedAtUtc = Max(at, encounter.LastObservedAtUtc),
            EndReason = reason
        };
        QueueEncounter(closed);
        if (provisional) timedOutEncounter = closed;
        encounter = null;
    }

    private ActivitySession? CloseSession(DateTime at, ActivityEndReason reason)
    {
        CloseEncounter(at, reason);
        unidentifiedDeaths.Clear();
        gathering.Reset();
        fameCollector.Reset();
        if (session is null) return null;
        CloseObservation(at);
        lastRunClosedAt = at;
        var ended = session with
        {
            Revision = session.Revision + 1,
            State = reason == ActivityEndReason.Interrupted ? ActivityState.Interrupted : ActivityState.Closed,
            IsPartial = session.IsPartial || reason == ActivityEndReason.Interrupted,
            ClosedAtUtc = Max(at, session.LastObservedAtUtc),
            LastObservedAtUtc = Max(at, session.LastObservedAtUtc),
            EndReason = reason
        };
        QueueSession(ended);
        session = null;
        locationObservation = null;
        resumable = null;
        mobParticipation.Clear();
        pendingTerminals.Clear();
        headersDirty = false;
        return ended;
    }

    private void Expire(DateTime at, bool fromTimer = false)
    {
        if (!fromTimer) RestoreTimedOutEncounter(at);
        if (encounter is not null && at >= encounter.LastCombatAtUtc + EncounterIdle)
            CloseEncounter(encounter.LastCombatAtUtc + EncounterIdle, ActivityEndReason.Inactivity, provisional: fromTimer);
        ExpirePlaySession(at);
        if (!reentryConfirmed && resumable?.ClosedAtUtc is { } exited && at - exited > ReentryGrace) resumable = null;
        pendingTerminals.RemoveAll(x => x.ExpiresAt < at);
        // Loot and fishing correlations follow the captured timeline. A wall-clock tick during
        // delayed processing would settle them before their later packets are observed.
        if (!fromTimer)
        {
            gathering.Expire(at);
            loot.Expire(at);
        }
        // Combat/fishing deadlines stay exact; broad map-cache maintenance does
        // not need to scan every entity again for each incoming packet.
        if (at - lastCacheSweep < TimeSpan.FromSeconds(1)) return;
        lastCacheSweep = at;
        actors.Expire(at);
        foreach (var key in mobParticipation.Where(x => at - x.Value.LastObservedAtUtc > ActivityActorContext.BindingGrace
            && (x.Value.Terminal || !actors.HasLifetime(x.Key, x.Value.LifetimeId))).Select(x => x.Key).ToArray())
            mobParticipation.Remove(key);
    }

    private void RestoreTimedOutEncounter(DateTime at)
    {
        if (timedOutEncounter is not { } previous) return;
        if (session?.Id != previous.SessionId || map?.RawLocationId != previous.RawMapId
            || at >= previous.LastCombatAtUtc + EncounterIdle)
        {
            timedOutEncounter = null;
            return;
        }
        if (at < previous.StartedAtUtc) return;
        // Capture callbacks can be delayed by more than seven seconds. A timer
        // must not split combat that was continuous on the captured timeline.
        encounter = previous with
        {
            Revision = previous.Revision + 1,
            State = ActivityState.Open,
            ClosedAtUtc = null,
            EndReason = ActivityEndReason.None
        };
        timedOutEncounter = null;
        headersDirty = true;
    }

    private void Tick()
    {
        lock (sync)
        {
            if (disposed) return;
            try { Expire(DateTime.UtcNow, fromTimer: true); if (headersDirty) SaveHeaders(); }
            catch (Exception error) { Log.Warning(error, "Failed to update activity recording state."); }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveHeaders()
    {
        // A new play session is saved before the first run that refers to it.
        SavePlaySession();
        if (session is not null) QueueSession(session);
        if (encounter is not null) QueueEncounter(encounter);
        headersDirty = false;
        lastHeaderSave = DateTime.UtcNow;
    }

    private void QueueSession(ActivitySession value)
    {
        if (!persistence.EnqueueSession(value))
        { if (!deferredSessions.TryGetValue(value.Id, out var old) || value.Revision > old.Revision) deferredSessions[value.Id] = value; storageSuspended = true; }
    }
    private void QueueEncounter(ActivityEncounter value)
    {
        if (!persistence.EnqueueEncounter(value))
        { if (!deferredEncounters.TryGetValue(value.Id, out var old) || value.Revision > old.Revision) deferredEncounters[value.Id] = value; storageSuspended = true; }
    }
    private bool SameConnection(string value, PhotonMessageIdentity? identity)
    {
        if (!string.IsNullOrEmpty(connectionId) && !string.IsNullOrEmpty(value)
            && !string.Equals(connectionId, value, StringComparison.Ordinal)) return false;
        if (connectionIdentity is null || identity is null) return true;
        if (connectionIdentity.ReceiverGeneration != identity.ReceiverGeneration
            || connectionIdentity.Challenge != identity.Challenge) return false;

        var joinedStream = connectionIdentity.DirectionalConnection;
        var observedStream = identity.DirectionalConnection;
        if (string.Equals(joinedStream, observedStream, StringComparison.Ordinal))
            return connectionIdentity.PeerId == identity.PeerId;

        // Photon peer IDs differ by direction. Requests must match the reverse of
        // the Join response's endpoints, not its inbound peer ID.
        var joinedSeparator = joinedStream.IndexOf('>');
        var observedSeparator = observedStream.IndexOf('>');
        return joinedSeparator > 0 && joinedSeparator < joinedStream.Length - 1
            && joinedSeparator == joinedStream.LastIndexOf('>')
            && observedSeparator > 0 && observedSeparator < observedStream.Length - 1
            && observedSeparator == observedStream.LastIndexOf('>')
            && joinedStream.AsSpan(0, joinedSeparator).SequenceEqual(observedStream.AsSpan(observedSeparator + 1))
            && joinedStream.AsSpan(joinedSeparator + 1).SequenceEqual(observedStream.AsSpan(0, observedSeparator));
    }

    private bool Accept(string? origin, string kind, DateTime at)
    {
        if (origin is null) return true;
        var key = origin + ":" + kind;
        if (!origins.TryAdd(key, at)) return false;
        originOrder.Enqueue(key);
        while (originOrder.Count > 20000) origins.Remove(originOrder.Dequeue());
        return true;
    }

    internal static string? Origin(BaseEvent value, string kind, int entry = 0) => value.MessageIdentity is { } identity
        ? $"{identity.OriginKey}:{entry}:{kind}" : null;
    internal static string? Origin(BaseOperation value, string kind, int entry = 0) => value.MessageIdentity is { } identity
        ? $"{identity.OriginKey}:{entry}:{kind}" : null;
    private static bool Tracked(ActivityActor actor) => Tracked(actor.Relationship);
    internal static bool Tracked(ActorRelationship relation) => relation is ActorRelationship.Self or ActorRelationship.Party;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static string InstanceKey(ActivityLocationDescriptor value) => $"{value.Kind}:{value.InstanceId?.ToString() ?? value.RawLocationId}";
    private static bool IsHub(ActivityMapKind kind) => kind is ActivityMapKind.Hub or ActivityMapKind.Island or ActivityMapKind.Hideout;
    private static bool MultiFloor(ActivityMapKind kind) => kind is ActivityMapKind.RandomDungeon or ActivityMapKind.Expedition or ActivityMapKind.AbyssalDepths;
    private static bool IsInstance(ActivityMapKind kind) => kind is not (ActivityMapKind.Unknown or ActivityMapKind.OpenWorld
        or ActivityMapKind.Hub or ActivityMapKind.Island or ActivityMapKind.Hideout);
    private static ActivitySessionKind SessionKind(ActivityMapKind kind) => kind switch
    {
        ActivityMapKind.RandomDungeon => ActivitySessionKind.Dungeon,
        ActivityMapKind.CorruptedDungeon => ActivitySessionKind.Corrupted,
        ActivityMapKind.Hellgate => ActivitySessionKind.Hellgate,
        ActivityMapKind.Mists => ActivitySessionKind.Mists,
        ActivityMapKind.MistsDungeon => ActivitySessionKind.MistsDungeon,
        ActivityMapKind.Expedition => ActivitySessionKind.Expedition,
        ActivityMapKind.AbyssalDepths => ActivitySessionKind.AbyssalDepths,
        ActivityMapKind.Arena => ActivitySessionKind.Arena,
        ActivityMapKind.DragonArea => ActivitySessionKind.DragonArea,
        ActivityMapKind.StaticDungeon => ActivitySessionKind.StaticDungeon,
        _ => ActivitySessionKind.Roaming
    };
    private static ActivityEncounterKind Classify(ActivityActor source, ActivityActor target) =>
        source.Kind == ActivityActorKind.Mob || target.Kind == ActivityActorKind.Mob ? ActivityEncounterKind.Pve
        : source.Kind == ActivityActorKind.Player && target.Kind == ActivityActorKind.Player
            && source.Id != target.Id && !(Tracked(source) && Tracked(target)) ? ActivityEncounterKind.Pvp : ActivityEncounterKind.Unknown;
    private static ActivityEncounterKind Combine(ActivityEncounterKind a, ActivityEncounterKind b) =>
        a == ActivityEncounterKind.Mixed || b == ActivityEncounterKind.Mixed
            || a == ActivityEncounterKind.Pve && b == ActivityEncounterKind.Pvp
            || a == ActivityEncounterKind.Pvp && b == ActivityEncounterKind.Pve ? ActivityEncounterKind.Mixed
        : a is ActivityEncounterKind.Unknown or ActivityEncounterKind.Healing ? b
        : b is ActivityEncounterKind.Unknown or ActivityEncounterKind.Healing ? a : b;

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(UserSettings.ActivityRecordingEnabled)) SetRecordingEnabled(settings.UserSettings.ActivityRecordingEnabled); }
    private void StorageChanged(ActivityRecordingStatus status)
    {
        lock (sync)
        {
            var suspended = persistence.Status.IsSuspended;
            if (suspended) { PauseForStorage(DateTime.UtcNow); storageSuspended = true; }
            else
            {
                storageSuspended = false;
                ResumeDeferredPlaySessions();
                foreach (var header in deferredSessions.Values.ToArray())
                    if (persistence.EnqueueSession(header)) deferredSessions.Remove(header.Id);
                foreach (var header in deferredEncounters.Values.ToArray())
                    if (persistence.EnqueueEncounter(header)) deferredEncounters.Remove(header.Id);
                if (deferredGap is { } gap && persistence.Create(gap)) deferredGap = null;
                storageSuspended = persistence.Status.IsSuspended;
                if (!storageSuspended) ResumeAfterStorage(DateTime.UtcNow);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    // Unlike a capture reset, the map, character and inventory remain known while
    // writes are refused. Only in-flight correlations are dropped, and the session
    // records an explicit gap that closes when the database accepts writes again.
    private void PauseForStorage(DateTime at)
    {
        if (storageGap) return;
        storageGap = true;
        loot.ResetRecordingCorrelations();
        gathering.Reset();
        fameCollector.Reset();
        if (awaitingContext || !enabled || session is null || captureGap) return;
        RecordGap(at, "StorageSuspended");
    }

    private void ResumeAfterStorage(DateTime at)
    {
        if (!storageGap) return;
        storageGap = false;
        if (session is null || !enabled || awaitingContext || map is null || captureGap || session.ObservationStartedAtUtc is not null) return;
        OpenObservation(at);
        headersDirty = true;
        SaveHeaders();
    }

    private void SessionDeleted(Guid id)
    {
        lock (sync)
        {
            loot.ForgetSession(id);
            deferredSessions.Remove(id);
            foreach (var key in deferredEncounters.Where(x => x.Value.SessionId == id).Select(x => x.Key).ToArray()) deferredEncounters.Remove(key);
            if (deferredGap?.SessionId == id) deferredGap = null;
            if (resumable?.Id == id) resumable = null;
            if (session?.Id != id) return;
            ClearPendingActorMetadata();
            gathering.Reset();
            fameCollector.Reset();
            session = null; encounter = null; timedOutEncounter = null; headersDirty = false;
            mapEnteredAt = DateTime.UtcNow;
            mobParticipation.Clear(); pendingTerminals.Clear();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void UnassignedLootDeleted()
    { lock (sync) loot.ForgetUnassigned(); Changed?.Invoke(this, EventArgs.Empty); }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            ResolvePending(DateTime.UtcNow);
            gathering.Reset();
            ClosePlaySession(DateTime.UtcNow, ActivityEndReason.Interrupted);
            SaveHeaders();
            captureSuspended = true;
        }
        await persistence.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        timer.Dispose(); loot.Dispose();
        settings.UserSettings.PropertyChanged -= SettingsChanged;
        persistence.StatusChanged -= StorageChanged;
        persistence.SessionDeleted -= SessionDeleted;
        persistence.UnassignedLootDeleted -= UnassignedLootDeleted;
        player.OnPlayerStateChanged -= PeriodOwnerChanged;
        identities.IdentityChanged -= IdentityChanged;
    }

    private sealed record MobParticipation(Guid LifetimeId, Guid? SessionId, Guid? EncounterId,
        bool Self, bool Party, bool Terminal, Dictionary<string, ActivityActor> Participants, DateTime LastObservedAtUtc);
    private sealed record PendingTerminal(long ObjectId, ActivityEvent Health, MobParticipation Participation,
        PacketFieldState HealthState, DateTime ExpiresAt);
}

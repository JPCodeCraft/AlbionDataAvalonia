using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Party;
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
public sealed class ActivityTrackerService : IDisposable
{
    private static readonly TimeSpan EncounterIdle = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan SessionIdle = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ReentryGrace = TimeSpan.FromMinutes(2);
    private readonly object sync = new();
    private readonly PlayerState player;
    private readonly SettingsManager settings;
    private readonly ActivityPersistence persistence;
    private readonly ActivityActorContext actors;
    private readonly ActivityLootCollector loot;
    private readonly Timer timer;
    private readonly Dictionary<string, DateTime> origins = new(StringComparer.Ordinal);
    private readonly Queue<string> originOrder = new();
    private readonly Dictionary<long, MobParticipation> mobParticipation = new();
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
    private bool disposed;
    private bool headersDirty;
    private DateTime lastHeaderSave;

    public ActivityTrackerService(PlayerState player, PartyTrackerService party, MobsService mobs,
        ItemEstimatedMarketValueService values, ItemsIdsService items, SettingsManager settings, ActivityPersistence persistence)
    {
        this.player = player;
        this.settings = settings;
        this.persistence = persistence;
        enabled = settings.UserSettings.ActivityRecordingEnabled;
        actors = new ActivityActorContext(party, mobs);
        loot = new ActivityLootCollector(this, values, items);
        timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        settings.UserSettings.PropertyChanged += SettingsChanged;
        persistence.StatusChanged += StorageChanged;
        persistence.SessionDeleted += SessionDeleted;
        persistence.UnassignedLootDeleted += UnassignedLootDeleted;
    }

    public event EventHandler? Changed;
    public Guid? CurrentSessionId { get { lock (sync) return session?.Id; } }
    public Guid? CurrentEncounterId { get { lock (sync) return encounter?.Id; } }
    public bool IsRecordingEnabled { get { lock (sync) return enabled; } }
    public bool IsAwaitingContext { get { lock (sync) return awaitingContext; } }
    public bool IsCaptureSuspended { get { lock (sync) return captureSuspended; } }
    public Task<ActivityLiveTimelineSnapshot> GetLiveTimelineAsync(Guid? sessionId, ActivityScope scope, DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        ActivitySession? liveSession;
        ActivityEncounter? liveEncounter;
        lock (sync)
        {
            liveSession = session?.Id == sessionId ? session : null;
            liveEncounter = encounter?.SessionId == sessionId ? encounter : null;
        }
        return persistence.GetLiveTimelineAsync(sessionId, scope, endUtc, liveSession, liveEncounter, cancellationToken);
    }
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
                    : session is not null ? "Recording session activity." : "Ready. A session begins with relevant activity.";
            }
        }
    }

    public void SetRecordingEnabled(bool value)
    {
        settings.UserSettings.ActivityRecordingEnabled = value;
        lock (sync)
        {
            if (enabled == value) return;
            enabled = value;
            if (value && map is not null) { mapEnteredAt = DateTime.UtcNow; partialEntry = true; }
            if (!value) CloseSession(DateTime.UtcNow, ActivityEndReason.Disabled);
            loot.Reset();
            mobParticipation.Clear();
            pendingTerminals.Clear();
            SaveHeaders();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void FinishSession(Guid? expectedSessionId = null)
    {
        lock (sync)
        {
            if (expectedSessionId is { } expected && session?.Id != expected) return;
            CloseSession(DateTime.UtcNow, ActivityEndReason.Finished);
            resumable = null;
            loot.ResetCandidates();
            mobParticipation.Clear();
            pendingTerminals.Clear();
            mapEnteredAt = DateTime.UtcNow;
            SaveHeaders();
        }
        _ = persistence.FlushAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OnCaptureReset()
    {
        lock (sync)
        {
            Suspend(DateTime.UtcNow);
            captureGeneration++;
            origins.Clear();
            originOrder.Clear();
            captureSuspended = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OnCaptureSuspended()
    {
        lock (sync) Suspend(DateTime.UtcNow);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Suspend(DateTime at)
    {
        if (!awaitingContext && enabled && session is not null)
        {
            var gap = Stamp(ActivityEventKind.CaptureGap, at, null) with
            { EncounterId = null, DetailsJson = JsonSerializer.Serialize(new { State = "Suspended" }) };
            awaitingContext = true;
            CloseEncounter(at, ActivityEndReason.CaptureGap);
            if (!persistence.Create(gap)) deferredGap = gap;
            CloseObservation(at);
            session = session with { Revision = session.Revision + 1, LastObservedAtUtc = at, IsPartial = true };
            headersDirty = true;
            SaveHeaders();
        }
        captureSuspended = true;
        awaitingContext = true;
        actors.Suspend();
        map = null;
        loot.Reset();
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
            if (!awaitingContext && (at < mapEnteredAt || !SameConnection(packet.ConnectionId, packet.MessageIdentity))) return;
            Expire(at);
            switch (packet)
            {
                case NewCharacterEvent character: actors.Character(character); break;
                case NewMobEvent mob when mob.ObjectId is > 0:
                    actors.Mob(mob.ObjectId.Value, mob.MobIndex, mob.CurrentHealth);
                    ResolvePendingTerminals(mob.ObjectId.Value, at);
                    break;
                case LeaveEvent leave:
                    actors.Leave(leave.userObjectId, at);
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
                case UpdateFameEvent fame when CanRecord && (fame.ObservedAward is > 0 || fame.RawFixedPointComponents.GetValueOrDefault((byte)2) is > 0):
                    EnsureSession(at);
                    TouchRelevant(at);
                    Persist(WithSource(Stamp(ActivityEventKind.Fame, at, Origin(packet, "Fame")), actors.Local) with
                    {
                        Amount = fame.ObservedAward ?? 0,
                        Evidence = fame.ObservedAward is null ? ActivityEvidence.Supplemental : ActivityEvidence.Confirmed,
                        DetailsJson = JsonSerializer.Serialize(new
                        {
                            Category = "Unknown",
                            fame.TotalPlayerFame,
                            fame.FameWithZoneMultiplier,
                            fame.ZoneFame,
                            fame.Multiplier,
                            fame.PremiumFame,
                            fame.SatchelFame,
                            fame.BonusFactor,
                            fame.ComponentStates,
                            fame.RawFixedPointComponents,
                            fame.ReportedBonusIncrement,
                            Award = fame.ObservedAward
                        })
                    });
                    break;
                case TakeSilverEvent silver when CanRecord: Silver(silver, at); break;
                case DiedEvent death when CanRecord: Death(death, at); break;
                case KnockedDownEvent knockdown when CanRecord: Knockdown(knockdown, at); break;
                case KilledPlayerEvent kill when CanRecord:
                    OutcomeEvidence(packet, kill.KillerObjectId, kill.VictimObjectId, kill.VictimName, at, "KilledPlayer");
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
                default:
                    if (enabled && !storageSuspended) loot.Observe(packet, at);
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
            if (packet is not JoinResponse && !awaitingContext && (at < mapEnteredAt || !SameConnection(packet.ConnectionId, packet.MessageIdentity))) return;
            Expire(at);
            if (packet is JoinResponse join && join.ReturnCode == 0
                && join.userObjectId > 0 && !string.IsNullOrWhiteSpace(join.RawLocationId)) Join(join, at);
            else if (CanRecord) loot.Observe(packet, at);
            if (headersDirty) SaveHeaders();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanRecord => enabled && !storageSuspended && !awaitingContext && actors.HasIdentity;

    private void Join(JoinResponse packet, DateTime at)
    {
        var next = packet.ActivityLocation;
        var previous = map;
        var nextServer = player.AlbionServer?.Id;
        var switched = session is not null && (session.Server != nextServer?.ToString(CultureInfo.InvariantCulture)
            || session.CharacterId != (packet.userGuid?.ToString() ?? "")
            || session.CharacterId.Length == 0 && session.CharacterName != packet.playerName);
        CloseEncounter(at, switched ? ActivityEndReason.CharacterChanged : ActivityEndReason.MapTransition);
        if (switched) CloseSession(at, ActivityEndReason.CharacterChanged);
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
        map = next;
        mapEnteredAt = at;
        partialEntry = !enabled;
        awaitingContext = !actors.HasIdentity;
        captureSuspended = false;
        reentryConfirmed = resumable is { ClosedAtUtc: { } exited } old && at - exited <= ReentryGrace
            && old.Server == nextServer?.ToString(CultureInfo.InvariantCulture)
            && old.CharacterId == (packet.userGuid?.ToString() ?? "") && !old.HasLocalDeath
            && JsonSerializer.Deserialize<string[]>(old.KnownInstancesJson)?.Contains(InstanceKey(next)) == true;
        loot.Join(packet, at);
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

    private void EnsureSession(DateTime at)
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
                State = ActivityState.Open,
                ClosedAtUtc = null,
                EndReason = ActivityEndReason.None,
                ObservationStartedAtUtc = mapEnteredAt
            };
            visits.Clear();
            visits.AddRange(JsonSerializer.Deserialize<ActivityVisit[]>(old.VisitsJson) ?? []);
            knownInstances.Clear();
            foreach (var value in JsonSerializer.Deserialize<string[]>(old.KnownInstancesJson) ?? []) knownInstances.Add(value);
            visits.Add(new ActivityVisit(map.RawLocationId, map.DisplayName, mapEnteredAt, null, map.InstanceId?.ToString()));
            visitStartedAt = mapEnteredAt;
            // Existing encounter numbers remain monotonic after a resumed visit.
        }
        else
        {
            visits.Clear();
            knownInstances.Clear();
            encounterNumber = 0;
            var start = IsInstance(map.Kind) ? mapEnteredAt : at;
            visitStartedAt = start;
            session = new ActivitySession
            {
                Id = Guid.NewGuid(),
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
            visits.Add(new ActivityVisit(map.RawLocationId, map.DisplayName, start, null, map.InstanceId?.ToString()));
        }
        resumable = null;
        reentryConfirmed = false;
        TrackInstance(map);
        SaveHeaders();
        Persist(LocationEvent(visitStartedAt, null));
    }

    private ActivityEvent LocationEvent(DateTime at, string? origin) => WithSource(Stamp(ActivityEventKind.Location, at, origin), actors.Local) with
    {
        DetailsJson = JsonSerializer.Serialize(new
        {
            OriginalName = map?.DisplayName,
            map?.ParentClusterId,
            map?.SourceClusterId,
            GroupingInferred = session?.HasInferredGrouping ?? false
        })
    };

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
        visits.Add(new ActivityVisit(map.RawLocationId, map.DisplayName, at, null, map.InstanceId?.ToString()));
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
        if (delta < 0 && Tracked(source) && target.Kind is ActivityActorKind.Mob or ActivityActorKind.Unknown)
        {
            if (!mobParticipation.TryGetValue(targetId, out var participation)
                || target.LifetimeId != Guid.Empty && participation.LifetimeId != Guid.Empty && participation.LifetimeId != target.LifetimeId)
                participation = new MobParticipation(target.LifetimeId, session?.Id, encounter?.Id, false, false, false, new());
            participation.Participants[source.Id] = source;
            mobParticipation[targetId] = participation with
            {
                LifetimeId = target.LifetimeId,
                EncounterId = encounter?.Id ?? participation.EncounterId,
                Self = participation.Self || source.Relationship == ActorRelationship.Self,
                Party = true
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
        mobParticipation[objectId] = participation with { Terminal = true, LifetimeId = mob.LifetimeId };
        actors.MarkTerminal(objectId);
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
        var netKnown = value.NetRaw is not null && (picker.Relationship == ActorRelationship.Self
            || value.ClusterTaxState == PacketFieldState.Valid && value.GuildTaxState == PacketFieldState.Valid
                && value.AlliancePenaltyState == PacketFieldState.Valid);
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
        var lethal = value.IsLethal == true && !actors.IsTerminal(value.VictimObjectId);
        Persist(WithTarget(WithSource(Stamp(lethal ? ActivityEventKind.Death : ActivityEventKind.OutcomeEvidence,
            at, Origin(value, "Died")), killer), victim) with
        {
            Evidence = lethal ? ActivityEvidence.Confirmed : ActivityEvidence.Supplemental,
            DetailsJson = JsonSerializer.Serialize(new { Signal = "Died", value.IsLethal, value.LethalityState })
        });
        if (value.IsLethal == true && victim.Relationship == ActorRelationship.Self && session is not null)
        { session = session with { Revision = session.Revision + 1, HasLocalDeath = true }; headersDirty = true; }
        if (value.IsLethal == true && value.VictimObjectId is { } objectId) actors.MarkTerminal(objectId);
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
            Relationship = ActorRelationship.Unknown
        };
    }
    internal ActivityActor LocalActor => actors.Local;
    internal int? ServerId => actors.HasIdentity ? actors.ServerId : null;
    internal bool HasContext => CanRecord;
    internal long MapEpoch => mapEpoch;
    internal ActivityEvent RecordLoot(ActivityEvent value, bool hasConfirmedPickupEvidence = false, bool preserveEncounterAssociation = false)
    {
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
    internal void AmendLoot(ActivityEvent value) => persistence.Amend(value);
    internal void RetractLoot(Guid id, int revision) => persistence.Retract(id, revision);
    internal Task FillLootPricesAsync(IReadOnlyCollection<ActivityLootPrice> prices) => persistence.FillMissingLootPricesAsync(prices);
    internal void Collect(Action action) { lock (sync) { if (!disposed) action(); } }
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
        if (!persistence.Create(value)) storageSuspended = true;
        if (session is not null && value.SessionId == session.Id)
        {
            session = session with
            {
                Revision = session.Revision + 1,
                LastObservedAtUtc = Max(session.LastObservedAtUtc, value.CapturedAtUtc)
            }; headersDirty = true;
        }
    }

    private void CloseEncounter(DateTime at, ActivityEndReason reason)
    {
        if (encounter is null) return;
        QueueEncounter(encounter with
        {
            Revision = encounter.Revision + 1,
            State = reason == ActivityEndReason.Interrupted ? ActivityState.Interrupted : ActivityState.Closed,
            ClosedAtUtc = Max(at, encounter.LastObservedAtUtc),
            EndReason = reason
        });
        encounter = null;
    }

    private ActivitySession? CloseSession(DateTime at, ActivityEndReason reason)
    {
        CloseEncounter(at, reason);
        if (session is null) return null;
        CloseObservation(at);
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
        resumable = null;
        mobParticipation.Clear();
        pendingTerminals.Clear();
        headersDirty = false;
        return ended;
    }

    private void Expire(DateTime at)
    {
        if (encounter is not null && at >= encounter.LastCombatAtUtc + EncounterIdle)
            CloseEncounter(encounter.LastCombatAtUtc + EncounterIdle, ActivityEndReason.Inactivity);
        if (session is { Kind: ActivitySessionKind.Roaming } && !awaitingContext && at >= session.LastRelevantAtUtc + SessionIdle)
            CloseSession(session.LastRelevantAtUtc + SessionIdle, ActivityEndReason.Inactivity);
        if (!reentryConfirmed && resumable?.ClosedAtUtc is { } exited && at - exited > ReentryGrace) resumable = null;
        pendingTerminals.RemoveAll(x => x.ExpiresAt < at);
        actors.Expire(at);
        loot.Expire(at);
    }

    private void Tick()
    {
        lock (sync)
        {
            if (disposed) return;
            try { Expire(DateTime.UtcNow); if (headersDirty) SaveHeaders(); }
            catch (Exception error) { Log.Warning(error, "Failed to update activity recording state."); }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveHeaders()
    {
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
    private bool SameConnection(string value, PhotonMessageIdentity? identity) =>
        (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(value) || string.Equals(connectionId, value, StringComparison.Ordinal))
        && (connectionIdentity is null || identity is null || connectionIdentity.ReceiverGeneration == identity.ReceiverGeneration
            && connectionIdentity.PeerId == identity.PeerId && connectionIdentity.Challenge == identity.Challenge);

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
            if (suspended && !awaitingContext) { storageSuspended = true; Suspend(DateTime.UtcNow); }
            else if (!suspended)
            {
                storageSuspended = false;
                foreach (var header in deferredSessions.Values.ToArray())
                    if (persistence.EnqueueSession(header)) deferredSessions.Remove(header.Id);
                foreach (var header in deferredEncounters.Values.ToArray())
                    if (persistence.EnqueueEncounter(header)) deferredEncounters.Remove(header.Id);
                if (deferredGap is { } gap && persistence.Create(gap)) deferredGap = null;
                storageSuspended = persistence.Status.IsSuspended;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
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
            session = null; encounter = null; headersDirty = false;
            mapEnteredAt = DateTime.UtcNow;
            mobParticipation.Clear(); pendingTerminals.Clear();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void UnassignedLootDeleted()
    { lock (sync) loot.ForgetUnassigned(); Changed?.Invoke(this, EventArgs.Empty); }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (sync) { CloseSession(DateTime.UtcNow, ActivityEndReason.Interrupted); captureSuspended = true; }
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
    }

    private sealed record MobParticipation(Guid LifetimeId, Guid? SessionId, Guid? EncounterId,
        bool Self, bool Party, bool Terminal, Dictionary<string, ActivityActor> Participants);
    private sealed record PendingTerminal(long ObjectId, ActivityEvent Health, MobParticipation Participation,
        PacketFieldState HealthState, DateTime ExpiresAt);
}

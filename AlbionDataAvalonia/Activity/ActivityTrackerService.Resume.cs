using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityTrackerService
{
    public async Task<IReadOnlyList<ActivitySession>> GetResumeCandidatesAsync(CancellationToken cancellationToken = default)
    {
        ActivityOwner? owner;
        lock (sync)
        {
            if (disposed || session is not null)
                throw new InvalidOperationException("A session has started. Close this window and use Merge session instead.");
            owner = confirmedOwner;
        }
        await persistence.FlushAsync(cancellationToken).ConfigureAwait(false);
        return await persistence.GetResumeCandidatesAsync(owner, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResumeSessionAsync(Guid sessionId)
    {
        lock (sync) RequireResumeContext();
        await persistence.ResumeSessionAsync(sessionId, sync, previous =>
        {
            RequireResumeContext();
            if (previous.CharacterId != actors.CharacterId ||
                previous.Server != actors.ServerId?.ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Log in with the same character and server to resume this session.");
            var now = DateTime.UtcNow;
            var resumedVisits = (JsonSerializer.Deserialize<ActivityVisit[]>(previous.VisitsJson) ?? [])
                .Append(new(map!.RawLocationId, map.DisplayName, now, null, map.InstanceId?.ToString(), mapMetadata));
            var instances = (JsonSerializer.Deserialize<string[]>(previous.KnownInstancesJson) ?? []).AsEnumerable();
            if (IsInstance(map.Kind)) instances = instances.Append(InstanceKey(map));
            return previous with
            {
                Revision = previous.Revision + 1,
                State = ActivityState.Open,
                EndReason = ActivityEndReason.None,
                ClosedAtUtc = null,
                LastRelevantAtUtc = now,
                LastObservedAtUtc = Max(previous.LastObservedAtUtc, now),
                ObservationStartedAtUtc = now,
                VisitsJson = JsonSerializer.Serialize(resumedVisits),
                KnownInstancesJson = JsonSerializer.Serialize(instances.Distinct(StringComparer.OrdinalIgnoreCase))
            };
        }, result =>
        {
            session = result.Session;
            encounter = null;
            encounterNumber = result.LastEncounterNumber;
            visits.Clear();
            visits.AddRange(JsonSerializer.Deserialize<ActivityVisit[]>(session.VisitsJson) ?? []);
            knownInstances.Clear();
            knownInstances.UnionWith(JsonSerializer.Deserialize<string[]>(session.KnownInstancesJson) ?? []);
            resumable = null;
            reentryConfirmed = false;
            mapEnteredAt = session.ObservationStartedAtUtc!.Value;
            loot.ResetRecordingCorrelations();
            gathering.Reset();
            fameCollector.Reset();
            mobParticipation.Clear();
            pendingTerminals.Clear();
            deferredSessions.Remove(sessionId);
            headersDirty = false;
            Interlocked.Increment(ref periodContextVersion);
            Persist(LocationEvent(session.ObservationStartedAtUtc!.Value, null));
            SaveHeaders();
        }).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RequireResumeContext()
    {
        if (disposed) throw new InvalidOperationException("Activity recording is shutting down.");
        if (session is not null)
            throw new InvalidOperationException("A session has started. Close this window and use Merge session instead.");
        if (!enabled) throw new InvalidOperationException("Enable recording before resuming a session.");
        if (storageSuspended || persistence.Status.IsSuspended)
            throw new InvalidOperationException("Wait for Activity storage to recover before resuming a session.");
        if (!HasConfirmedContext)
            throw new InvalidOperationException("Start capture and enter a zone so your character and location can be detected, then try again.");
    }
}

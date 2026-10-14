using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityTrackerService
{
    public async Task<IReadOnlyList<ActivitySession>> GetMergeCandidatesAsync(Guid expectedCurrentSessionId,
        CancellationToken cancellationToken = default)
    {
        ActivitySession current;
        lock (sync)
        {
            if (disposed || session?.Id != expectedCurrentSessionId || session.State != ActivityState.Open)
                throw new InvalidOperationException("The current session changed. Close this window and try again.");
            current = session;
        }
        await persistence.FlushAsync(cancellationToken).ConfigureAwait(false);
        return await persistence.GetMergeCandidatesAsync(current, cancellationToken).ConfigureAwait(false);
    }

    public async Task MergeSessionAsync(Guid sourceSessionId, Guid expectedCurrentSessionId)
    {
        lock (sync)
        {
            if (disposed || storageSuspended || session?.Id != expectedCurrentSessionId || session.State != ActivityState.Open)
                throw new InvalidOperationException("The current session is not available to merge. Try again when recording resumes.");
            SaveHeaders();
        }
        await persistence.MergeSessionAsync(sourceSessionId, expectedCurrentSessionId, sync, () =>
        {
            if (disposed || storageSuspended) return null;
            SaveHeaders();
            return storageSuspended ? null : session;
        }, result =>
        {
            session = result.Session;
            visits.Clear();
            visits.AddRange(JsonSerializer.Deserialize<ActivityVisit[]>(session.VisitsJson) ?? []);
            knownInstances.Clear();
            knownInstances.UnionWith(JsonSerializer.Deserialize<string[]>(session.KnownInstancesJson) ?? []);
            encounterNumber = result.Encounters.Count;
            if (encounter is { } active) encounter = result.Encounters.Single(x => x.Id == active.Id);
            if (timedOutEncounter is { } idle) timedOutEncounter = result.Encounters.Single(x => x.Id == idle.Id);
            if (resumable?.Id == sourceSessionId) resumable = null;
            deferredSessions.Remove(sourceSessionId);
            foreach (var id in deferredEncounters.Where(x => x.Value.SessionId == sourceSessionId).Select(x => x.Key).ToArray())
                deferredEncounters.Remove(id);
            headersDirty = false;
            Interlocked.Increment(ref periodContextVersion);
        }).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

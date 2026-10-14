using AlbionDataAvalonia.Activity.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

internal sealed record ActivitySessionMergeResult(ActivitySession Session, IReadOnlyList<ActivityEncounter> Encounters);

public sealed partial class ActivityPersistence
{
    // Correlations may still hold the original session after a manual merge. Keep
    // their writes attached to the surviving session until this process ends.
    private readonly Dictionary<Guid, Guid> mergedSessions = new();

    internal Task<IReadOnlyList<ActivitySession>> GetMergeCandidatesAsync(ActivitySession current,
        CancellationToken cancellationToken) => ReadAsync<IReadOnlyList<ActivitySession>>(async ct =>
    {
        if (string.IsNullOrWhiteSpace(current.CharacterId) || string.IsNullOrWhiteSpace(current.Server)) return [];
        await using var context = contextFactory();
        return await context.ActivitySessions.AsNoTracking().Where(x => x.Id != current.Id &&
                x.State != ActivityState.Open && x.CharacterId == current.CharacterId && x.Server == current.Server)
            .OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Take(100).ToArrayAsync(ct);
    }, cancellationToken);

    internal async Task MergeSessionAsync(Guid sourceId, Guid targetId, object recordingSync,
        Func<ActivitySession?> snapshot, Action<ActivitySessionMergeResult> apply)
    {
        var command = new MergeSessionCommand(sourceId, targetId, recordingSync, snapshot, apply, NewCompletion());
        // Once accepted, closing the picker must not cancel a transaction that may have committed.
        await ControlAsync(command, CancellationToken.None).ConfigureAwait(false);
        if (command.Failure is { } failure) throw new IOException("The merged session could not be saved.", failure);
        if (command.Error is { } error) throw new InvalidOperationException(error);
    }

    private void ExecuteMerge(MergeSessionCommand command)
    {
        if (command.Completed) return;
        lock (command.RecordingSync)
        {
            var current = command.Snapshot();
            if (current?.Id != command.TargetId || current.State != ActivityState.Open)
            { command.Error = "The current session changed. Close this window and try again."; command.Completed = true; return; }
            ActivitySessionMergeResult? result;
            try
            {
                result = MergeSessionsCore(command.SourceId, current, out var error);
                command.Error = error;
            }
            catch (Exception error)
            {
                // A failed manual operation rolls back and returns to the picker.
                // Normal accepted recording writes keep their usual retry policy.
                Log.Error(error, "Could not merge Activity sessions.");
                command.Failure = error;
                command.Completed = true;
                return;
            }
            if (result is not null) command.Apply(result);
            command.Completed = true;
        }
        if (command.Error is null)
        {
            var changes = new CommittedChanges { Items = true };
            changes.Sessions.UnionWith([command.SourceId, command.TargetId]);
            NotifyCommitted(changes);
        }
    }

    // This runs on the serialized writer, briefly holding recording's lock. No
    // asynchronous callbacks run inside the transaction; capture resumes with
    // the new headers before another packet or writer batch can overwrite them.
    private ActivitySessionMergeResult? MergeSessionsCore(Guid sourceId, ActivitySession current, out string? error)
    {
        error = null;
        using var context = contextFactory();
        using var transaction = context.Database.BeginTransaction();
        var source = context.ActivitySessions.Find(sourceId);
        var savedTarget = context.ActivitySessions.Find(current.Id);
        if (source is null || savedTarget is null || source.Id == current.Id || source.State == ActivityState.Open)
        { error = "That session is no longer available to merge."; return null; }
        if (string.IsNullOrWhiteSpace(current.CharacterId) || string.IsNullOrWhiteSpace(current.Server) ||
            source.CharacterId != current.CharacterId || source.Server != current.Server)
        { error = "Only sessions from the same character and server can be merged."; return null; }

        // Ingress can enqueue headers after the merge command but before the
        // writer reaches it. Include them now; their later replay is idempotent.
        ActivityEncounter[] pendingHeaders;
        lock (acceptedSync)
            pendingHeaders = acceptedWrites.OfType<EncounterCommand>().Select(x => x.Value)
                .Where(x => x.SessionId == current.Id).GroupBy(x => x.Id)
                .Select(x => x.MaxBy(h => h.Revision)!).ToArray();
        foreach (var header in pendingHeaders)
        {
            var saved = context.ActivityEncounters.Find(header.Id);
            if (saved is null) context.ActivityEncounters.Add(header with
            { PersonalSummaryJson = EmptySummary, PartySummaryJson = EmptySummary, ObservedSummaryJson = EmptySummary });
            else if (header.Revision > saved.Revision)
                context.Entry(saved).CurrentValues.SetValues(header with
                {
                    PersonalSummaryJson = saved.PersonalSummaryJson,
                    PartySummaryJson = saved.PartySummaryJson,
                    ObservedSummaryJson = saved.ObservedSummaryJson,
                    LastObservedAtUtc = header.LastObservedAtUtc > saved.LastObservedAtUtc ? header.LastObservedAtUtc : saved.LastObservedAtUtc
                });
        }
        context.SaveChanges();

        var encounters = context.ActivityEncounters.Where(x => x.SessionId == sourceId || x.SessionId == current.Id)
            .OrderBy(x => x.StartedAtUtc).ThenBy(x => x.Id).ToArray();
        // (SessionId, Number) is unique. Vacate positive numbers before moving
        // encounters, then assign one chronological numbering in the target.
        for (var index = 0; index < encounters.Length; index++)
            context.Entry(encounters[index]).CurrentValues.SetValues(encounters[index] with { Number = -index - 1 });
        context.SaveChanges();
        for (var index = 0; index < encounters.Length; index++)
            context.Entry(encounters[index]).CurrentValues.SetValues(encounters[index] with
            { SessionId = current.Id, Number = index + 1, Revision = encounters[index].Revision + 1 });
        context.SaveChanges();
        context.ActivityEvents.Where(x => x.SessionId == sourceId)
            .ExecuteUpdate(set => set.SetProperty(x => x.SessionId, (Guid?)current.Id));

        var now = DateTime.UtcNow;
        // The tracker closes its last visit on the next boundary. Keep the live
        // visit last even if an interrupted historical recording overlapped it.
        var visits = MergeVisits(source).Concat(MergeVisits(current))
            .OrderBy(x => x.EndedAtUtc is null).ThenBy(x => x.StartedAtUtc).ToArray();
        var intervals = visits.Select(x => new ActivityObservationInterval(x.StartedAtUtc, x.EndedAtUtc));
        var start = source.StartedAtUtc < current.StartedAtUtc ? source.StartedAtUtc : current.StartedAtUtc;
        var duration = ActivityPeriodProjection.MergeIntervals(intervals, start, now)
            .Sum(x => (x.EndedAtUtc!.Value - x.StartedAtUtc).TotalSeconds);
        var openDuration = current.ObservationStartedAtUtc is { } opened ? Math.Max(0, (now - opened).TotalSeconds) : 0;
        var merged = current with
        {
            Revision = Math.Max(current.Revision, savedTarget.Revision) + 1,
            StartedAtUtc = start,
            LastObservedAtUtc = new[] { current.LastObservedAtUtc, savedTarget.LastObservedAtUtc, source.LastObservedAtUtc }.Max(),
            ObservedDurationSeconds = Math.Max(0, duration - openDuration),
            VisitsJson = JsonSerializer.Serialize(visits),
            KnownInstancesJson = JsonSerializer.Serialize(ReadArray<string>(source.KnownInstancesJson)
                .Concat(ReadArray<string>(current.KnownInstancesJson)).Distinct(StringComparer.OrdinalIgnoreCase)),
            HasLocalDeath = current.HasLocalDeath || source.HasLocalDeath,
            HasInferredGrouping = current.HasInferredGrouping || source.HasInferredGrouping,
            IsPartial = current.IsPartial || source.IsPartial,
            PersonalSummaryJson = Summary(ActivityScope.Personal),
            PartySummaryJson = Summary(ActivityScope.Party),
            ObservedSummaryJson = Summary(ActivityScope.AllObserved)
        };
        context.Entry(savedTarget).CurrentValues.SetValues(merged);
        context.ActivitySessions.Remove(source);
        context.SaveChanges();
        lock (acceptedSync)
        {
            transaction.Commit();
            mergedSessions[sourceId] = current.Id;
            deletedSessions.Add(sourceId);
            Interlocked.Increment(ref readVersion);
        }
        return new(merged, encounters);

        string Summary(ActivityScope scope) => ActivityProjection.Serialize(AggregateSummaryAsync(
            context.ActivityEvents.AsNoTracking().Where(x => x.SessionId == current.Id), scope, CancellationToken.None)
            .GetAwaiter().GetResult());
    }

    private static ActivityVisit[] MergeVisits(ActivitySession value)
    {
        var visits = ReadArray<ActivityVisit>(value.VisitsJson);
        var end = value.ClosedAtUtc ?? value.LastObservedAtUtc;
        if (value.State == ActivityState.Interrupted && end > value.LastObservedAtUtc) end = value.LastObservedAtUtc;
        if (visits.Length == 0)
        {
            var start = value.ObservationStartedAtUtc ?? end.AddSeconds(-Math.Min(
                Math.Max(0, value.ObservedDurationSeconds), Math.Max(0, (end - value.StartedAtUtc).TotalSeconds)));
            return [new(value.RootMapId, value.LocationName, start,
                value.State == ActivityState.Open && value.ObservationStartedAtUtc is not null ? null : end)];
        }
        return visits.Select(x => x with
        {
            EndedAtUtc = value.State == ActivityState.Interrupted && (x.EndedAtUtc is null || x.EndedAtUtc > end) ? end
                : x.EndedAtUtc ?? (value.State == ActivityState.Open && value.ObservationStartedAtUtc is not null ? null : end)
        }).Where(x => x.EndedAtUtc is null || x.EndedAtUtc >= x.StartedAtUtc).ToArray();
    }

    private static T[] ReadArray<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private ActivityEvent RedirectMergedEvent(ActivityEvent value)
    {
        var id = value.SessionId;
        while (id is { } original && mergedSessions.TryGetValue(original, out var replacement)) id = replacement;
        return id == value.SessionId ? value : value with { SessionId = id };
    }

    private sealed record MergeSessionCommand(Guid SourceId, Guid TargetId, object RecordingSync,
        Func<ActivitySession?> Snapshot, Action<ActivitySessionMergeResult> Apply, TaskCompletionSource Completion) : ControlCommand(Completion)
    {
        public bool Completed { get; set; }
        public string? Error { get; set; }
        public Exception? Failure { get; set; }
    }
}

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

internal sealed record ActivitySessionResumeResult(ActivitySession Session, int LastEncounterNumber);

public sealed partial class ActivityPersistence
{
    internal Task<IReadOnlyList<ActivitySession>> GetResumeCandidatesAsync(ActivityOwner? owner,
        CancellationToken cancellationToken) => ReadAsync<IReadOnlyList<ActivitySession>>(async ct =>
    {
        await using var context = contextFactory();
        var query = context.ActivitySessions.AsNoTracking().Where(x => x.State != ActivityState.Open &&
            x.CharacterId != "" && x.Server != "");
        if (owner is not null) query = query.Where(x => x.CharacterId == owner.CharacterId && x.Server == owner.Server);
        return await query.OrderByDescending(x => x.LastObservedAtUtc).ThenBy(x => x.Id).Take(100).ToArrayAsync(ct);
    }, cancellationToken);

    internal async Task ResumeSessionAsync(Guid sessionId, object recordingSync,
        Func<ActivitySession, ActivitySession> prepare, Action<ActivitySessionResumeResult> apply)
    {
        var command = new ResumeSessionCommand(sessionId, recordingSync, prepare, apply, NewCompletion());
        await ControlAsync(command, CancellationToken.None).ConfigureAwait(false);
        if (command.Failure is { } failure) throw new IOException("The resumed session could not be saved.", failure);
        if (command.Error is { } error) throw new InvalidOperationException(error);
    }

    private void ExecuteResume(ResumeSessionCommand command)
    {
        if (command.Completed) return;
        lock (command.RecordingSync)
        {
            ActivitySessionResumeResult result;
            try
            {
                using var context = contextFactory();
                using var transaction = context.Database.BeginTransaction();
                var saved = context.ActivitySessions.Find(command.SessionId);
                if (saved is null || saved.State == ActivityState.Open || deletedSessions.Contains(command.SessionId))
                    throw new InvalidOperationException("That session is no longer available to resume. Select another session.");

                // Recovery can leave an unfinished visit in the header. Close it at
                // the old recording boundary before adding today's observation.
                var previous = saved with { VisitsJson = JsonSerializer.Serialize(MergeVisits(saved)) };
                lock (acceptedSync)
                    previous = previous with
                    {
                        Revision = Math.Max(previous.Revision, acceptedWrites.OfType<SessionCommand>()
                            .Where(x => x.Value.Id == saved.Id).Select(x => x.Value.Revision).DefaultIfEmpty(0).Max())
                    };
                var resumed = command.Prepare(previous);
                var lastNumber = context.ActivityEncounters.Where(x => x.SessionId == saved.Id)
                    .Select(x => (int?)x.Number).Max() ?? 0;
                var previousPlaySession = saved.PlaySessionId;
                context.Entry(saved).CurrentValues.SetValues(resumed);
                context.SaveChanges();
                // The resumed run moves into the current play session; its old one may now be empty.
                if (previousPlaySession is { } playSessionId && playSessionId != resumed.PlaySessionId)
                {
                    RefreshPlaySessionsAsync(context, [playSessionId], new CommittedChanges(), CancellationToken.None).GetAwaiter().GetResult();
                    command.PreviousPlaySessionId = playSessionId;
                }
                transaction.Commit();
                Interlocked.Increment(ref readVersion);
                result = new(resumed, lastNumber);
            }
            catch (InvalidOperationException error)
            {
                command.Error = error.Message;
                command.Completed = true;
                return;
            }
            catch (Exception error)
            {
                Log.Error(error, "Could not resume Activity session.");
                command.Failure = error;
                command.Completed = true;
                return;
            }
            command.Apply(result);
            command.Completed = true;
        }
        var changes = new CommittedChanges();
        changes.Sessions.Add(command.SessionId);
        if (command.PreviousPlaySessionId is { } movedFrom) changes.PlaySessions.Add(movedFrom);
        NotifyCommitted(changes);
    }

    private sealed record ResumeSessionCommand(Guid SessionId, object RecordingSync,
        Func<ActivitySession, ActivitySession> Prepare, Action<ActivitySessionResumeResult> Apply,
        TaskCompletionSource Done) : ControlCommand(Done)
    {
        public Guid? PreviousPlaySessionId { get; set; }
        public bool Completed { get; set; }
        public string? Error { get; set; }
        public Exception? Failure { get; set; }
    }
}

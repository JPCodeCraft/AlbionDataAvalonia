using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.DB;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed record ActivityPlaySessionHeader(ActivityPlaySession PlaySession, IReadOnlyList<ActivitySession> Runs);

public sealed partial class ActivityPersistence
{
    // Play sessions interrupted by the app stopping or the game closing. The same character back within
    // the idle limit continues one.
    private readonly List<ActivityPlaySession> recentPlaySessions = [];

    public bool EnqueuePlaySession(ActivityPlaySession playSession) => Enqueue(new PlaySessionCommand(playSession));
    public Task DeletePlaySessionAsync(Guid playSessionId, CancellationToken cancellationToken = default) =>
        ControlAsync(new DeletePlaySessionCommand(playSessionId, NewCompletion()), cancellationToken);

    public Task<ActivityPage<ActivityPlaySessionHeader>> GetPlaySessionsAsync(ActivityQuery filter, int page = 0,
        CancellationToken cancellationToken = default) => ReadAsync(ct => GetPlaySessionsCoreAsync(filter, page, ct), cancellationToken);

    public Task<ActivityPlaySessionHeader?> GetPlaySessionAsync(Guid playSessionId, CancellationToken cancellationToken = default) =>
        ReadAsync(async ct =>
        {
            await using var context = contextFactory();
            var playSession = await context.ActivityPlaySessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == playSessionId, ct);
            if (playSession is null) return null;
            var runs = await context.ActivitySessions.AsNoTracking().Where(x => x.PlaySessionId == playSessionId)
                .OrderBy(x => x.StartedAtUtc).ThenBy(x => x.Id).ToArrayAsync(ct);
            return new ActivityPlaySessionHeader(playSession, runs);
        }, cancellationToken);

    /// <summary>Takes this character's interrupted play session when it is back within the idle limit.</summary>
    internal ActivityPlaySession? TakeRecentPlaySession(ActivityOwner owner, DateTime nowUtc)
    {
        lock (recentPlaySessions)
        {
            var match = recentPlaySessions.Where(x => x.Server == owner.Server &&
                    (owner.CharacterId.Length > 0 ? x.CharacterId == owner.CharacterId : x.CharacterName == owner.CharacterName) &&
                    nowUtc - (x.EndedAtUtc ?? x.LastActivityAtUtc) <= ActivityPlaySessionProjection.IdleLimit)
                .OrderByDescending(x => x.EndedAtUtc ?? x.LastActivityAtUtc).FirstOrDefault();
            if (match is not null) recentPlaySessions.Remove(match);
            return match;
        }
    }

    /// <summary>Keeps a play session the game closing interrupted, so the character's return continues it.</summary>
    internal void RememberRecentPlaySession(ActivityPlaySession playSession)
    {
        lock (recentPlaySessions)
        {
            recentPlaySessions.RemoveAll(x => x.Id == playSession.Id);
            recentPlaySessions.Add(playSession);
        }
    }

    private async Task<ActivityPage<ActivityPlaySessionHeader>> GetPlaySessionsCoreAsync(ActivityQuery filter, int page,
        CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        // A play session whose runs were all deleted waits for its refresh; it has nothing to show.
        var query = context.ActivityPlaySessions.AsNoTracking().Where(x => context.ActivitySessions.Any(s => s.PlaySessionId == x.Id));
        if (filter.PlaySessionId is { } id) query = query.Where(x => x.Id == id);
        if (!string.IsNullOrWhiteSpace(filter.Server)) query = query.Where(x => x.Server == filter.Server);
        if (!string.IsNullOrWhiteSpace(filter.CharacterId)) query = query.Where(x => x.CharacterId == filter.CharacterId);
        if (!string.IsNullOrWhiteSpace(filter.CharacterName)) query = query.Where(x => x.CharacterName == filter.CharacterName);
        var rows = await query.OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id)
            .Skip(Math.Max(0, page) * 100).Take(101).ToListAsync(cancellationToken);
        var ids = rows.Take(100).Select(x => (Guid?)x.Id).ToArray();
        var runs = (await context.ActivitySessions.AsNoTracking().Where(x => ids.Contains(x.PlaySessionId)).ToListAsync(cancellationToken))
            .GroupBy(x => x.PlaySessionId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ActivitySession>)g.OrderBy(x => x.StartedAtUtc).ThenBy(x => x.Id).ToArray());
        return new(rows.Take(100).Select(x => new ActivityPlaySessionHeader(x, runs.GetValueOrDefault(x.Id) ?? [])).ToArray(), rows.Count > 100);
    }

    // Runs of a play session as one read sees them: saved, still queued, or open in the tracker.
    private static async Task<HashSet<Guid>?> PlayRunsAsync(LocalContext context, ActivityQuery query, PendingRead pendingRead,
        ActivitySession? liveSession, CancellationToken cancellationToken)
    {
        if (query.PlaySessionId is null && query.PlaySessionIds is null) return null;
        var runs = (await RunsInPlaySessions(context.ActivitySessions.AsNoTracking(), query)
            .Select(x => x.Id).ToArrayAsync(cancellationToken)).ToHashSet();
        foreach (var header in pendingRead.Accepted.OfType<SessionCommand>().Select(x => x.Value))
            if (InPlaySessions(header.PlaySessionId, query)) runs.Add(header.Id);
        if (liveSession is not null && InPlaySessions(liveSession.PlaySessionId, query)) runs.Add(liveSession.Id);
        runs.ExceptWith(pendingRead.Deleted);
        return runs;
    }

    private static bool InPlaySessions(Guid? playSessionId, ActivityQuery query) =>
        query.PlaySessionId is { } single ? playSessionId == single
        : query.PlaySessionIds is not { } set || playSessionId is { } id && set.Contains(id);

    // A query names one play session or several; without either it keeps every run.
    private static IQueryable<ActivitySession> RunsInPlaySessions(IQueryable<ActivitySession> runs, ActivityQuery query)
    {
        if (query.PlaySessionId is { } playSessionId) return runs.Where(x => x.PlaySessionId == playSessionId);
        if (query.PlaySessionIds is not { } set) return runs;
        var ids = set.Nullable();
        return runs.Where(x => ids.Contains(x.PlaySessionId));
    }

    private static IQueryable<ActivityEvent> InPlaySession(LocalContext context, IQueryable<ActivityEvent> events, Guid playSessionId) =>
        events.Where(x => x.SessionId != null && context.ActivitySessions.Any(s => s.Id == x.SessionId && s.PlaySessionId == playSessionId));

    private static IQueryable<ActivityEvent> InPlaySessions(LocalContext context, IQueryable<ActivityEvent> events, ActivityQuery query)
    {
        if (query.PlaySessionId is { } playSessionId) return InPlaySession(context, events, playSessionId);
        if (query.PlaySessionIds is not { } set) return events;
        var ids = set.Nullable();
        return events.Where(x => x.SessionId != null && context.ActivitySessions.Any(s => s.Id == x.SessionId && ids.Contains(s.PlaySessionId)));
    }

    private static IQueryable<ActivityEncounter> InPlaySessions(LocalContext context, IQueryable<ActivityEncounter> encounters, ActivityQuery query)
    {
        if (query.PlaySessionId is { } playSessionId)
            return encounters.Where(x => context.ActivitySessions.Any(s => s.Id == x.SessionId && s.PlaySessionId == playSessionId));
        if (query.PlaySessionIds is not { } set) return encounters;
        var ids = set.Nullable();
        return encounters.Where(x => context.ActivitySessions.Any(s => s.Id == x.SessionId && ids.Contains(s.PlaySessionId)));
    }

    // Recovery closes play sessions left open by a crash or shutdown and gives older runs their play sessions.
    private async Task RecoverPlaySessionsAsync(LocalContext context, CommittedChanges changes, CancellationToken cancellationToken)
    {
        var open = await context.ActivityPlaySessions.Where(x => x.State == ActivityState.Open).ToListAsync(cancellationToken);
        foreach (var playSession in open)
        {
            var ends = await context.ActivitySessions.Where(x => x.PlaySessionId == playSession.Id)
                .Select(x => (DateTime?)(x.ClosedAtUtc ?? x.LastObservedAtUtc)).ToArrayAsync(cancellationToken);
            if (ends.Length == 0) { context.ActivityPlaySessions.Remove(playSession); continue; }
            var lastRun = ends.Max()!.Value;
            context.Entry(playSession).CurrentValues.SetValues(playSession with
            {
                Revision = playSession.Revision + 1,
                State = ActivityState.Interrupted,
                EndReason = ActivityEndReason.Interrupted,
                EndedAtUtc = lastRun > playSession.LastActivityAtUtc ? lastRun : playSession.LastActivityAtUtc
            });
        }
        changes.Include(context);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        await AssignPlaySessionsAsync(context, changes, cancellationToken);
        var cutoff = DateTime.UtcNow - ActivityPlaySessionProjection.IdleLimit;
        var recent = await context.ActivityPlaySessions.AsNoTracking()
            .Where(x => x.State == ActivityState.Interrupted && (x.EndedAtUtc ?? x.LastActivityAtUtc) >= cutoff).ToListAsync(cancellationToken);
        lock (recentPlaySessions)
        {
            recentPlaySessions.Clear();
            recentPlaySessions.AddRange(recent);
        }
    }

    // Runs recorded before play sessions existed are grouped by the same rule: one character,
    // with no more than the idle limit between runs. Imported summaries stay on their own.
    private static async Task AssignPlaySessionsAsync(LocalContext context, CommittedChanges changes, CancellationToken cancellationToken)
    {
        var runs = await context.ActivitySessions.Where(x => x.PlaySessionId == null)
            .OrderBy(x => x.Server).ThenBy(x => x.CharacterId).ThenBy(x => x.CharacterName).ThenBy(x => x.StartedAtUtc)
            .ToListAsync(cancellationToken);
        if (runs.Count == 0) return;
        var imported = (await context.ActivityEvents.AsNoTracking()
            .Where(x => x.Kind == ActivityEventKind.LegacyGatheringAggregate && x.SessionId != null)
            .Select(x => x.SessionId!.Value).Distinct().ToArrayAsync(cancellationToken)).ToHashSet();
        ActivityPlaySession? current = null;
        ActivitySession? previous = null;
        foreach (var run in runs)
        {
            var end = run.ClosedAtUtc ?? run.LastObservedAtUtc;
            var sameOwner = previous is not null && previous.Server == run.Server && previous.CharacterId == run.CharacterId &&
                (run.CharacterId.Length > 0 || previous.CharacterName == run.CharacterName);
            if (current is null || !sameOwner || imported.Contains(run.Id) || imported.Contains(previous!.Id) ||
                run.StartedAtUtc - (current.EndedAtUtc ?? current.StartedAtUtc) > ActivityPlaySessionProjection.IdleLimit)
            {
                if (current is not null) context.ActivityPlaySessions.Add(current);
                current = new ActivityPlaySession
                {
                    Id = Guid.NewGuid(),
                    Server = run.Server,
                    CharacterId = run.CharacterId,
                    CharacterName = run.CharacterName,
                    StartedAtUtc = run.StartedAtUtc,
                    LastActivityAtUtc = run.LastRelevantAtUtc,
                    EndedAtUtc = end,
                    State = ActivityState.Closed,
                    EndReason = run.EndReason
                };
            }
            else current = current with
            {
                LastActivityAtUtc = run.LastRelevantAtUtc > current.LastActivityAtUtc ? run.LastRelevantAtUtc : current.LastActivityAtUtc,
                EndedAtUtc = end > current.EndedAtUtc ? end : current.EndedAtUtc,
                EndReason = run.EndReason
            };
            context.Entry(run).CurrentValues.SetValues(run with { PlaySessionId = current.Id });
            previous = run;
        }
        context.ActivityPlaySessions.Add(current!);
        changes.Include(context);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    // After runs are deleted, closed play sessions take their bounds from the runs left,
    // and a play session left without runs is removed. The open one belongs to the tracker.
    private static async Task RefreshPlaySessionsAsync(LocalContext context, IEnumerable<Guid> playSessionIds,
        CommittedChanges changes, CancellationToken cancellationToken)
    {
        foreach (var id in playSessionIds.Distinct().ToArray())
        {
            var playSession = await context.ActivityPlaySessions.FindAsync([id], cancellationToken);
            if (playSession is null || playSession.State == ActivityState.Open) continue;
            var runs = await context.ActivitySessions.AsNoTracking().Where(x => x.PlaySessionId == id).ToArrayAsync(cancellationToken);
            if (runs.Length == 0) { context.ActivityPlaySessions.Remove(playSession); continue; }
            context.Entry(playSession).CurrentValues.SetValues(playSession with
            {
                Revision = playSession.Revision + 1,
                StartedAtUtc = runs.Min(x => x.StartedAtUtc),
                EndedAtUtc = runs.Max(x => x.ClosedAtUtc ?? x.LastObservedAtUtc),
                LastActivityAtUtc = runs.Max(x => x.LastRelevantAtUtc)
            });
        }
        changes.Include(context);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecuteDeletePlaySessionAsync(DeletePlaySessionCommand command, CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var changes = new CommittedChanges();
        var runIds = await context.ActivitySessions.Where(x => x.PlaySessionId == command.Id).Select(x => x.Id).ToArrayAsync(cancellationToken);
        changes.PlaySessions.Add(command.Id);
        foreach (var id in runIds) changes.Sessions.Add(id);
        changes.Items = await context.ActivityEvents.AnyAsync(x => x.SessionId != null && runIds.Contains(x.SessionId.Value) &&
            (x.Kind == ActivityEventKind.Loot || x.Kind == ActivityEventKind.Gathered || x.Kind == ActivityEventKind.FishingReward ||
                x.Kind == ActivityEventKind.LegacyGatheringAggregate), cancellationToken);
        await context.ActivitySessions.Where(x => x.PlaySessionId == command.Id && x.State != ActivityState.Open).ExecuteDeleteAsync(cancellationToken);
        await context.ActivityPlaySessions.Where(x => x.Id == command.Id && x.State != ActivityState.Open).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        lock (acceptedSync)
        {
            foreach (var id in runIds) deletedSessions.Add(id);
            Interlocked.Increment(ref readVersion);
        }
        NotifyCommitted(changes);
        foreach (var id in runIds)
        {
            try { SessionDeleted?.Invoke(id); } catch { }
        }
    }

    private sealed record PlaySessionCommand(ActivityPlaySession Value) : WriteCommand;
    private sealed record DeletePlaySessionCommand(Guid Id, TaskCompletionSource Completion) : ControlCommand(Completion);
}

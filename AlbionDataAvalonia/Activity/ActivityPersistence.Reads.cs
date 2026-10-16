using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.DB;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed record ActivityDataChange(long Version, IReadOnlyList<Guid> SessionIds, bool ItemsChanged,
    IReadOnlyList<Guid>? PlaySessionIds = null)
{
    public bool HistoryChanged => SessionIds.Count > 0 || PlaySessionIds is { Count: > 0 };
}

public sealed partial class ActivityPersistence
{
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly CancellationTokenSource readShutdown = new();
    private long dataVersion;
    private long readVersion;
    public long CurrentReadVersion => Interlocked.Read(ref readVersion);
    private bool readsClosed;
    public event Action<ActivityDataChange>? DataChanged;

    // SQLite's async APIs execute synchronously. Only the DB delegate runs in the worker;
    // callers still resume on their UI context to apply immutable results. One active read
    // bounds contexts/workers, and queued callers wait asynchronously before Task.Run.
    private async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(readsClosed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readShutdown.Token);
        await readGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var result = await Task.Run(() => read(linked.Token), linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        finally { readGate.Release(); }
    }

    internal Task<ActivityLiveTimelineSnapshot> GetLiveTimelineAsync(Guid? sessionId, ActivityScope scope,
        DateTime endUtc, ActivitySession? liveSession, ActivityEncounter? liveEncounter, CancellationToken cancellationToken,
        ActivityCategories categories = ActivityCategories.All) =>
        ReadAsync(ct => GetLiveTimelineCoreAsync(sessionId, scope, endUtc, liveSession, liveEncounter, ct, categories), cancellationToken);
    public Task<ActivitySession?> GetSessionAsync(Guid id, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetSessionCoreAsync(id, ct), cancellationToken);
    public Task<ActivityEncounter?> GetEncounterAsync(Guid id, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetEncounterCoreAsync(id, ct), cancellationToken);
    public Task<double> GetCombatDurationSecondsAsync(ActivityQuery filter, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetCombatDurationSecondsCoreAsync(filter, ct), cancellationToken);
    public Task<ActivityEvent?> GetEventAsync(Guid id, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetEventCoreAsync(id, ct), cancellationToken);
    public Task<ActivityPage<ActivitySession>> GetSessionsAsync(ActivityQuery filter, int page = 0, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetSessionsCoreAsync(filter, page, ct), cancellationToken);
    internal Task<IReadOnlyList<ActivitySession>> GetSessionsByIdsAsync(ActivityQuery filter, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ids.Count, 1000);
        var requested = ids.Distinct().ToArray();
        return requested.Length == 0 ? Task.FromResult<IReadOnlyList<ActivitySession>>([]) :
            ReadAsync(ct => GetSessionsByIdsCoreAsync(filter, requested, ct), cancellationToken);
    }
    public Task<ActivityPage<ActivityEncounter>> GetEncountersAsync(Guid sessionId, int page = 0, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetEncountersCoreAsync(sessionId, page, ct), cancellationToken);
    public Task<ActivityPage<ActivityEncounter>> GetPlaySessionEncountersAsync(Guid playSessionId, int page = 0, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetPlaySessionEncountersCoreAsync(playSessionId, page, ct), cancellationToken);
    public Task<ActivityPage<ActivityEvent>> GetEventsAsync(ActivityQuery filter, int page = 0, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetEventsCoreAsync(filter, page, ct), cancellationToken);
    internal Task<ActivityPage<ActivityEvent>> GetEventsThroughPageAsync(ActivityQuery filter, int lastPage, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetEventsCoreAsync(filter, 0, ct, (int)Math.Min(int.MaxValue, Math.Max(0L, lastPage) + 1)), cancellationToken);
    public Task<ActivitySummary> GetSummaryAsync(ActivityQuery filter, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetSummaryCoreAsync(filter, ct), cancellationToken);
    public Task<IReadOnlyList<ActivityChartPoint>> GetChartsAsync(ActivityQuery filter, ActivityChartMetric metric, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetChartsCoreAsync(filter, metric, ct), cancellationToken);
    public Task<ActivityLiveTimelineSnapshot> GetTimelineAsync(ActivityQuery query, DateTime fromUtc, DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        fromUtc = ActivityLiveTimeline.WindowEnd(fromUtc);
        var lastTick = toUtc.ToUniversalTime().Ticks;
        toUtc = new DateTime(Math.Min(DateTime.MaxValue.Ticks,
            (lastTick / TimeSpan.TicksPerSecond + 1) * TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        if (toUtc <= fromUtc) toUtc = new DateTime(Math.Min(DateTime.MaxValue.Ticks,
            fromUtc.Ticks + TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        return ReadAsync(ct => GetLiveTimelineCoreAsync(query with { FromUtc = fromUtc, ToUtc = toUtc },
            toUtc, null, null, ct, (toUtc - fromUtc).TotalSeconds), cancellationToken);
    }
    public Task<IReadOnlyList<ActivityActorTotal>> GetPlayersAsync(ActivityQuery filter, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetPlayersCoreAsync(filter, ct), cancellationToken);
    internal Task<IReadOnlyList<ActivityEvent>> GetPartyEventsAsync(ActivityQuery filter, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetPartyEventsCoreAsync(filter, ct), cancellationToken);
    internal Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetSessionPartyMembersAsync(IEnumerable<Guid> sessionIds, CancellationToken cancellationToken = default)
    {
        var requested = sessionIds.Distinct().Select(id => (Guid?)id).ToArray();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requested.Length, 1000);
        return requested.Length == 0 ? Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<string>>>(new Dictionary<Guid, IReadOnlyList<string>>()) :
            ReadAsync(ct => GetSessionPartyMembersCoreAsync(requested, ct), cancellationToken);
    }
    public Task<IReadOnlyList<ActivityResourceTotal>> GetResourceTotalsAsync(ActivityQuery filter, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetResourceTotalsCoreAsync(filter, ct), cancellationToken);
    public Task<ActivityLegacyImportInfo?> GetLegacyImportInfoAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetLegacyImportInfoCoreAsync(sessionId, ct), cancellationToken);
    public Task<IReadOnlyList<ActivityMobTotal>> GetMobsAsync(ActivityQuery filter, CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetMobsCoreAsync(filter, ct), cancellationToken);

    private sealed class CommittedChanges
    {
        public HashSet<Guid> Sessions { get; } = [];
        public HashSet<Guid> PlaySessions { get; } = [];
        public bool Items;
        public void Include(LocalContext context)
        {
            context.ChangeTracker.DetectChanges();
            foreach (var entry in context.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                if (entry.Entity is ActivityPlaySession playSession) PlaySessions.Add(playSession.Id);
                else if (entry.Entity is ActivitySession session)
                {
                    Sessions.Add(session.Id);
                    if (session.PlaySessionId is { } playSessionId) PlaySessions.Add(playSessionId);
                }
                else if (entry.Entity is ActivityEncounter encounter) Sessions.Add(encounter.SessionId);
                else if (entry.Entity is ActivityEvent observation)
                {
                    if (observation.SessionId is { } id) Sessions.Add(id);
                    if (entry.OriginalValues[nameof(ActivityEvent.SessionId)] is Guid oldId) Sessions.Add(oldId);
                    Items |= ActivityClassification.IsItem(observation.Kind) ||
                        entry.OriginalValues[nameof(ActivityEvent.Kind)] is ActivityEventKind oldKind && ActivityClassification.IsItem(oldKind);
                }
            }
        }
    }

    private void NotifyCommitted(CommittedChanges changes)
    {
        if (changes.Sessions.Count == 0 && changes.PlaySessions.Count == 0 && !changes.Items) return;
        Interlocked.Increment(ref readVersion);
        var change = new ActivityDataChange(Interlocked.Increment(ref dataVersion), changes.Sessions.ToArray(), changes.Items,
            changes.PlaySessions.ToArray());
        try { DataChanged?.Invoke(change); }
        catch { /* A presentation subscriber cannot interrupt committed journal writes. */ }
    }
}

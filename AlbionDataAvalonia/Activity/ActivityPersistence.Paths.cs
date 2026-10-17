using AlbionDataAvalonia.Activity.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityPersistence
{
    private static readonly ActivityEventKind[] PlacedKinds =
    [
        ActivityEventKind.MobKilled, ActivityEventKind.PlayerKilled, ActivityEventKind.Death, ActivityEventKind.Knockdown,
        ActivityEventKind.Loot, ActivityEventKind.Gathered, ActivityEventKind.FishingReward
    ];

    /// <summary>The latest runs that recorded a path or a placed event, newest first.</summary>
    public async Task<IReadOnlyList<ActivitySession>> GetMappedRunsAsync(int count = 60, CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync<IReadOnlyList<ActivitySession>>(async ct =>
        {
            await using var context = contextFactory();
            return await context.ActivitySessions.AsNoTracking()
                .Where(s => context.ActivityPathSegments.Any(p => p.SessionId == s.Id)
                    || context.ActivityEvents.Any(e => e.SessionId == s.Id && e.PositionX != null))
                .OrderByDescending(s => s.StartedAtUtc).Take(count).ToArrayAsync(ct);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActivityRunMap?> GetRunMapAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(async ct =>
        {
            await using var context = contextFactory();
            var run = await context.ActivitySessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
            if (run is null) return null;
            var segments = await context.ActivityPathSegments.AsNoTracking().Where(p => p.SessionId == sessionId)
                .OrderBy(p => p.StartedAtUtc).ThenBy(p => p.Id).ToArrayAsync(ct);
            var events = await context.ActivityEvents.AsNoTracking()
                .Where(e => e.SessionId == sessionId && e.PositionX != null && !e.IsRetracted && PlacedKinds.Contains(e.Kind))
                .OrderBy(e => e.CapturedAtUtc).ThenBy(e => e.CaptureOrder).ToArrayAsync(ct);
            return new ActivityRunMap(run, segments, events.Select(ActivityMapMarker.From).OfType<ActivityMapMarker>().ToArray());
        }, cancellationToken).ConfigureAwait(false);
    }
}

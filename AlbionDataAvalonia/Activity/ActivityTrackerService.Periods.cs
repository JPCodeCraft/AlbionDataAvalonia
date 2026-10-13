using AlbionDataAvalonia.Activity.Models;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityTrackerService
{
    private ActivityOwner? confirmedOwner;
    private long periodContextVersion;
    public long CurrentReadVersion => persistence.CurrentReadVersion + Interlocked.Read(ref periodContextVersion);

    public ActivityCurrentContext GetCurrentContext()
    {
        lock (sync) return new(confirmedOwner, session, encounter);
    }

    private void PeriodOwnerChanged(object? sender, EventArgs args)
    {
        var changed = false;
        lock (sync)
        {
            if (confirmedOwner is not { } owner) return;
            var server = player.AlbionServer?.Id.ToString(CultureInfo.InvariantCulture);
            if (server is not null && owner.Server != server ||
                !string.IsNullOrWhiteSpace(player.PlayerName) && owner.CharacterName != player.PlayerName)
            {
                confirmedOwner = null;
                Interlocked.Increment(ref periodContextVersion);
                changed = true;
            }
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<ActivityPeriodSnapshot> GetPeriodSnapshotAsync(ActivityQuery query, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ActivitySession? liveSession;
        ActivityEncounter? liveEncounter;
        long version;
        lock (sync) { liveSession = session; liveEncounter = encounter; version = Interlocked.Read(ref periodContextVersion); }
        return persistence.GetPeriodSnapshotWithContextAsync(query, nowUtc, liveSession, liveEncounter, version, cancellationToken);
    }

    public Task<ActivityLiveTimelineSnapshot> GetLiveTimelineAsync(ActivityQuery query, DateTime endUtc,
        CancellationToken cancellationToken = default, int windowSeconds = ActivityLiveTimeline.WindowSeconds)
    {
        ActivitySession? liveSession;
        ActivityEncounter? liveEncounter;
        lock (sync) { liveSession = session; liveEncounter = encounter; }
        return persistence.GetLiveTimelineAsync(query, endUtc, liveSession, liveEncounter, cancellationToken, windowSeconds);
    }
}

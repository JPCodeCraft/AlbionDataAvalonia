using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Activity;

public sealed record ActivityPlayDurations(double PlaySeconds, double RunSeconds);

/// <summary>Rules shared by the tracker, persistence and presentation for play sessions.</summary>
public static class ActivityPlaySessionProjection
{
    // Without activity for this long a play session ends at its last activity.
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);
    // The last pickup after a fight still lands inside a session trimmed to its last activity.
    public static readonly TimeSpan ActivityGrace = TimeSpan.FromMinutes(1);
    // The live view reports a pause once nothing has happened for this long.
    public static readonly TimeSpan IdleNoticeDelay = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Run time counts recorded time in runs. Play time adds the gaps between runs up to the idle limit,
    /// such as a town visit; a capture gap inside a run counts in neither. A play session started with
    /// Start session counts from that press, so the wait before its first run is play time too.
    /// </summary>
    public static ActivityPlayDurations Durations(IEnumerable<ActivityObservationInterval> intervals, DateTime fromUtc, DateTime toUtc)
    {
        var all = intervals.ToArray();
        var run = ActivityPeriodProjection.MergeIntervals(all, fromUtc, toUtc).Sum(x => (x.EndedAtUtc!.Value - x.StartedAtUtc).TotalSeconds);
        var spans = all.GroupBy(x => x.RunId ?? Guid.NewGuid())
            .Select(group => ActivityPeriodProjection.MergeIntervals(group, fromUtc, toUtc))
            .Where(merged => merged.Count > 0)
            .Select(merged => (Start: merged[0].StartedAtUtc, End: merged[^1].EndedAtUtc!.Value))
            .OrderBy(x => x.Start).ToArray();
        if (spans.Length == 0) return new(toUtc > fromUtc && toUtc - fromUtc <= IdleLimit ? (toUtc - fromUtc).TotalSeconds : 0, 0);
        var play = run;
        DateTime reached = fromUtc;
        foreach (var span in spans)
        {
            if (span.Start > reached && span.Start - reached <= IdleLimit) play += (span.Start - reached).TotalSeconds;
            if (span.End > reached) reached = span.End;
        }
        return new(play, run);
    }

    /// <summary>Durations of saved runs, ending at the play session's trimmed end.</summary>
    public static ActivityPlayDurations Durations(ActivityPlaySession playSession, IReadOnlyCollection<ActivitySession> runs,
        ActivitySession? liveRun = null, DateTime? nowUtc = null)
    {
        if (runs.Count == 0 && liveRun is null) return new(0, 0);
        var end = LiveEnd(playSession, nowUtc ?? DateTime.UtcNow);
        var intervals = ActivityPeriodProjection.ObservationIntervals(liveRun is null ? runs : runs.Where(x => x.Id != liveRun.Id).Append(liveRun), liveRun);
        var first = intervals.Count == 0 ? playSession.StartedAtUtc : intervals.Min(x => x.StartedAtUtc);
        return Durations(intervals, first < playSession.StartedAtUtc ? first : playSession.StartedAtUtc, end);
    }

    /// <summary>
    /// A closed play session ends at its saved end; an open one pauses one grace period after its last activity.
    /// One started with Start session keeps its clock running until its first run.
    /// </summary>
    public static DateTime LiveEnd(ActivityPlaySession playSession, DateTime nowUtc, bool hasRun = true)
    {
        if (playSession.State != ActivityState.Open) return playSession.EndedAtUtc ?? playSession.LastActivityAtUtc;
        if (!hasRun) return nowUtc;
        var paused = playSession.LastActivityAtUtc + ActivityGrace;
        return paused < nowUtc ? paused : nowUtc;
    }

    public static bool IsIdle(ActivityPlaySession? playSession, DateTime nowUtc) =>
        playSession is { State: ActivityState.Open } && nowUtc - playSession.LastActivityAtUtc >= IdleNoticeDelay;

    /// <summary>Totals of all runs in one scope.</summary>
    public static ActivitySummary Summary(IEnumerable<ActivitySession> runs, ActivityScope scope) =>
        runs.Aggregate(new ActivitySummary { TagVersion = ActivityClassification.TagVersion },
            (total, run) => ActivityProjection.Add(total, ActivityProjection.GetSummary(run, scope), 1));
}

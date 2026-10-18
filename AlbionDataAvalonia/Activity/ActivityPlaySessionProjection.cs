using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Activity;

/// <summary>Rules shared by the tracker, persistence and presentation for play sessions.</summary>
public static class ActivityPlaySessionProjection
{
    // A session ends after this long without moving or acting. One the game or app interrupted
    // continues when the character is back within it.
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);
    // After this long without game traffic the game was closed or lost, which interrupts the session.
    public static readonly TimeSpan GameLeftAfter = TimeSpan.FromSeconds(30);

    /// <summary>A session's time is clock time from its start to its end, less the time it was away.</summary>
    public static double Seconds(DateTime startUtc, DateTime endUtc, double awaySeconds = 0) =>
        Math.Max(0, (endUtc - startUtc).TotalSeconds - awaySeconds);

    /// <summary>Time of a saved session, open or closed.</summary>
    public static double Seconds(ActivityPlaySession playSession, IEnumerable<ActivitySession> runs, DateTime nowUtc) =>
        Seconds(Start(playSession, runs), End(playSession, nowUtc), playSession.AwaySeconds);

    /// <summary>A session starts at its saved start, or at a run that began before it.</summary>
    public static DateTime Start(ActivityPlaySession playSession, IEnumerable<ActivitySession> runs) =>
        runs.Select(x => x.StartedAtUtc).Append(playSession.StartedAtUtc).Min();

    /// <summary>An open session runs to now; a closed one ends at its saved end.</summary>
    public static DateTime End(ActivityPlaySession playSession, DateTime nowUtc) =>
        playSession.State == ActivityState.Open ? nowUtc : playSession.EndedAtUtc ?? playSession.LastActivityAtUtc;

    /// <summary>Totals of all runs in one scope.</summary>
    public static ActivitySummary Summary(IEnumerable<ActivitySession> runs, ActivityScope scope) =>
        runs.Aggregate(new ActivitySummary { TagVersion = ActivityClassification.TagVersion },
            (total, run) => ActivityProjection.Add(total, ActivityProjection.GetSummary(run, scope), 1));
}

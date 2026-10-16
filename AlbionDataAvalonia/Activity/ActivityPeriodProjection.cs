using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

public static class ActivityPeriodProjection
{
    public static ActivityRates GetRates(ActivityPeriodSnapshot snapshot, DateTime fromUtc, DateTime nowUtc)
    {
        var duration = MergeIntervals(snapshot.ObservationIntervals, fromUtc, nowUtc)
            .Sum(x => (x.EndedAtUtc!.Value - x.StartedAtUtc).TotalSeconds);
        var combat = snapshot.CombatIntervals.Sum(x =>
        {
            if (x.EndedAtUtc < fromUtc || x.StartedAtUtc >= nowUtc) return 0;
            var start = x.StartedAtUtc < fromUtc ? fromUtc : x.StartedAtUtc;
            var end = x.EndedAtUtc > nowUtc ? nowUtc : x.EndedAtUtc;
            return Math.Max(1, (end - start).TotalSeconds);
        });
        return ActivityProjection.GetRates(snapshot.Summary, duration, combat);
    }

    internal static IReadOnlyList<ActivityObservationInterval> ObservationIntervals(IEnumerable<ActivitySession> sessions,
        ActivitySession? liveSession)
    {
        var result = new List<ActivityObservationInterval>();
        foreach (var session in sessions)
        {
            ActivityVisit[] visits;
            try { visits = JsonSerializer.Deserialize<ActivityVisit[]>(session.VisitsJson) ?? []; }
            catch (JsonException) { visits = []; }
            foreach (var visit in visits)
            {
                // Only the confirmed in-memory session can extend beyond its latest observation.
                // Open records from another app run stop there even before recovery finishes.
                var open = session.Id == liveSession?.Id && session.State == ActivityState.Open &&
                    session.ObservationStartedAtUtc is not null && visit.EndedAtUtc is null;
                DateTime? end = open ? null : visit.EndedAtUtc ?? session.ClosedAtUtc ?? session.LastObservedAtUtc;
                if (session.State == ActivityState.Interrupted && end > session.LastObservedAtUtc)
                    end = session.LastObservedAtUtc;
                if (end is null || end >= visit.StartedAtUtc) result.Add(new(visit.StartedAtUtc, end, session.Id));
            }
            if (visits.Length == 0)
            {
                var open = session.Id == liveSession?.Id && session.State == ActivityState.Open && session.ObservationStartedAtUtc is not null;
                DateTime? end = open ? null : session.ClosedAtUtc ?? session.LastObservedAtUtc;
                if (session.State == ActivityState.Interrupted && end > session.LastObservedAtUtc)
                    end = session.LastObservedAtUtc;
                result.Add(new(session.ObservationStartedAtUtc ?? session.StartedAtUtc, end, session.Id));
            }
        }
        return result;
    }

    internal static IReadOnlyList<ActivityObservationInterval> MergeIntervals(IEnumerable<ActivityObservationInterval> intervals,
        DateTime fromUtc, DateTime toUtc)
    {
        var result = new List<ActivityObservationInterval>();
        foreach (var interval in intervals.Select(x => new ActivityObservationInterval(
                     x.StartedAtUtc < fromUtc ? fromUtc : x.StartedAtUtc,
                     x.EndedAtUtc is not { } end || end > toUtc ? toUtc : end))
                 .Where(x => x.EndedAtUtc > x.StartedAtUtc).OrderBy(x => x.StartedAtUtc))
        {
            if (result.Count > 0 && result[^1].EndedAtUtc >= interval.StartedAtUtc)
                result[^1] = result[^1] with { EndedAtUtc = result[^1].EndedAtUtc > interval.EndedAtUtc ? result[^1].EndedAtUtc : interval.EndedAtUtc };
            else result.Add(interval);
        }
        return result;
    }
}

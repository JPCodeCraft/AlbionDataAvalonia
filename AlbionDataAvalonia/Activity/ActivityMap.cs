using System;

namespace AlbionDataAvalonia.Activity.Models;

public sealed record ActivityMap(string RawMapId, string Name, DateTime StartedAtUtc,
    DateTime LastObservedAtUtc, int Visits, double ObservedDurationSeconds, DateTime? ObservationStartedAtUtc)
{
    public double GetDurationSeconds(DateTime nowUtc) => ObservedDurationSeconds +
        (ObservationStartedAtUtc is { } started ? Math.Max(0, (nowUtc - started).TotalSeconds) : 0);
}

public sealed record ActivityMapSummary(ActivitySummary Summary, double CombatDurationSeconds);

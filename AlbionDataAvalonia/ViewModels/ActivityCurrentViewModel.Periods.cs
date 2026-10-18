using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityCurrentViewModel
{
    private ActivityPeriodSnapshot? currentPeriodSnapshot;
    private CurrentPeriodKey? currentPeriodKey;
    private CurrentObservationKey? currentObservationKey;
    private DateTime currentPeriodReadAtUtc;
    private DateTime currentPeriodFromUtc;
    private DateTime currentPeriodToUtc;

    public IReadOnlyList<Option<ActivityPeriodMode>> PeriodOptions { get; } =
        [new(ActivityPeriodMode.PlaySession, "Session"),
         new(ActivityPeriodMode.CurrentRun, "Current run"),
         new(ActivityPeriodMode.LastHours, "Last X hours")];

    [ObservableProperty] private Option<ActivityPeriodMode> selectedPeriod = new(ActivityPeriodMode.PlaySession, "Session");
    [ObservableProperty] private decimal lastHours = 1;
    [ObservableProperty] private string currentPeriodDescription = "Waiting for activity";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentPeriodWarning))]
    private string currentPeriodWarning = "";

    public bool IsLastHoursPeriod => SelectedPeriod.Value == ActivityPeriodMode.LastHours;
    public bool IsPlaySessionPeriod => SelectedPeriod.Value == ActivityPeriodMode.PlaySession;
    public bool HasCurrentPeriodWarning => !string.IsNullOrEmpty(CurrentPeriodWarning);

    private sealed record CurrentPeriodKey(ActivityPeriodMode Mode, decimal Hours, ActivityScope Scope,
        string Server, string CharacterId, string CharacterName, Guid? SessionId, Guid? PlaySessionId = null);
    private sealed record CurrentObservationKey(Guid? SessionId, DateTime? ObservationStartedAtUtc,
        DateTime? LastObservedAtUtc, ActivityState? State, string? VisitsJson);

    private CurrentObservationKey GetCurrentObservationKey()
    {
        var observed = Tracker?.GetCurrentContext().Session;
        return new(observed?.Id, observed?.ObservationStartedAtUtc, observed?.LastObservedAtUtc, observed?.State, observed?.VisitsJson);
    }

    private CurrentPeriodKey? GetCurrentPeriodKey()
    {
        if (Tracker is null) return null;
        var context = Tracker.GetCurrentContext();
        if (context.Owner is not { } owner) return null;
        var mode = SelectedPeriod.Value;
        if (mode == ActivityPeriodMode.CurrentRun && context.Session is null) return null;
        // A play session waiting for its first activity has nothing to total yet.
        if (mode == ActivityPeriodMode.PlaySession && context.PlaySession is null) return null;
        return new(mode, IsLastHoursPeriod ? LastHours : 0, Filters.Scope,
            owner.Server, owner.CharacterId, owner.CharacterName,
            mode == ActivityPeriodMode.CurrentRun ? context.Session?.Id : null,
            mode == ActivityPeriodMode.PlaySession ? context.PlaySession?.Id : null);
    }

    private DateTime CurrentPeriodStartUtc(DateTime nowUtc) => SelectedPeriod.Value switch
    {
        ActivityPeriodMode.PlaySession => currentPlayFromUtc ?? Tracker?.GetCurrentContext().PlaySession?.StartedAtUtc ?? nowUtc,
        ActivityPeriodMode.LastHours => nowUtc.AddHours(-(double)LastHours),
        _ => currentSession?.StartedAtUtc ?? nowUtc
    };

    private ActivityQuery? CurrentPeriodQuery(DateTime nowUtc, bool timeline = false)
    {
        var key = GetCurrentPeriodKey();
        if (key is null) return null;
        return new()
        {
            PlaySessionId = key.PlaySessionId,
            SessionId = key.SessionId,
            Server = key.Server,
            CharacterId = string.IsNullOrWhiteSpace(key.CharacterId) ? null : key.CharacterId,
            CharacterName = key.CharacterName,
            MatchCharacterNameExactly = true,
            Scope = key.Scope,
            FromUtc = CurrentPeriodStartUtc(nowUtc),
            ToUtc = nowUtc,
            ShowCategories = timeline ? Filters.VisibleCategories : ActivityCategories.All
        };
    }

    private async Task RefreshCurrentPeriodAsync(CancellationToken cancellationToken)
    {
        if (Tracker is not { } tracker) return;
        var nowUtc = DateTime.UtcNow;
        var key = GetCurrentPeriodKey();
        if (key is null)
        {
            ClearCurrentPeriod();
            return;
        }
        var query = CurrentPeriodQuery(nowUtc);
        if (query is null)
        {
            ClearCurrentPeriod();
            return;
        }
        var observationKey = GetCurrentObservationKey();
        var contextChanged = currentPeriodKey != key || currentPeriodSnapshot is null ||
            currentObservationKey?.SessionId != observationKey.SessionId ||
            currentObservationKey?.ObservationStartedAtUtc != observationKey.ObservationStartedAtUtc ||
            currentObservationKey?.State != observationKey.State || currentObservationKey?.VisitsJson != observationKey.VisitsJson;
        var changed = contextChanged ||
            currentObservationKey != observationKey ||
            currentPeriodSnapshot?.Version != tracker.CurrentReadVersion ||
            IsLastHoursPeriod && currentPeriodSnapshot?.NextEventExpiryUtc is { } expiry && nowUtc >= expiry;
        // A play session refreshes more often than a long time range, so its totals keep pace with play.
        var interval = key.Mode == ActivityPeriodMode.PlaySession ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5);
        if (changed && (contextChanged || nowUtc - currentPeriodReadAtUtc >= interval))
        {
            var snapshot = await tracker.GetPeriodSnapshotAsync(query, nowUtc, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!page.IsShowing(ActivityTab.Current) || key != GetCurrentPeriodKey()) return;
            currentPeriodKey = key;
            currentPeriodSnapshot = snapshot;
            currentObservationKey = observationKey;
            currentPeriodFromUtc = query.FromUtc ?? nowUtc;
            currentPeriodToUtc = query.ToUtc ?? nowUtc;
            currentPeriodReadAtUtc = DateTime.UtcNow;
            CurrentResources = new(snapshot.Resources.Select(row => new ActivityResourceRow(row)));
        }
        ApplyCurrentPeriodPresentation(DateTime.UtcNow);
    }

    private void ApplyCurrentPeriodPresentation(DateTime nowUtc)
    {
        if (currentPeriodSnapshot is not { } snapshot || currentPeriodKey is not { } key || key != GetCurrentPeriodKey() ||
            currentObservationKey?.SessionId != GetCurrentObservationKey().SessionId) return;
        if (key.Mode == ActivityPeriodMode.PlaySession && Tracker?.GetCurrentContext().PlaySession is { } playSession)
        {
            // Time pauses one grace period after the last activity; a run within the idle limit adds the gap back.
            var end = ActivityPlaySessionProjection.LiveEnd(playSession, nowUtc);
            var durations = ActivityPlaySessionProjection.Durations(snapshot.ObservationIntervals, currentPeriodFromUtc, end);
            var combat = ActivityPeriodProjection.GetRates(snapshot, currentPeriodFromUtc, end).CombatDurationSeconds;
            CurrentSummary = new(snapshot.Summary, ActivityProjection.GetRates(snapshot.Summary, durations.PlaySeconds, combat), true, true, durations.RunSeconds);
            CurrentDurationText = $"Session time: {UiFormat.Duration(durations.PlaySeconds)}";
            CurrentRunTimeText = $"In runs: {UiFormat.Duration(durations.RunSeconds)}";
            CurrentPeriodDescription = $"Started {UiFormat.LocalDateTime(playSession.StartedAtUtc)} · ends after 30 minutes without activity";
            CurrentPeriodWarning = "";
            if (snapshot.IsPartial) CurrentPeriodDescription += " · incomplete recording";
            return;
        }
        // Sliding-range totals and rates share the saved cutoff until their next refresh.
        var presentationEndUtc = key.Mode == ActivityPeriodMode.LastHours ? currentPeriodToUtc : nowUtc;
        var rates = ActivityPeriodProjection.GetRates(snapshot, currentPeriodFromUtc, presentationEndUtc);
        CurrentSummary = new(snapshot.Summary, rates, true);
        CurrentDurationText = $"Tracked time: {UiFormat.Duration(rates.DurationSeconds)}";
        CurrentRunTimeText = "";
        var fromUtc = currentPeriodFromUtc;
        CurrentPeriodDescription = currentSession is not null
            ? $"Since {UiFormat.LocalDateTime(fromUtc)}"
            : $"{key.CharacterName} · {ActivityDisplay.Server(key.Server)} · Since {UiFormat.LocalDateTime(fromUtc)}";
        CurrentPeriodWarning = snapshot.HasExcludedImports
            ? "Imported gathering totals are excluded from this time range." : "";
        if (snapshot.IsPartial)
            CurrentPeriodDescription += " · incomplete recording";
    }

    private void ClearCurrentPeriod()
    {
        ClearCurrentLoot();
        ClearCurrentMobs();
        currentPeriodKey = null;
        currentPeriodSnapshot = null;
        currentObservationKey = null;
        currentPeriodReadAtUtc = DateTime.MinValue;
        CurrentSummary = ActivitySummaryPresentation.Empty;
        if (CurrentResources.Count > 0) CurrentResources = new();
        CurrentDurationText = SelectedPeriod.Value switch
        {
            ActivityPeriodMode.PlaySession => "Session time: 0:00",
            ActivityPeriodMode.CurrentRun => "Run time: 0:00",
            _ => "Tracked time: 0:00"
        };
        CurrentRunTimeText = "";
        CurrentPeriodDescription = SelectedPeriod.Value == ActivityPeriodMode.LastHours ? "Waiting for your character" : "Waiting for activity";
        CurrentPeriodWarning = "";
    }

    private void ChangeCurrentPeriod()
    {
        currentQueryCancellation.Cancel();
        liveTimelineCancellation.Cancel();
        currentResourcesQuery = null;
        ClearCurrentPeriod();
        CurrentLiveTimeline = null;
        OnPropertyChanged(nameof(IsLastHoursPeriod));
        OnPropertyChanged(nameof(IsPlaySessionPeriod));
        OnPropertyChanged(nameof(ShowCurrentRuns));
        page.RefreshIfShowing(ActivityTab.Current);
    }

    partial void OnSelectedPeriodChanged(Option<ActivityPeriodMode> value) => ChangeCurrentPeriod();
    partial void OnLastHoursChanged(decimal value)
    {
        var normalized = Math.Clamp(Math.Round(value, 0, MidpointRounding.AwayFromZero), 1m, 168m);
        if (normalized != value) { LastHours = normalized; return; }
        if (IsLastHoursPeriod) ChangeCurrentPeriod();
    }
}

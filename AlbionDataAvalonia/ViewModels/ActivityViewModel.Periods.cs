using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    private ActivityPeriodSnapshot? currentPeriodSnapshot;
    private CurrentPeriodKey? currentPeriodKey;
    private CurrentObservationKey? currentObservationKey;
    private DateTime currentPeriodReadAtUtc;
    private DateTime currentPeriodFromUtc;
    private DateTime currentPeriodToUtc;

    public IReadOnlyList<ActivityOption<ActivityPeriodMode>> PeriodOptions { get; } =
        [new(ActivityPeriodMode.CurrentSession, "Current session"),
         new(ActivityPeriodMode.SinceAppStarted, "Since app started"),
         new(ActivityPeriodMode.LastHours, "Last X hours")];

    [ObservableProperty] private ActivityOption<ActivityPeriodMode> selectedPeriod = new(ActivityPeriodMode.CurrentSession, "Current session");
    [ObservableProperty] private decimal lastHours = 1;
    [ObservableProperty] private string currentPeriodDescription = "Waiting for activity";
    [ObservableProperty] private string currentPeriodWarning = "";

    public bool IsLastHoursPeriod => SelectedPeriod.Value == ActivityPeriodMode.LastHours;
    public string CurrentTotalsHeading => SelectedPeriod.Value == ActivityPeriodMode.CurrentSession ? "Session totals" : "Totals";
    public bool HasCurrentPeriodWarning => !string.IsNullOrEmpty(CurrentPeriodWarning);

    private sealed record CurrentPeriodKey(ActivityPeriodMode Mode, decimal Hours, ActivityScope Scope,
        string Server, string CharacterId, string CharacterName, Guid? SessionId);
    private sealed record CurrentObservationKey(Guid? SessionId, DateTime? ObservationStartedAtUtc,
        DateTime? LastObservedAtUtc, ActivityState? State, string? VisitsJson);

    private CurrentObservationKey GetCurrentObservationKey()
    {
        var observed = tracker?.GetCurrentContext().Session;
        return new(observed?.Id, observed?.ObservationStartedAtUtc, observed?.LastObservedAtUtc, observed?.State, observed?.VisitsJson);
    }

    private CurrentPeriodKey? GetCurrentPeriodKey()
    {
        if (tracker is null) return null;
        var context = tracker.GetCurrentContext();
        if (context.Owner is not { } owner) return null;
        if (SelectedPeriod.Value == ActivityPeriodMode.CurrentSession && context.Session is null) return null;
        return new(SelectedPeriod.Value, IsLastHoursPeriod ? LastHours : 0, SelectedScope.Value,
            owner.Server, owner.CharacterId, owner.CharacterName,
            SelectedPeriod.Value == ActivityPeriodMode.CurrentSession ? context.Session?.Id : null);
    }

    private DateTime CurrentPeriodStartUtc(DateTime nowUtc) => SelectedPeriod.Value switch
    {
        ActivityPeriodMode.SinceAppStarted => ActivityPeriodClock.AppStartedAtUtc,
        ActivityPeriodMode.LastHours => nowUtc.AddHours(-(double)LastHours),
        _ => currentSession?.StartedAtUtc ?? nowUtc
    };

    private ActivityQuery? CurrentPeriodQuery(DateTime nowUtc, bool timeline = false)
    {
        var key = GetCurrentPeriodKey();
        if (key is null) return null;
        return new()
        {
            SessionId = key.SessionId,
            Server = key.Server,
            CharacterId = string.IsNullOrWhiteSpace(key.CharacterId) ? null : key.CharacterId,
            CharacterName = key.CharacterName,
            MatchCharacterNameExactly = true,
            Scope = key.Scope,
            FromUtc = CurrentPeriodStartUtc(nowUtc),
            ToUtc = nowUtc,
            ShowCategories = timeline ? VisibleCategories : ActivityCategories.All
        };
    }

    private async Task RefreshCurrentPeriodAsync(CancellationToken cancellationToken)
    {
        if (tracker is null) return;
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
        if (changed && (contextChanged || nowUtc - currentPeriodReadAtUtc >= TimeSpan.FromSeconds(5)))
        {
            var snapshot = await tracker.GetPeriodSnapshotAsync(query, nowUtc, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (isDisposed || !isActive || SelectedTabIndex != 0 || key != GetCurrentPeriodKey()) return;
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
        // Sliding-range totals and rates share the saved cutoff until their next refresh.
        // Since-app-start duration can advance within the existing open observation visit.
        var presentationEndUtc = key.Mode == ActivityPeriodMode.LastHours ? currentPeriodToUtc : nowUtc;
        var rates = ActivityPeriodProjection.GetRates(snapshot, currentPeriodFromUtc, presentationEndUtc);
        CurrentSummary = new(snapshot.Summary, rates, true);
        CurrentDurationText = $"Tracked time: {ActivityDisplay.Duration(rates.DurationSeconds)}";
        var fromUtc = currentPeriodFromUtc;
        CurrentPeriodDescription = currentSession is not null
            ? $"Since {ActivityDisplay.LocalDate(fromUtc)}"
            : $"{key.CharacterName} · {ActivityDisplay.Server(key.Server)} · Since {ActivityDisplay.LocalDate(fromUtc)}";
        CurrentPeriodWarning = snapshot.HasExcludedImports
            ? "Imported gathering totals are excluded from this time range." : "";
        if (snapshot.IsPartial)
            CurrentPeriodDescription += " · incomplete recording";
    }

    private void ClearCurrentPeriod()
    {
        ClearCurrentLoot();
        currentPeriodKey = null;
        currentPeriodSnapshot = null;
        currentObservationKey = null;
        currentPeriodReadAtUtc = DateTime.MinValue;
        CurrentSummary = ActivitySummaryPresentation.Empty;
        if (CurrentResources.Count > 0) CurrentResources = new();
        CurrentDurationText = SelectedPeriod.Value == ActivityPeriodMode.CurrentSession ? "Session time: 0:00" : "Tracked time: 0:00";
        CurrentPeriodDescription = SelectedPeriod.Value == ActivityPeriodMode.CurrentSession ? "Waiting for activity" : "Waiting for your character";
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
        OnPropertyChanged(nameof(CurrentTotalsHeading));
        if (isActive && SelectedTabIndex == 0) _ = RunSafelyAsync(RefreshVisibleAsync);
    }

    partial void OnSelectedPeriodChanged(ActivityOption<ActivityPeriodMode> value) => ChangeCurrentPeriod();
    partial void OnLastHoursChanged(decimal value)
    {
        var normalized = Math.Clamp(Math.Round(value, 0, MidpointRounding.AwayFromZero), 1m, 168m);
        if (normalized != value) { LastHours = normalized; return; }
        if (IsLastHoursPeriod) ChangeCurrentPeriod();
    }
    partial void OnCurrentPeriodWarningChanged(string value) => OnPropertyChanged(nameof(HasCurrentPeriodWarning));
}

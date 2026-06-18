using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Combat.Models;
using AlbionDataAvalonia.Settings;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace AlbionDataAvalonia.ViewModels;

public partial class CombatViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan ChartRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ActiveSummaryRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InactiveFameSummaryRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ChartAggregationThirtyMinuteThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ChartAggregationOneHourThreshold = TimeSpan.FromHours(1);
    private static readonly TimeSpan ChartAggregationTwoHourThreshold = TimeSpan.FromHours(2);
    private static readonly TimeSpan ChartAggregationTenHourThreshold = TimeSpan.FromHours(10);
    private static readonly CombatAggregationOptionViewModel[] AllAggregationOptions =
    {
        new(1),
        new(3),
        new(5),
        new(10),
        new(30),
        new(60)
    };

    private readonly CombatTrackerService? combatTracker;
    private readonly SettingsManager? settingsManager;
    private DispatcherTimer? summaryRefreshTimer;
    private IDisposable? pendingChartRefreshRegistration;
    private CombatTrackerSnapshot currentSnapshot;
    private bool applyingSnapshot;
    private bool applyingPlayerSelection;
    private bool applyingAggregationSelection;
    private string? lastChartRenderSignature;
    private DateTime lastChartRefreshUtc = DateTime.MinValue;
    private string? selectedEncounterKey;
    private string? selectedPlayerKey;
    private int desiredAggregationSeconds = 1;

    [ObservableProperty]
    private string elapsedText = "00:00";

    [ObservableProperty]
    private string damagePerSecondText = "0.0";

    [ObservableProperty]
    private string damageReceivedPerSecondText = "0.0";

    [ObservableProperty]
    private string healingPerSecondText = "0.0";

    [ObservableProperty]
    private string healingReceivedPerSecondText = "0.0";

    [ObservableProperty]
    private string famePerHourText = "0.0";

    [ObservableProperty]
    private string silverPerHourText = "0.0";

    [ObservableProperty]
    private CombatAggregationOptionViewModel selectedAggregation;

    [ObservableProperty]
    private CombatChartWindowOptionViewModel selectedChartWindow;

    [ObservableProperty]
    private CombatChartMetricOptionViewModel selectedChartMetric;

    [ObservableProperty]
    private CombatPlayerFilterOptionViewModel selectedPlayerFilter;

    [ObservableProperty]
    private int partyMemberCount;

    [ObservableProperty]
    private bool showMissingPlayerWarning = true;

    [ObservableProperty]
    private string localPlayerText = "Player not set";

    [ObservableProperty]
    private string summaryScopeText = "No encounters yet";

    [ObservableProperty]
    private string summaryTargetText = "Player not set";

    [ObservableProperty]
    private string summaryWindowLabel = "Visible window (all)";

    [ObservableProperty]
    private string summaryDamageDealtTotalText = "0";

    [ObservableProperty]
    private string summaryDamageDealtWindowText = "0";

    [ObservableProperty]
    private string summaryDamageDealtWindowRateText = "0.0";

    [ObservableProperty]
    private string summaryDamageReceivedTotalText = "0";

    [ObservableProperty]
    private string summaryDamageReceivedWindowText = "0";

    [ObservableProperty]
    private string summaryDamageReceivedWindowRateText = "0.0";

    [ObservableProperty]
    private string summaryHealingDoneTotalText = "0";

    [ObservableProperty]
    private string summaryHealingDoneWindowText = "0";

    [ObservableProperty]
    private string summaryHealingDoneWindowRateText = "0.0";

    [ObservableProperty]
    private string summaryHealingReceivedTotalText = "0";

    [ObservableProperty]
    private string summaryHealingReceivedWindowText = "0";

    [ObservableProperty]
    private string summaryHealingReceivedWindowRateText = "0.0";

    [ObservableProperty]
    private string summaryFameTotalText = "0";

    [ObservableProperty]
    private string summaryFameWindowText = "0";

    [ObservableProperty]
    private string summaryFameWindowRateText = "0.0";

    [ObservableProperty]
    private bool isFameVisible = true;

    [ObservableProperty]
    private string summarySilverTotalText = "0";

    [ObservableProperty]
    private string summarySilverWindowText = "0";

    [ObservableProperty]
    private string summarySilverWindowRateText = "0.0";

    [ObservableProperty]
    private bool isSilverVisible = true;

    [ObservableProperty]
    private string chartTitle = "Combat over time";

    [ObservableProperty]
    private CombatPlayerRowViewModel? selectedPlayer;

    [ObservableProperty]
    private CombatEncounterListItemViewModel? selectedEncounter;

    [ObservableProperty]
    private string selectedEncounterTitleText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterStatusText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterLocationText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterStartedText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterDurationText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterFameText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterSilverText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterDamageDealtText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterDamageDealtPerSecondText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterHealingDoneText = string.Empty;

    [ObservableProperty]
    private string selectedEncounterHealingDonePerSecondText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerNameText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerRoleText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerSilverText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerDamageDealtText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerDamageDealtPerSecondText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerDamageDealtPartyPercentText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerDamageReceivedText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerDamageReceivedPerSecondText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerHealingDoneText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerHealingDonePerSecondText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerHealingDonePartyPercentText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerHealingReceivedText = string.Empty;

    [ObservableProperty]
    private string selectedPlayerHealingReceivedPerSecondText = string.Empty;

    [ObservableProperty]
    private bool isCombatTrackerDisabled;

    [ObservableProperty]
    private bool isCombatTrackerPaused;

    [ObservableProperty]
    private bool useAllPartyStats;

    public bool IsCombatTrackerEnabled => !IsCombatTrackerDisabled;
    public bool HasSelectedPlayer => SelectedPlayer is not null;
    public bool HasSelectedEncounter => SelectedEncounter is not null;
    public string PauseButtonText => IsCombatTrackerPaused ? "Resume" : "Pause";

    public ObservableCollection<CombatPlayerRowViewModel> Players { get; } = new();
    public ObservableCollection<CombatEncounterListItemViewModel> Encounters { get; } = new();
    public ObservableCollection<CombatAggregationOptionViewModel> AggregationOptions { get; } = new();
    public ObservableCollection<CombatChartWindowOptionViewModel> ChartWindowOptions { get; } = new();
    public ObservableCollection<CombatChartMetricOptionViewModel> ChartMetricOptions { get; } = new();
    public ObservableCollection<CombatPlayerFilterOptionViewModel> PlayerFilterOptions { get; } = new();
    public ObservableCollection<CombatPlayerRowViewModel> SelectedEncounterParticipants { get; } = new();

    [ObservableProperty]
    private ISeries[] chartSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private Axis[] chartXAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] chartYAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private RectangularSection[] chartSections = Array.Empty<RectangularSection>();

    public CombatViewModel()
    {
        currentSnapshot = CombatTrackerSnapshot.Empty();
        isCombatTrackerDisabled = false;
        selectedChartWindow = InitializeChartWindowOptions();
        selectedAggregation = InitializeAggregationOptions();
        selectedChartMetric = InitializeChartMetricOptions();
        selectedPlayerFilter = InitializePlayerFilterOptions();
    }

    public CombatViewModel(CombatTrackerService combatTracker, SettingsManager settingsManager)
    {
        this.combatTracker = combatTracker;
        this.settingsManager = settingsManager;
        isCombatTrackerDisabled = settingsManager.UserSettings.DisableCombatTracker;
        currentSnapshot = combatTracker.CurrentSnapshot;
        isCombatTrackerPaused = currentSnapshot.IsPaused;
        selectedChartWindow = InitializeChartWindowOptions();
        selectedAggregation = InitializeAggregationOptions();
        selectedChartMetric = InitializeChartMetricOptions();
        selectedPlayerFilter = InitializePlayerFilterOptions();
        ApplySnapshot(currentSnapshot);
        settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        combatTracker.SnapshotChanged += OnSnapshotChanged;
    }

    partial void OnIsCombatTrackerDisabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCombatTrackerEnabled));
    }

    partial void OnIsCombatTrackerPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseButtonText));
    }

    partial void OnUseAllPartyStatsChanged(bool value)
    {
        RefreshDisplayedSummary(DateTime.UtcNow);
        RequestChartRefresh(immediate: true);
    }

    partial void OnSelectedPlayerChanged(CombatPlayerRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedPlayer));

        if (applyingPlayerSelection)
        {
            return;
        }

        selectedPlayerKey = value?.EntityKey;
        RefreshSelectedPlayerDetails();
    }

    partial void OnSelectedEncounterChanged(CombatEncounterListItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedEncounter));

        if (applyingSnapshot)
        {
            return;
        }

        selectedEncounterKey = value?.EncounterKey;
        RefreshSelectedEncounterParticipants();
        RefreshSelectedEncounterDetails();
    }

    partial void OnSelectedAggregationChanged(CombatAggregationOptionViewModel value)
    {
        if (applyingAggregationSelection || value is null)
        {
            return;
        }

        desiredAggregationSeconds = value.Seconds;
        RefreshDisplayedSummary(DateTime.UtcNow);
        RequestChartRefresh(immediate: true);
    }

    partial void OnSelectedChartWindowChanged(CombatChartWindowOptionViewModel value)
    {
        RefreshDisplayedSummary(DateTime.UtcNow);
        RequestChartRefresh(immediate: true);
    }

    partial void OnSelectedChartMetricChanged(CombatChartMetricOptionViewModel value)
    {
        RequestChartRefresh(immediate: true);
    }

    partial void OnSelectedPlayerFilterChanged(CombatPlayerFilterOptionViewModel value)
    {
        RefreshPlayers();
    }

    [RelayCommand]
    private void Reset()
    {
        combatTracker?.Reset();
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (combatTracker is null)
        {
            IsCombatTrackerPaused = !IsCombatTrackerPaused;
            return;
        }

        combatTracker.SetPaused(!IsCombatTrackerPaused);
    }

    [RelayCommand]
    private void ClearSelectedEncounter()
    {
        ClearSelectedEncounterState();
    }

    [RelayCommand]
    private void ClearSelectedPlayer()
    {
        ClearSelectedPlayerState();
    }

    private void OnSnapshotChanged(CombatTrackerSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.DisableCombatTracker))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (settingsManager is not null)
            {
                IsCombatTrackerDisabled = settingsManager.UserSettings.DisableCombatTracker;
            }
        });
    }

    private void OnSummaryRefreshTimerTick(object? sender, EventArgs e)
    {
        var nowUtc = DateTime.UtcNow;
        RefreshDisplayedSummary(nowUtc);
        RequestChartRefresh();
    }

    private void ApplySnapshot(CombatTrackerSnapshot snapshot)
    {
        currentSnapshot = snapshot;
        IsCombatTrackerPaused = snapshot.IsPaused;
        PartyMemberCount = snapshot.PartyMemberCount;
        ShowMissingPlayerWarning = !snapshot.HasLocalPlayer;
        LocalPlayerText = snapshot.LocalPlayer is null
            ? "Player not set"
            : snapshot.LocalPlayer.Name;

        RefreshEncounters();
        RefreshDisplayedSummary(DateTime.UtcNow);
        RefreshPlayers();

        RequestChartRefresh();
    }

    private void UpdateSummaryRefreshTimer(
        bool includeFame,
        long totalFameGained,
        DateTime? firstFameBucketStartedAtUtc,
        bool includeSilver,
        long totalSilverGained,
        DateTime? firstSilverBucketStartedAtUtc)
    {
        var shouldRefresh = currentSnapshot.IsEncounterActive
            || includeFame && totalFameGained > 0 && firstFameBucketStartedAtUtc is not null
            || includeSilver && totalSilverGained > 0 && firstSilverBucketStartedAtUtc is not null;
        if (!shouldRefresh)
        {
            StopSummaryRefreshTimer();
            return;
        }

        summaryRefreshTimer ??= CreateSummaryRefreshTimer();
        var interval = currentSnapshot.IsEncounterActive
            ? ActiveSummaryRefreshInterval
            : InactiveFameSummaryRefreshInterval;
        if (summaryRefreshTimer.Interval != interval)
        {
            summaryRefreshTimer.Interval = interval;
        }

        if (!summaryRefreshTimer.IsEnabled)
        {
            summaryRefreshTimer.Start();
        }
    }

    private DispatcherTimer CreateSummaryRefreshTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = ActiveSummaryRefreshInterval
        };
        timer.Tick += OnSummaryRefreshTimerTick;
        return timer;
    }

    private void StopSummaryRefreshTimer()
    {
        summaryRefreshTimer?.Stop();
    }

    private void ClearSelectedEncounterState()
    {
        selectedEncounterKey = null;
        SelectedEncounterParticipants.Clear();

        applyingSnapshot = true;
        try
        {
            SelectedEncounter = null;
        }
        finally
        {
            applyingSnapshot = false;
        }

        RefreshSelectedEncounterDetails();
    }

    private void ClearSelectedPlayerState()
    {
        selectedPlayerKey = null;

        applyingPlayerSelection = true;
        try
        {
            SelectedPlayer = null;
        }
        finally
        {
            applyingPlayerSelection = false;
        }
    }

    private void RefreshDisplayedSummary(DateTime nowUtc)
    {
        var encounterArray = currentSnapshot.Encounters.ToArray();
        var displayEndUtc = nowUtc;
        RefreshAggregationOptions(encounterArray, displayEndUtc);
        var elapsed = SumElapsed(encounterArray, nowUtc);
        var metricTarget = CreateMetricTarget(encounterArray);
        var includeFame = ShouldShowFame();
        var includeSilver = ShouldShowSilver();
        var aggregatedBuckets = AggregateBuckets(
            encounterArray,
            GetCurrentAggregationSeconds(),
            metricTarget.IncludeEntity,
            includeFame,
            includeSilver,
            fillMissingBuckets: false).ToArray();
        var totals = AggregateTotals(encounterArray, metricTarget.IncludeEntity, includeFame, includeSilver);
        var firstFameBucketStartedAtUtc = includeFame ? GetFirstFameBucketStartedAtUtc(encounterArray) : null;
        var fameElapsed = GetPauseAdjustedElapsedSince(firstFameBucketStartedAtUtc, displayEndUtc, currentSnapshot.PauseIntervals);
        var firstSilverBucketStartedAtUtc = includeSilver ? GetFirstSilverBucketStartedAtUtc(encounterArray, metricTarget.IncludeEntity) : null;
        var silverElapsed = GetPauseAdjustedElapsedSince(firstSilverBucketStartedAtUtc, displayEndUtc, currentSnapshot.PauseIntervals);
        var visibleBuckets = GetVisibleBuckets(aggregatedBuckets, displayEndUtc);
        var firstVisibleFameBucketStartedAtUtc = includeFame ? GetFirstFameBucketStartedAtUtc(visibleBuckets) : null;
        var visibleFameBucketStartedAtUtc = SelectedChartWindow.Duration is null
            ? firstFameBucketStartedAtUtc
            : firstVisibleFameBucketStartedAtUtc;
        var firstVisibleSilverBucketStartedAtUtc = includeSilver ? GetFirstSilverBucketStartedAtUtc(visibleBuckets) : null;
        var visibleSilverBucketStartedAtUtc = SelectedChartWindow.Duration is null
            ? firstSilverBucketStartedAtUtc
            : firstVisibleSilverBucketStartedAtUtc;
        var windowTotals = AggregateTotals(visibleBuckets);
        var visibleFameElapsed = GetPauseAdjustedVisibleWallClockDuration(
            visibleBuckets,
            visibleFameBucketStartedAtUtc,
            displayEndUtc,
            SelectedChartWindow.Duration,
            currentSnapshot.PauseIntervals);
        var visibleSilverElapsed = GetPauseAdjustedVisibleWallClockDuration(
            visibleBuckets,
            visibleSilverBucketStartedAtUtc,
            displayEndUtc,
            SelectedChartWindow.Duration,
            currentSnapshot.PauseIntervals);
        var visibleWindowDuration = GetVisibleEncounterDuration(
            encounterArray,
            nowUtc,
            displayEndUtc,
            SelectedChartWindow.Duration);

        IsFameVisible = includeFame;
        IsSilverVisible = includeSilver;
        ElapsedText = FormatDuration(elapsed);
        SummaryTargetText = metricTarget.Label;
        SummaryScopeText = CreateEncounterScopeLabel();
        SummaryWindowLabel = CreateSummaryWindowLabel();
        SummaryDamageDealtTotalText = FormatAmount(totals.DamageDealt);
        DamagePerSecondText = FormatRate(CalculateRate(totals.DamageDealt, elapsed));
        SummaryDamageDealtWindowText = FormatAmount(windowTotals.DamageDealt);
        SummaryDamageDealtWindowRateText = FormatRate(CalculateRate(windowTotals.DamageDealt, visibleWindowDuration));
        SummaryDamageReceivedTotalText = FormatAmount(totals.DamageReceived);
        DamageReceivedPerSecondText = FormatRate(CalculateRate(totals.DamageReceived, elapsed));
        SummaryDamageReceivedWindowText = FormatAmount(windowTotals.DamageReceived);
        SummaryDamageReceivedWindowRateText = FormatRate(CalculateRate(windowTotals.DamageReceived, visibleWindowDuration));
        SummaryHealingDoneTotalText = FormatAmount(totals.HealingDone);
        HealingPerSecondText = FormatRate(CalculateRate(totals.HealingDone, elapsed));
        SummaryHealingDoneWindowText = FormatAmount(windowTotals.HealingDone);
        SummaryHealingDoneWindowRateText = FormatRate(CalculateRate(windowTotals.HealingDone, visibleWindowDuration));
        SummaryHealingReceivedTotalText = FormatAmount(totals.HealingReceived);
        HealingReceivedPerSecondText = FormatRate(CalculateRate(totals.HealingReceived, elapsed));
        SummaryHealingReceivedWindowText = FormatAmount(windowTotals.HealingReceived);
        SummaryHealingReceivedWindowRateText = FormatRate(CalculateRate(windowTotals.HealingReceived, visibleWindowDuration));
        SummaryFameTotalText = FormatAmount(totals.FameGained);
        FamePerHourText = FormatWholeRate(CalculateHourlyRate(totals.FameGained, fameElapsed));
        SummaryFameWindowText = FormatAmount(windowTotals.FameGained);
        SummaryFameWindowRateText = FormatWholeRate(CalculateHourlyRate(windowTotals.FameGained, visibleFameElapsed));
        SummarySilverTotalText = FormatAmount(totals.SilverGained);
        SilverPerHourText = FormatWholeRate(CalculateHourlyRate(totals.SilverGained, silverElapsed));
        SummarySilverWindowText = FormatAmount(windowTotals.SilverGained);
        SummarySilverWindowRateText = FormatWholeRate(CalculateHourlyRate(windowTotals.SilverGained, visibleSilverElapsed));
        UpdateSummaryRefreshTimer(
            includeFame,
            totals.FameGained,
            firstFameBucketStartedAtUtc,
            includeSilver,
            totals.SilverGained,
            firstSilverBucketStartedAtUtc);
    }

    private static CombatEncounterListItemViewModel CreateEncounterItem(CombatEncounterSnapshot encounter)
    {
        var duration = encounter.Elapsed;
        var partyTotals = GetPartyTotals(encounter);
        return new CombatEncounterListItemViewModel(
            encounter.EncounterKey,
            encounter.EncounterNumber,
            encounter.LocationName,
            encounter.StartedAtUtc,
            encounter.IsActive ? "Active" : "Ended",
            duration,
            encounter.TotalFameGained,
            encounter.TotalSilverGained,
            partyTotals.DamageDealt,
            CalculateRate(partyTotals.DamageDealt, duration),
            partyTotals.DamageReceived,
            CalculateRate(partyTotals.DamageReceived, duration),
            partyTotals.HealingDone,
            CalculateRate(partyTotals.HealingDone, duration),
            partyTotals.HealingReceived,
            CalculateRate(partyTotals.HealingReceived, duration),
            encounter.IsActive,
            encounter);
    }

    private static CombatMetricTotals GetPartyTotals(CombatEncounterSnapshot encounter)
    {
        var totals = new CombatMetricTotals();
        foreach (var player in encounter.Players.Where(x => x.Role == CombatEntityRole.PartyPlayer))
        {
            totals.DamageDealt += player.DamageDealt;
            totals.DamageReceived += player.DamageReceived;
            totals.HealingDone += player.HealingDone;
            totals.HealingReceived += player.HealingReceived;
            totals.SilverGained += player.SilverGained;
        }

        return totals;
    }

    private void RefreshEncounters()
    {
        var requestedSelectedEncounterKey = selectedEncounterKey ?? SelectedEncounter?.EncounterKey;
        var encounters = currentSnapshot.Encounters
            .Select(CreateEncounterItem)
            .ToArray();

        applyingSnapshot = true;
        try
        {
            Replace(Encounters, encounters);

            var selectedEncounter = string.IsNullOrEmpty(requestedSelectedEncounterKey)
                ? null
                : Encounters.FirstOrDefault(x => x.EncounterKey == requestedSelectedEncounterKey);

            SelectedEncounter = selectedEncounter;
            selectedEncounterKey = selectedEncounter?.EncounterKey;
        }
        finally
        {
            applyingSnapshot = false;
        }

        RefreshSelectedEncounterParticipants();
        RefreshSelectedEncounterDetails();
    }

    private void RefreshPlayers()
    {
        var encounters = currentSnapshot.Encounters.ToArray();
        var requestedSelectedPlayerKey = selectedPlayerKey ?? SelectedPlayer?.EntityKey;
        var duration = SumElapsed(encounters, DateTime.UtcNow);
        var aggregatedPlayers = AggregatePlayers(encounters);
        var totalPartyDamageDealt = aggregatedPlayers
            .Where(player => player.Role == CombatEntityRole.PartyPlayer)
            .Sum(player => player.DamageDealt);
        var totalPartyHealingDone = aggregatedPlayers
            .Where(player => player.Role == CombatEntityRole.PartyPlayer)
            .Sum(player => player.HealingDone);
        var players = aggregatedPlayers
            .Where(MatchesSelectedPlayerFilter)
            .OrderBy(player => GetRoleSortOrder(player.Role))
            .ThenByDescending(player => player.DamageDealt)
            .ThenByDescending(player => player.HealingDone)
            .ThenByDescending(player => player.SilverGained)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .Select(player => CreatePlayerRow(player, duration, totalPartyDamageDealt, totalPartyHealingDone))
            .ToArray();

        applyingPlayerSelection = true;
        try
        {
            Replace(Players, players);

            SelectedPlayer = string.IsNullOrEmpty(requestedSelectedPlayerKey)
                ? null
                : Players.FirstOrDefault(x => x.EntityKey == requestedSelectedPlayerKey);
            selectedPlayerKey = SelectedPlayer?.EntityKey;
        }
        finally
        {
            applyingPlayerSelection = false;
        }

        RefreshSelectedPlayerDetails();
    }

    private void RefreshSelectedEncounterParticipants()
    {
        if (SelectedEncounter is not { Encounter: { } encounter })
        {
            SelectedEncounterParticipants.Clear();
            return;
        }

        var duration = GetElapsed(encounter, DateTime.UtcNow);
        var aggregatedPlayers = AggregatePlayers(new[] { encounter });
        var totalPartyDamageDealt = aggregatedPlayers
            .Where(player => player.Role == CombatEntityRole.PartyPlayer)
            .Sum(player => player.DamageDealt);
        var totalPartyHealingDone = aggregatedPlayers
            .Where(player => player.Role == CombatEntityRole.PartyPlayer)
            .Sum(player => player.HealingDone);
        var participants = aggregatedPlayers
            .OrderBy(player => GetRoleSortOrder(player.Role))
            .ThenByDescending(player => player.DamageDealt)
            .ThenByDescending(player => player.HealingDone)
            .ThenByDescending(player => player.SilverGained)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .Select(player => CreatePlayerRow(player, duration, totalPartyDamageDealt, totalPartyHealingDone));

        Replace(SelectedEncounterParticipants, participants);
    }

    private void RefreshSelectedEncounterDetails()
    {
        if (SelectedEncounter is not { } encounter)
        {
            SelectedEncounterTitleText = string.Empty;
            SelectedEncounterStatusText = string.Empty;
            SelectedEncounterLocationText = string.Empty;
            SelectedEncounterStartedText = string.Empty;
            SelectedEncounterDurationText = string.Empty;
            SelectedEncounterFameText = string.Empty;
            SelectedEncounterSilverText = string.Empty;
            SelectedEncounterDamageDealtText = string.Empty;
            SelectedEncounterDamageDealtPerSecondText = string.Empty;
            SelectedEncounterHealingDoneText = string.Empty;
            SelectedEncounterHealingDonePerSecondText = string.Empty;
            return;
        }

        SelectedEncounterTitleText = $"Combat #{encounter.EncounterNumber}";
        SelectedEncounterStatusText = encounter.Status;
        SelectedEncounterLocationText = encounter.LocationName;
        SelectedEncounterStartedText = FormatUtcTime(encounter.StartedAtUtc);
        SelectedEncounterDurationText = encounter.DurationDisplay;
        SelectedEncounterFameText = FormatAmount(encounter.FameGained);
        SelectedEncounterSilverText = FormatAmount(encounter.SilverGained);
        SelectedEncounterDamageDealtText = FormatAmount(encounter.DamageDealt);
        SelectedEncounterDamageDealtPerSecondText = FormatRate(encounter.DamageDealtPerSecond);
        SelectedEncounterHealingDoneText = FormatAmount(encounter.HealingDone);
        SelectedEncounterHealingDonePerSecondText = FormatRate(encounter.HealingDonePerSecond);
    }

    private void RefreshSelectedPlayerDetails()
    {
        if (SelectedPlayer is not { } player)
        {
            SelectedPlayerNameText = string.Empty;
            SelectedPlayerRoleText = string.Empty;
            SelectedPlayerSilverText = string.Empty;
            SelectedPlayerDamageDealtText = string.Empty;
            SelectedPlayerDamageDealtPerSecondText = string.Empty;
            SelectedPlayerDamageDealtPartyPercentText = string.Empty;
            SelectedPlayerDamageReceivedText = string.Empty;
            SelectedPlayerDamageReceivedPerSecondText = string.Empty;
            SelectedPlayerHealingDoneText = string.Empty;
            SelectedPlayerHealingDonePerSecondText = string.Empty;
            SelectedPlayerHealingDonePartyPercentText = string.Empty;
            SelectedPlayerHealingReceivedText = string.Empty;
            SelectedPlayerHealingReceivedPerSecondText = string.Empty;
            return;
        }

        SelectedPlayerNameText = player.Name;
        SelectedPlayerRoleText = player.RoleLabel;
        SelectedPlayerSilverText = FormatAmount(player.SilverGained);
        SelectedPlayerDamageDealtText = FormatAmount(player.DamageDealt);
        SelectedPlayerDamageDealtPerSecondText = FormatRate(player.DamageDealtPerSecond);
        SelectedPlayerDamageDealtPartyPercentText = player.DamageDealtPartyPercentText;
        SelectedPlayerDamageReceivedText = FormatAmount(player.DamageReceived);
        SelectedPlayerDamageReceivedPerSecondText = FormatRate(player.DamageReceivedPerSecond);
        SelectedPlayerHealingDoneText = FormatAmount(player.HealingDone);
        SelectedPlayerHealingDonePerSecondText = FormatRate(player.HealingDonePerSecond);
        SelectedPlayerHealingDonePartyPercentText = player.HealingDonePartyPercentText;
        SelectedPlayerHealingReceivedText = FormatAmount(player.HealingReceived);
        SelectedPlayerHealingReceivedPerSecondText = FormatRate(player.HealingReceivedPerSecond);
    }

    private void RequestChartRefresh(bool immediate = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RequestChartRefresh(immediate));
            return;
        }

        if (immediate)
        {
            CancelPendingChartRefresh();
            RefreshChart();
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var elapsed = nowUtc - lastChartRefreshUtc;
        if (lastChartRefreshUtc == DateTime.MinValue || elapsed >= ChartRefreshInterval)
        {
            CancelPendingChartRefresh();
            RefreshChart();
            return;
        }

        if (pendingChartRefreshRegistration is not null)
        {
            return;
        }

        var delay = elapsed < TimeSpan.Zero
            ? ChartRefreshInterval
            : ChartRefreshInterval - elapsed;

        pendingChartRefreshRegistration = DispatcherTimer.RunOnce(() =>
        {
            pendingChartRefreshRegistration = null;
            RefreshChart();
        }, delay);
    }

    private void CancelPendingChartRefresh()
    {
        pendingChartRefreshRegistration?.Dispose();
        pendingChartRefreshRegistration = null;
    }

    private void RefreshChart()
    {
        var nowUtc = DateTime.UtcNow;
        lastChartRefreshUtc = nowUtc;
        var encounters = currentSnapshot.Encounters.ToArray();
        var displayEndUtc = nowUtc;
        var metricTarget = CreateMetricTarget(encounters);
        var includeFame = SelectedChartMetric.Kind == CombatChartMetricKind.Fame && ShouldShowFame();
        var includeSilver = SelectedChartMetric.Kind == CombatChartMetricKind.Silver && ShouldShowSilver();
        RefreshAggregationOptions(encounters, displayEndUtc);
        var effectiveAggregationSeconds = GetCurrentAggregationSeconds();
        ChartTitle = CreateChartTitle(metricTarget.Label);
        var pauseSections = CreatePauseSections(currentSnapshot.PauseIntervals, SelectedChartWindow.Duration, displayEndUtc);

        if (!metricTarget.HasTarget && !includeFame && !includeSilver)
        {
            var emptyBuckets = Array.Empty<AggregatedCombatBucket>();
            lastChartRenderSignature = CreateChartRenderSignature(emptyBuckets, pauseSections, metricTarget.SignatureKey, includeFame, includeSilver, effectiveAggregationSeconds, displayEndUtc);
            ChartSeries = Array.Empty<ISeries>();
            ChartXAxes = CreateChartXAxes(emptyBuckets, effectiveAggregationSeconds, SelectedChartWindow.Duration, displayEndUtc);
            ChartYAxes = CreateChartYAxes(0);
            ChartSections = pauseSections;
            return;
        }

        var buckets = AggregateBuckets(
            encounters,
            effectiveAggregationSeconds,
            metricTarget.IncludeEntity,
            includeFame,
            includeSilver,
            SelectedChartWindow.Duration,
            displayEndUtc,
            extendToDisplayEnd: SelectedChartWindow.Duration is null && encounters.Any(x => x.IsActive)).ToArray();
        var chartBuckets = CompressZeroChartBuckets(buckets, SelectedChartMetric.Kind, includeFame, includeSilver);
        var signature = CreateChartRenderSignature(chartBuckets, pauseSections, metricTarget.SignatureKey, includeFame, includeSilver, effectiveAggregationSeconds, displayEndUtc);
        if (signature == lastChartRenderSignature)
        {
            return;
        }

        lastChartRenderSignature = signature;
        if (chartBuckets.Count == 0)
        {
            ChartSeries = Array.Empty<ISeries>();
            ChartXAxes = CreateChartXAxes(chartBuckets, effectiveAggregationSeconds, SelectedChartWindow.Duration, displayEndUtc);
            ChartYAxes = CreateChartYAxes(0);
            ChartSections = pauseSections;
            return;
        }

        var maxValue = chartBuckets
            .SelectMany(bucket => GetChartValues(bucket, includeFame, includeSilver, SelectedChartMetric.Kind))
            .DefaultIfEmpty(0)
            .Max();

        ChartSeries = CreateChartSeries(chartBuckets, SelectedChartMetric.Kind, includeFame, includeSilver);
        ChartXAxes = CreateChartXAxes(chartBuckets, effectiveAggregationSeconds, SelectedChartWindow.Duration, displayEndUtc);
        ChartYAxes = CreateChartYAxes(maxValue);
        ChartSections = pauseSections;
    }

    private IEnumerable<AggregatedCombatBucket> AggregateBuckets(
        IEnumerable<CombatEncounterSnapshot> encounters,
        int aggregationSeconds,
        Func<string, bool> includeEntity,
        bool includeFame,
        bool includeSilver,
        TimeSpan? chartWindow = null,
        DateTime? displayEndUtc = null,
        bool fillMissingBuckets = true,
        bool extendToDisplayEnd = false)
    {
        var encounterArray = encounters as IReadOnlyList<CombatEncounterSnapshot> ?? encounters.ToArray();
        var aggregationTicks = TimeSpan.FromSeconds(aggregationSeconds).Ticks;
        var minimumBucketTicks = GetMinimumVisibleBucketTicks(aggregationTicks, chartWindow, displayEndUtc);
        var shouldUseDisplayEnd = displayEndUtc is not null && (chartWindow is not null || extendToDisplayEnd);
        long? maximumBucketTicks = shouldUseDisplayEnd
            ? GetAggregatedBucketTicks(displayEndUtc!.Value, aggregationTicks)
            : null;
        var groupedBuckets = encounterArray
            .SelectMany(encounter => encounter.TimeBuckets.Select(bucket =>
            {
                var bucketTicks = GetAggregatedBucketTicks(encounter.StartedAtUtc.Add(bucket.StartOffset), aggregationTicks);
                var damageDealt = 0L;
                var damageReceived = 0L;
                var healingDone = 0L;
                var healingReceived = 0L;
                var silverGained = 0L;
                foreach (var player in bucket.PlayerTotals)
                {
                    if (!includeEntity(player.EntityKey))
                    {
                        continue;
                    }

                    damageDealt += player.DamageDealt;
                    damageReceived += player.DamageReceived;
                    healingDone += player.HealingDone;
                    healingReceived += player.HealingReceived;
                    silverGained += includeSilver ? player.SilverGained : 0;
                }

                return new
                {
                    BucketTicks = bucketTicks,
                    DamageDealt = damageDealt,
                    DamageReceived = damageReceived,
                    HealingDone = healingDone,
                    HealingReceived = healingReceived,
                    FameGained = includeFame ? bucket.FameGained : 0,
                    SilverGained = silverGained
                };
            }))
            .Where(x => (minimumBucketTicks is null || x.BucketTicks >= minimumBucketTicks.Value)
                && (maximumBucketTicks is null || x.BucketTicks <= maximumBucketTicks.Value))
            .GroupBy(x => x.BucketTicks)
            .OrderBy(x => x.Key)
            .Select(x =>
            {
                return new AggregatedCombatBucket(
                    new DateTime(x.Key, DateTimeKind.Utc),
                    x.Sum(bucket => bucket.DamageDealt),
                    x.Sum(bucket => bucket.DamageReceived),
                    x.Sum(bucket => bucket.HealingDone),
                    x.Sum(bucket => bucket.HealingReceived),
                    x.Sum(bucket => bucket.FameGained),
                    x.Sum(bucket => bucket.SilverGained));
            })
            .ToArray();

        if (groupedBuckets.Length == 0)
        {
            return fillMissingBuckets
                ? CreateEmptyBuckets(aggregationSeconds, minimumBucketTicks, maximumBucketTicks)
                : Array.Empty<AggregatedCombatBucket>();
        }

        return fillMissingBuckets
            ? FillMissingBuckets(groupedBuckets, aggregationSeconds, minimumBucketTicks, maximumBucketTicks)
            : groupedBuckets;
    }

    private static long? GetMinimumVisibleBucketTicks(
        long aggregationTicks,
        TimeSpan? chartWindow,
        DateTime? displayEndUtc)
    {
        if (chartWindow is null || displayEndUtc is null)
        {
            return null;
        }

        return GetAggregatedBucketTicks(displayEndUtc.Value - chartWindow.Value, aggregationTicks);
    }

    private static long GetAggregatedBucketTicks(DateTime bucketStartedAtUtc, long aggregationTicks)
    {
        return bucketStartedAtUtc.Ticks / aggregationTicks * aggregationTicks;
    }

    private static IEnumerable<AggregatedCombatBucket> FillMissingBuckets(
        IReadOnlyList<AggregatedCombatBucket> buckets,
        int aggregationSeconds,
        long? firstTicksOverride = null,
        long? lastTicksOverride = null)
    {
        var bucketsByTicks = buckets.ToDictionary(x => x.StartedAtUtc.Ticks);
        var aggregationTicks = TimeSpan.FromSeconds(aggregationSeconds).Ticks;
        var firstTicks = firstTicksOverride is { } firstOverrideTicks && firstOverrideTicks < buckets[0].StartedAtUtc.Ticks
            ? firstOverrideTicks
            : buckets[0].StartedAtUtc.Ticks;
        var lastTicks = lastTicksOverride is { } lastOverrideTicks && lastOverrideTicks > buckets[^1].StartedAtUtc.Ticks
            ? lastOverrideTicks
            : buckets[^1].StartedAtUtc.Ticks;

        for (var ticks = firstTicks; ticks <= lastTicks; ticks += aggregationTicks)
        {
            if (bucketsByTicks.TryGetValue(ticks, out var bucket))
            {
                yield return bucket;
                continue;
            }

            yield return new AggregatedCombatBucket(new DateTime(ticks, DateTimeKind.Utc), 0, 0, 0, 0, 0, 0);
        }
    }

    private static IEnumerable<AggregatedCombatBucket> CreateEmptyBuckets(
        int aggregationSeconds,
        long? firstTicks,
        long? lastTicks)
    {
        if (firstTicks is not { } startTicks || lastTicks is not { } endTicks || endTicks < startTicks)
        {
            return Array.Empty<AggregatedCombatBucket>();
        }

        var buckets = new List<AggregatedCombatBucket>();
        var aggregationTicks = TimeSpan.FromSeconds(aggregationSeconds).Ticks;
        for (var ticks = startTicks; ticks <= endTicks; ticks += aggregationTicks)
        {
            buckets.Add(new AggregatedCombatBucket(new DateTime(ticks, DateTimeKind.Utc), 0, 0, 0, 0, 0, 0));
        }

        return buckets;
    }

    private void RefreshAggregationOptions(
        IReadOnlyList<CombatEncounterSnapshot> encounters,
        DateTime displayEndUtc)
    {
        var visibleSpan = SelectedChartWindow?.Duration ?? GetUnlimitedChartSpan(encounters, displayEndUtc);
        var minimumAggregationSeconds = GetMinimumChartAggregationSeconds(visibleSpan);
        var options = AllAggregationOptions
            .Where(x => x.Seconds >= minimumAggregationSeconds)
            .ToArray();
        var selectedAggregationSeconds = Math.Max(desiredAggregationSeconds, minimumAggregationSeconds);
        var selectedOption = options.FirstOrDefault(x => x.Seconds >= selectedAggregationSeconds) ?? options[^1];

        applyingAggregationSelection = true;
        try
        {
            SyncAggregationOptions(options);

            if (SelectedAggregation?.Seconds != selectedOption.Seconds)
            {
                SelectedAggregation = selectedOption;
            }
        }
        finally
        {
            applyingAggregationSelection = false;
        }
    }

    private int GetCurrentAggregationSeconds()
    {
        return SelectedAggregation?.Seconds ?? desiredAggregationSeconds;
    }

    private void SyncAggregationOptions(IReadOnlyList<CombatAggregationOptionViewModel> options)
    {
        for (var index = AggregationOptions.Count - 1; index >= 0; index--)
        {
            if (!options.Any(x => x.Seconds == AggregationOptions[index].Seconds))
            {
                AggregationOptions.RemoveAt(index);
            }
        }

        for (var index = 0; index < options.Count; index++)
        {
            if (index < AggregationOptions.Count && AggregationOptions[index].Seconds == options[index].Seconds)
            {
                continue;
            }

            var existingIndex = FindAggregationOptionIndex(options[index].Seconds);
            if (existingIndex >= 0)
            {
                AggregationOptions.Move(existingIndex, index);
            }
            else
            {
                AggregationOptions.Insert(index, options[index]);
            }
        }
    }

    private int FindAggregationOptionIndex(int seconds)
    {
        for (var index = 0; index < AggregationOptions.Count; index++)
        {
            if (AggregationOptions[index].Seconds == seconds)
            {
                return index;
            }
        }

        return -1;
    }

    private static int GetMinimumChartAggregationSeconds(TimeSpan visibleSpan)
    {
        if (visibleSpan >= ChartAggregationTenHourThreshold)
        {
            return 30;
        }

        if (visibleSpan >= ChartAggregationTwoHourThreshold)
        {
            return 10;
        }

        if (visibleSpan >= ChartAggregationOneHourThreshold)
        {
            return 5;
        }

        if (visibleSpan >= ChartAggregationThirtyMinuteThreshold)
        {
            return 3;
        }

        return 1;
    }

    private static TimeSpan GetUnlimitedChartSpan(
        IReadOnlyList<CombatEncounterSnapshot> encounters,
        DateTime displayEndUtc)
    {
        DateTime? firstBucketStartedAtUtc = null;
        DateTime? lastBucketEndedAtUtc = null;

        foreach (var encounter in encounters)
        {
            foreach (var bucket in encounter.TimeBuckets)
            {
                var bucketStartedAtUtc = encounter.StartedAtUtc.Add(bucket.StartOffset);
                var bucketEndedAtUtc = encounter.StartedAtUtc.Add(bucket.EndOffset);

                if (firstBucketStartedAtUtc is null || bucketStartedAtUtc < firstBucketStartedAtUtc.Value)
                {
                    firstBucketStartedAtUtc = bucketStartedAtUtc;
                }

                if (lastBucketEndedAtUtc is null || bucketEndedAtUtc > lastBucketEndedAtUtc.Value)
                {
                    lastBucketEndedAtUtc = bucketEndedAtUtc;
                }
            }

            if (encounter.IsActive)
            {
                if (firstBucketStartedAtUtc is null || encounter.StartedAtUtc < firstBucketStartedAtUtc.Value)
                {
                    firstBucketStartedAtUtc = encounter.StartedAtUtc;
                }

                if (lastBucketEndedAtUtc is null || displayEndUtc > lastBucketEndedAtUtc.Value)
                {
                    lastBucketEndedAtUtc = displayEndUtc;
                }
            }
        }

        if (firstBucketStartedAtUtc is null
            || lastBucketEndedAtUtc is null
            || lastBucketEndedAtUtc.Value <= firstBucketStartedAtUtc.Value)
        {
            return TimeSpan.Zero;
        }

        return lastBucketEndedAtUtc.Value - firstBucketStartedAtUtc.Value;
    }

    private static IReadOnlyList<AggregatedCombatBucket> CompressZeroChartBuckets(
        IReadOnlyList<AggregatedCombatBucket> buckets,
        CombatChartMetricKind metricKind,
        bool includeFame,
        bool includeSilver)
    {
        if (buckets.Count < 3)
        {
            return buckets;
        }

        var compressedBuckets = new List<AggregatedCombatBucket>(buckets.Count);
        var index = 0;
        while (index < buckets.Count)
        {
            var bucket = buckets[index];
            if (!IsZeroChartBucket(bucket, metricKind, includeFame, includeSilver))
            {
                compressedBuckets.Add(bucket);
                index++;
                continue;
            }

            var zeroRunStart = index;
            do
            {
                index++;
            }
            while (index < buckets.Count && IsZeroChartBucket(buckets[index], metricKind, includeFame, includeSilver));

            var zeroRunLength = index - zeroRunStart;
            compressedBuckets.Add(buckets[zeroRunStart]);
            if (zeroRunLength > 1)
            {
                compressedBuckets.Add(buckets[index - 1]);
            }
        }

        return compressedBuckets.Count == buckets.Count
            ? buckets
            : compressedBuckets;
    }

    private static bool IsZeroChartBucket(
        AggregatedCombatBucket bucket,
        CombatChartMetricKind metricKind,
        bool includeFame,
        bool includeSilver)
    {
        if (metricKind == CombatChartMetricKind.Fame)
        {
            return !includeFame || bucket.FameGained == 0;
        }

        if (metricKind == CombatChartMetricKind.Silver)
        {
            return !includeSilver || bucket.SilverGained == 0;
        }

        return bucket.DamageDealt == 0
            && bucket.DamageReceived == 0;
    }

    private bool ShouldShowFame()
    {
        return !UseAllPartyStats;
    }

    private bool ShouldShowSilver()
    {
        if (UseAllPartyStats)
        {
            return true;
        }

        return currentSnapshot.LocalPlayer is not null;
    }

    private CombatMetricTarget CreateMetricTarget(IReadOnlyList<CombatEncounterSnapshot> encounters)
    {
        if (UseAllPartyStats)
        {
            var partyKeys = encounters
                .SelectMany(encounter => encounter.Players)
                .Where(player => player.Role == CombatEntityRole.PartyPlayer)
                .Select(player => player.EntityKey)
                .ToHashSet(StringComparer.Ordinal);

            return new CombatMetricTarget(
                "party",
                "Party",
                partyKeys.Contains,
                partyKeys.Count > 0);
        }

        var targetEntityKey = GetSingleMetricTargetEntityKey();
        return new CombatMetricTarget(
            targetEntityKey ?? "none",
            GetSingleMetricTargetLabel(),
            CreateEntityPredicate(targetEntityKey),
            !string.IsNullOrEmpty(targetEntityKey));
    }

    private static Func<string, bool> CreateEntityPredicate(string? selectedEntityKey)
    {
        if (string.IsNullOrEmpty(selectedEntityKey))
        {
            return _ => false;
        }

        return entityKey => entityKey == selectedEntityKey;
    }

    private static IReadOnlyList<CombatPlayerSummary> AggregatePlayers(IEnumerable<CombatEncounterSnapshot> encounters)
    {
        return encounters
            .SelectMany(x => x.Players)
            .GroupBy(x => x.EntityKey)
            .Select(x => new CombatPlayerSummary(
                x.Key,
                x.First().Name,
                x.First().Role,
                x.Sum(player => player.DamageDealt),
                x.Sum(player => player.DamageReceived),
                x.Sum(player => player.HealingDone),
                x.Sum(player => player.HealingReceived),
                x.Sum(player => player.SilverGained)))
            .Where(x => x.DamageDealt > 0 || x.DamageReceived > 0 || x.HealingDone > 0 || x.HealingReceived > 0 || x.SilverGained > 0)
            .ToArray();
    }

    private static CombatPlayerRowViewModel CreatePlayerRow(
        CombatPlayerSummary player,
        TimeSpan duration,
        long totalPartyDamageDealt,
        long totalPartyHealingDone)
    {
        var damageDealtPartyPercent = CalculatePartyPercent(player, player.DamageDealt, totalPartyDamageDealt);
        var healingDonePartyPercent = CalculatePartyPercent(player, player.HealingDone, totalPartyHealingDone);

        return new CombatPlayerRowViewModel(
            player.EntityKey,
            player.Name,
            player.Role,
            GetRoleLabel(player.Role),
            GetRoleSortOrder(player.Role),
            player.DamageDealt,
            damageDealtPartyPercent,
            FormatPercent(damageDealtPartyPercent),
            CalculateRate(player.DamageDealt, duration),
            player.DamageReceived,
            CalculateRate(player.DamageReceived, duration),
            player.HealingDone,
            healingDonePartyPercent,
            FormatPercent(healingDonePartyPercent),
            CalculateRate(player.HealingDone, duration),
            player.HealingReceived,
            CalculateRate(player.HealingReceived, duration),
            player.SilverGained);
    }

    private static double? CalculatePartyPercent(CombatPlayerSummary player, long amount, long totalPartyAmount)
    {
        if (player.Role != CombatEntityRole.PartyPlayer || totalPartyAmount <= 0)
        {
            return null;
        }

        return amount * 100d / totalPartyAmount;
    }

    private bool MatchesSelectedPlayerFilter(CombatPlayerSummary player)
    {
        return SelectedPlayerFilter.Kind switch
        {
            CombatPlayerFilterKind.Party => player.Role == CombatEntityRole.PartyPlayer,
            CombatPlayerFilterKind.Players => player.Role is CombatEntityRole.PartyPlayer or CombatEntityRole.Player,
            CombatPlayerFilterKind.Mobs => player.Role == CombatEntityRole.Mob,
            _ => true
        };
    }

    private static int GetRoleSortOrder(CombatEntityRole role)
    {
        return role switch
        {
            CombatEntityRole.PartyPlayer => 0,
            CombatEntityRole.Player => 1,
            CombatEntityRole.Mob => 2,
            _ => 3
        };
    }

    private static string GetRoleLabel(CombatEntityRole role)
    {
        return role switch
        {
            CombatEntityRole.PartyPlayer => "Party Player",
            CombatEntityRole.Player => "Player",
            CombatEntityRole.Mob => "Mob",
            _ => "Unknown"
        };
    }

    private static CombatMetricTotals AggregateTotals(
        IEnumerable<CombatEncounterSnapshot> encounters,
        Func<string, bool> includeEntity,
        bool includeFame,
        bool includeSilver)
    {
        var totals = new CombatMetricTotals();
        var encounterArray = encounters as IReadOnlyList<CombatEncounterSnapshot> ?? encounters.ToArray();
        foreach (var player in AggregatePlayers(encounterArray).Where(x => includeEntity(x.EntityKey)))
        {
            totals.DamageDealt += player.DamageDealt;
            totals.DamageReceived += player.DamageReceived;
            totals.HealingDone += player.HealingDone;
            totals.HealingReceived += player.HealingReceived;
            totals.SilverGained += includeSilver ? player.SilverGained : 0;
        }

        if (includeFame)
        {
            totals.FameGained = encounterArray.Sum(x => x.TotalFameGained);
        }

        return totals;
    }

    private static CombatMetricTotals AggregateTotals(IEnumerable<AggregatedCombatBucket> buckets)
    {
        var totals = new CombatMetricTotals();
        foreach (var bucket in buckets)
        {
            totals.DamageDealt += bucket.DamageDealt;
            totals.DamageReceived += bucket.DamageReceived;
            totals.HealingDone += bucket.HealingDone;
            totals.HealingReceived += bucket.HealingReceived;
            totals.FameGained += bucket.FameGained;
            totals.SilverGained += bucket.SilverGained;
        }

        return totals;
    }

    private static TimeSpan SumElapsed(IEnumerable<CombatEncounterSnapshot> encounters, DateTime nowUtc)
    {
        return TimeSpan.FromTicks(encounters.Sum(x => GetElapsed(x, nowUtc).Ticks));
    }

    private static TimeSpan GetPauseAdjustedElapsedSince(
        DateTime? startedAtUtc,
        DateTime endUtc,
        IReadOnlyList<CombatPauseIntervalSnapshot> pauseIntervals)
    {
        if (startedAtUtc is not { } startUtc || endUtc <= startUtc)
        {
            return TimeSpan.Zero;
        }

        var elapsed = endUtc - startUtc;
        var paused = GetPauseOverlap(startUtc, endUtc, pauseIntervals);
        var adjusted = elapsed - paused;
        return adjusted > TimeSpan.Zero
            ? adjusted
            : TimeSpan.Zero;
    }

    private static TimeSpan GetPauseOverlap(
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        IReadOnlyList<CombatPauseIntervalSnapshot> pauseIntervals)
    {
        if (rangeEndUtc <= rangeStartUtc)
        {
            return TimeSpan.Zero;
        }

        long pausedTicks = 0;
        foreach (var interval in pauseIntervals)
        {
            var intervalStartUtc = interval.StartedAtUtc;
            var intervalEndUtc = interval.EndedAtUtc ?? rangeEndUtc;
            if (intervalEndUtc < intervalStartUtc)
            {
                intervalEndUtc = intervalStartUtc;
            }

            var overlapStartUtc = intervalStartUtc > rangeStartUtc ? intervalStartUtc : rangeStartUtc;
            var overlapEndUtc = intervalEndUtc < rangeEndUtc ? intervalEndUtc : rangeEndUtc;
            if (overlapEndUtc > overlapStartUtc)
            {
                pausedTicks += (overlapEndUtc - overlapStartUtc).Ticks;
            }
        }

        return TimeSpan.FromTicks(pausedTicks);
    }

    private static DateTime? GetFirstFameBucketStartedAtUtc(IEnumerable<CombatEncounterSnapshot> encounters)
    {
        DateTime? firstStartedAtUtc = null;
        foreach (var encounter in encounters)
        {
            foreach (var bucket in encounter.TimeBuckets)
            {
                if (bucket.FameGained <= 0)
                {
                    continue;
                }

                var bucketStartedAtUtc = encounter.StartedAtUtc.Add(bucket.StartOffset);
                if (firstStartedAtUtc is null || bucketStartedAtUtc < firstStartedAtUtc.Value)
                {
                    firstStartedAtUtc = bucketStartedAtUtc;
                }
            }
        }

        return firstStartedAtUtc;
    }

    private static DateTime? GetFirstFameBucketStartedAtUtc(IEnumerable<AggregatedCombatBucket> buckets)
    {
        DateTime? firstStartedAtUtc = null;
        foreach (var bucket in buckets)
        {
            if (bucket.FameGained <= 0)
            {
                continue;
            }

            if (firstStartedAtUtc is null || bucket.StartedAtUtc < firstStartedAtUtc.Value)
            {
                firstStartedAtUtc = bucket.StartedAtUtc;
            }
        }

        return firstStartedAtUtc;
    }

    private static DateTime? GetFirstSilverBucketStartedAtUtc(
        IEnumerable<CombatEncounterSnapshot> encounters,
        Func<string, bool> includeEntity)
    {
        DateTime? firstStartedAtUtc = null;
        foreach (var encounter in encounters)
        {
            foreach (var bucket in encounter.TimeBuckets)
            {
                var hasSilver = bucket.PlayerTotals.Any(player => includeEntity(player.EntityKey) && player.SilverGained > 0);
                if (!hasSilver)
                {
                    continue;
                }

                var bucketStartedAtUtc = encounter.StartedAtUtc.Add(bucket.StartOffset);
                if (firstStartedAtUtc is null || bucketStartedAtUtc < firstStartedAtUtc.Value)
                {
                    firstStartedAtUtc = bucketStartedAtUtc;
                }
            }
        }

        return firstStartedAtUtc;
    }

    private static DateTime? GetFirstSilverBucketStartedAtUtc(IEnumerable<AggregatedCombatBucket> buckets)
    {
        DateTime? firstStartedAtUtc = null;
        foreach (var bucket in buckets)
        {
            if (bucket.SilverGained <= 0)
            {
                continue;
            }

            if (firstStartedAtUtc is null || bucket.StartedAtUtc < firstStartedAtUtc.Value)
            {
                firstStartedAtUtc = bucket.StartedAtUtc;
            }
        }

        return firstStartedAtUtc;
    }

    private static TimeSpan GetPauseAdjustedVisibleWallClockDuration(
        IReadOnlyList<AggregatedCombatBucket> buckets,
        DateTime? firstVisibleFameBucketStartedAtUtc,
        DateTime displayEndUtc,
        TimeSpan? chartWindow,
        IReadOnlyList<CombatPauseIntervalSnapshot> pauseIntervals)
    {
        var visibleEndUtc = displayEndUtc;
        var visibleStartUtc = chartWindow is null
            ? firstVisibleFameBucketStartedAtUtc ?? (buckets.Count == 0 ? displayEndUtc : buckets[0].StartedAtUtc)
            : displayEndUtc - chartWindow.Value;

        if (visibleEndUtc <= visibleStartUtc)
        {
            return TimeSpan.Zero;
        }

        var duration = visibleEndUtc - visibleStartUtc;
        var paused = GetPauseOverlap(visibleStartUtc, visibleEndUtc, pauseIntervals);
        var adjusted = duration - paused;
        return adjusted > TimeSpan.Zero
            ? adjusted
            : TimeSpan.Zero;
    }

    private static TimeSpan GetElapsed(CombatEncounterSnapshot encounter, DateTime nowUtc)
    {
        var elapsed = encounter.IsActive
            ? nowUtc - encounter.StartedAtUtc
            : encounter.Elapsed;

        return elapsed < TimeSpan.Zero
            ? TimeSpan.Zero
            : elapsed;
    }

    private static IEnumerable<double> GetChartValues(
        AggregatedCombatBucket bucket,
        bool includeFame,
        bool includeSilver,
        CombatChartMetricKind metricKind)
    {
        if (metricKind == CombatChartMetricKind.Fame)
        {
            if (includeFame)
            {
                yield return bucket.FameGained;
            }

            yield break;
        }

        if (metricKind == CombatChartMetricKind.Silver)
        {
            if (includeSilver)
            {
                yield return bucket.SilverGained;
            }

            yield break;
        }

        yield return bucket.DamageDealt;
        yield return bucket.DamageReceived;
    }

    private IReadOnlyList<AggregatedCombatBucket> GetVisibleBuckets(IReadOnlyList<AggregatedCombatBucket> buckets, DateTime displayEndUtc)
    {
        if (SelectedChartWindow.Duration is not { } duration || buckets.Count == 0)
        {
            return buckets;
        }

        var minTicks = displayEndUtc.Ticks - duration.Ticks;
        var maxTicks = displayEndUtc.Ticks;

        return buckets
            .Where(bucket => bucket.StartedAtUtc.Ticks >= minTicks && bucket.StartedAtUtc.Ticks <= maxTicks)
            .ToArray();
    }

    private static TimeSpan GetVisibleEncounterDuration(
        IReadOnlyList<CombatEncounterSnapshot> encounters,
        DateTime nowUtc,
        DateTime displayEndUtc,
        TimeSpan? chartWindow)
    {
        if (encounters.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var visibleStartUtc = chartWindow is null
            ? GetEarliestEncounterStartUtc(encounters) ?? displayEndUtc
            : displayEndUtc - chartWindow.Value;
        var visibleEndUtc = chartWindow is null
            ? GetLatestEncounterEndUtc(encounters, nowUtc) ?? displayEndUtc
            : displayEndUtc;
        if (visibleEndUtc <= visibleStartUtc)
        {
            return TimeSpan.Zero;
        }

        long visibleTicks = 0;
        foreach (var encounter in encounters)
        {
            var encounterStartUtc = encounter.StartedAtUtc;
            var encounterEndUtc = encounter.StartedAtUtc + GetElapsed(encounter, nowUtc);
            var overlapStartUtc = encounterStartUtc > visibleStartUtc ? encounterStartUtc : visibleStartUtc;
            var overlapEndUtc = encounterEndUtc < visibleEndUtc ? encounterEndUtc : visibleEndUtc;

            if (overlapEndUtc > overlapStartUtc)
            {
                visibleTicks += (overlapEndUtc - overlapStartUtc).Ticks;
            }
        }

        return TimeSpan.FromTicks(visibleTicks);
    }

    private static DateTime? GetEarliestEncounterStartUtc(IReadOnlyList<CombatEncounterSnapshot> encounters)
    {
        DateTime? startedAtUtc = null;
        foreach (var encounter in encounters)
        {
            if (startedAtUtc is null || encounter.StartedAtUtc < startedAtUtc.Value)
            {
                startedAtUtc = encounter.StartedAtUtc;
            }
        }

        return startedAtUtc;
    }

    private static DateTime? GetLatestEncounterEndUtc(
        IReadOnlyList<CombatEncounterSnapshot> encounters,
        DateTime nowUtc)
    {
        DateTime? endedAtUtc = null;
        foreach (var encounter in encounters)
        {
            var encounterEndedAtUtc = encounter.StartedAtUtc + GetElapsed(encounter, nowUtc);
            if (endedAtUtc is null || encounterEndedAtUtc > endedAtUtc.Value)
            {
                endedAtUtc = encounterEndedAtUtc;
            }
        }

        return endedAtUtc;
    }

    private string CreateChartRenderSignature(
        IReadOnlyList<AggregatedCombatBucket> buckets,
        IReadOnlyList<RectangularSection> pauseSections,
        string chartTargetKey,
        bool includeFame,
        bool includeSilver,
        int aggregationSeconds,
        DateTime displayEndUtc)
    {
        var builder = new StringBuilder();
        builder.Append(chartTargetKey)
            .Append('|')
            .Append(UseAllPartyStats)
            .Append('|')
            .Append(includeFame)
            .Append('|')
            .Append(includeSilver)
            .Append('|')
            .Append(aggregationSeconds)
            .Append('|')
            .Append(SelectedChartMetric.Kind)
            .Append('|')
            .Append(SelectedChartWindow.Duration?.Ticks)
            .Append('|')
            .Append(SelectedChartWindow.Duration is null ? displayEndUtc.Ticks : 0)
            .Append('|')
            .Append(buckets.Count)
            .Append('|')
            .Append(pauseSections.Count);

        foreach (var section in pauseSections)
        {
            builder.Append('|')
                .Append(section.Xi)
                .Append(':')
                .Append(section.Xj);
        }

        if (buckets.Count == 0)
        {
            return builder.ToString();
        }

        foreach (var bucket in buckets)
        {
            builder.Append('|')
                .Append(bucket.StartedAtUtc.Ticks);

            if (SelectedChartMetric.Kind == CombatChartMetricKind.Damage)
            {
                builder.Append(':')
                    .Append(bucket.DamageDealt)
                    .Append(':')
                    .Append(bucket.DamageReceived);
            }
            else if (SelectedChartMetric.Kind == CombatChartMetricKind.Fame)
            {
                builder.Append(':')
                    .Append(bucket.FameGained);
            }
            else if (SelectedChartMetric.Kind == CombatChartMetricKind.Silver)
            {
                builder.Append(':')
                    .Append(bucket.SilverGained);
            }
        }

        return builder.ToString();
    }

    private string? GetSingleMetricTargetEntityKey()
    {
        return currentSnapshot.LocalPlayer?.EntityKey;
    }

    private string GetSingleMetricTargetLabel()
    {
        return currentSnapshot.LocalPlayer?.Name
            ?? "Player not set";
    }

    private string CreateEncounterScopeLabel()
    {
        return currentSnapshot.Encounters.Count == 0
            ? "No encounters yet"
            : "All encounters";
    }

    private string CreateSummaryWindowLabel()
    {
        return SelectedChartWindow.Duration is null
            ? "Visible window (all)"
            : $"Visible window ({SelectedChartWindow.Label})";
    }

    private string CreateChartTitle(string targetLabel)
    {
        return $"Combat over time - {targetLabel} - {CreateEncounterScopeLabel()}";
    }

    private sealed record CombatMetricTarget(
        string SignatureKey,
        string Label,
        Func<string, bool> IncludeEntity,
        bool HasTarget);

    private static RectangularSection[] CreatePauseSections(
        IReadOnlyList<CombatPauseIntervalSnapshot> pauseIntervals,
        TimeSpan? chartWindow,
        DateTime displayEndUtc)
    {
        if (pauseIntervals.Count == 0)
        {
            return Array.Empty<RectangularSection>();
        }

        var visibleStartUtc = chartWindow is { } window
            ? displayEndUtc - window
            : (DateTime?)null;
        var sections = new List<RectangularSection>();

        foreach (var interval in pauseIntervals)
        {
            var intervalStartUtc = interval.StartedAtUtc;
            var intervalEndUtc = interval.EndedAtUtc ?? displayEndUtc;
            if (intervalEndUtc < intervalStartUtc)
            {
                intervalEndUtc = intervalStartUtc;
            }

            if (intervalStartUtc >= displayEndUtc)
            {
                continue;
            }

            if (visibleStartUtc is { } visibleStart && intervalEndUtc <= visibleStart)
            {
                continue;
            }

            var sectionStartUtc = visibleStartUtc is { } minStart && intervalStartUtc < minStart
                ? minStart
                : intervalStartUtc;
            var sectionEndUtc = intervalEndUtc > displayEndUtc
                ? displayEndUtc
                : intervalEndUtc;

            if (sectionEndUtc <= sectionStartUtc)
            {
                continue;
            }

            sections.Add(new RectangularSection
            {
                Xi = sectionStartUtc.Ticks,
                Xj = sectionEndUtc.Ticks,
                Fill = new SolidColorPaint(new SKColor(220, 72, 72, 34)),
                Stroke = null,
                ZIndex = -1
            });
        }

        return sections.ToArray();
    }

    private static ISeries[] CreateChartSeries(
        IReadOnlyList<AggregatedCombatBucket> buckets,
        CombatChartMetricKind metricKind,
        bool includeFame,
        bool includeSilver)
    {
        if (metricKind == CombatChartMetricKind.Fame)
        {
            return includeFame
                ? new ISeries[]
                {
                    CreateLineSeries("Fame", buckets, x => x.FameGained, SKColors.Gold)
                }
                : Array.Empty<ISeries>();
        }

        if (metricKind == CombatChartMetricKind.Silver)
        {
            return includeSilver
                ? new ISeries[]
                {
                    CreateLineSeries("Silver", buckets, x => x.SilverGained, SKColors.Silver)
                }
                : Array.Empty<ISeries>();
        }

        return new ISeries[]
        {
            CreateLineSeries("Damage dealt", buckets, x => x.DamageDealt, SKColors.DeepSkyBlue),
            CreateLineSeries("Damage received", buckets, x => x.DamageReceived, SKColors.LightCoral)
        };
    }

    private static LineSeries<DateTimePoint> CreateLineSeries(
        string name,
        IReadOnlyList<AggregatedCombatBucket> buckets,
        Func<AggregatedCombatBucket, double> selector,
        SKColor color)
    {
        return new LineSeries<DateTimePoint>
        {
            Name = name,
            Values = buckets
                .Select(bucket => new DateTimePoint(bucket.StartedAtUtc, selector(bucket)))
                .ToArray(),
            XToolTipLabelFormatter = chartPoint => FormatUtcTooltipTime(chartPoint.Model?.DateTime ?? DateTime.MinValue),
            YToolTipLabelFormatter = chartPoint =>
                FormatChartValue(chartPoint.Coordinate.PrimaryValue),
            AnimationsSpeed = TimeSpan.Zero,
            Fill = null,
            Stroke = new SolidColorPaint(color)
            {
                StrokeThickness = 1.5f
            },
            GeometryFill = null,
            GeometryStroke = null,
            GeometrySize = 12,
            LineSmoothness = 0
        };
    }

    private static Axis[] CreateChartXAxes(
        IReadOnlyList<AggregatedCombatBucket> buckets,
        int aggregationSeconds,
        TimeSpan? chartWindow,
        DateTime displayEndUtc)
    {
        var maxTicks = displayEndUtc.Ticks;
        double? minLimit = null;
        double? maxLimit = maxTicks;
        if (chartWindow is { } window)
        {
            var minTicks = maxTicks - window.Ticks;

            minLimit = minTicks;
        }

        return new[]
        {
            new Axis
            {
                Name = "Time",
                Labeler = FormatUtcAxisTick,
                NameTextSize = 12,
                TextSize = 11,
                MinStep = TimeSpan.FromSeconds(aggregationSeconds).Ticks,
                MinLimit = minLimit,
                MaxLimit = maxLimit,
                LabelsRotation = 0
            }
        };
    }

    private static Axis[] CreateChartYAxes(double maxValue)
    {
        return new[]
        {
            new Axis
            {
                Name = "Amount",
                Labeler = FormatChartValue,
                NameTextSize = 12,
                TextSize = 11,
                MinLimit = 0,
                MaxLimit = maxValue > 0 ? null : 1
            }
        };
    }

    private static string FormatDuration(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private static string FormatAmount(long value)
    {
        return value.ToString("N0");
    }

    private static string FormatRate(double value)
    {
        return value.ToString("N1");
    }

    private static string FormatWholeRate(double value)
    {
        return value.ToString("N0");
    }

    private static string FormatPercent(double? value)
    {
        return value is null
            ? string.Empty
            : $"{value.Value:N1}%";
    }

    private static string FormatChartValue(double value) => value.ToString("N0");

    private static double CalculateRate(long amount, TimeSpan duration)
    {
        return duration.TotalSeconds > 0
            ? amount / duration.TotalSeconds
            : 0;
    }

    private static double CalculateHourlyRate(long amount, TimeSpan duration)
    {
        return duration.TotalSeconds > 0
            ? amount * 3600d / duration.TotalSeconds
            : 0;
    }

    private static string FormatUtcTime(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("HH:mm:ss 'UTC'");
    }

    private static string FormatUtcAxisTime(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("HH:mm:ss");
    }

    private static string FormatUtcAxisTick(double value)
    {
        if (!double.IsFinite(value)
            || value < DateTime.MinValue.Ticks
            || value > DateTime.MaxValue.Ticks)
        {
            return string.Empty;
        }

        return FormatUtcAxisTime(new DateTime((long)value, DateTimeKind.Utc));
    }

    private static string FormatUtcTooltipTime(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("HH:mm:ss 'UTC'");
    }

    private CombatAggregationOptionViewModel InitializeAggregationOptions()
    {
        desiredAggregationSeconds = AllAggregationOptions[0].Seconds;
        Replace(AggregationOptions, AllAggregationOptions);
        return AllAggregationOptions[0];
    }

    private CombatChartWindowOptionViewModel InitializeChartWindowOptions()
    {
        var options = new[]
        {
            new CombatChartWindowOptionViewModel(TimeSpan.FromMinutes(1), "1m"),
            new CombatChartWindowOptionViewModel(TimeSpan.FromMinutes(5), "5m"),
            new CombatChartWindowOptionViewModel(TimeSpan.FromMinutes(10), "10m"),
            new CombatChartWindowOptionViewModel(TimeSpan.FromMinutes(30), "30m"),
            new CombatChartWindowOptionViewModel(TimeSpan.FromHours(1), "1h"),
            new CombatChartWindowOptionViewModel(TimeSpan.FromHours(2), "2h"),
            new CombatChartWindowOptionViewModel(TimeSpan.FromHours(3), "3h"),
            new CombatChartWindowOptionViewModel(TimeSpan.FromHours(4), "4h"),
            new CombatChartWindowOptionViewModel(TimeSpan.FromHours(5), "5h"),
            new CombatChartWindowOptionViewModel(TimeSpan.FromHours(10), "10h"),
            new CombatChartWindowOptionViewModel(null, "Unlimited")
        };

        Replace(ChartWindowOptions, options);
        return options[0];
    }

    private CombatChartMetricOptionViewModel InitializeChartMetricOptions()
    {
        var options = new[]
        {
            new CombatChartMetricOptionViewModel(CombatChartMetricKind.Damage, "Damage"),
            new CombatChartMetricOptionViewModel(CombatChartMetricKind.Fame, "Fame"),
            new CombatChartMetricOptionViewModel(CombatChartMetricKind.Silver, "Silver")
        };

        Replace(ChartMetricOptions, options);
        return options[0];
    }

    private CombatPlayerFilterOptionViewModel InitializePlayerFilterOptions()
    {
        var options = new[]
        {
            new CombatPlayerFilterOptionViewModel(CombatPlayerFilterKind.All, "All"),
            new CombatPlayerFilterOptionViewModel(CombatPlayerFilterKind.Party, "Only Party"),
            new CombatPlayerFilterOptionViewModel(CombatPlayerFilterKind.Players, "Only Players"),
            new CombatPlayerFilterOptionViewModel(CombatPlayerFilterKind.Mobs, "Only Mobs")
        };

        Replace(PlayerFilterOptions, options);
        return options[0];
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    public void Dispose()
    {
        if (combatTracker is not null)
        {
            combatTracker.SnapshotChanged -= OnSnapshotChanged;
        }

        if (settingsManager is not null)
        {
            settingsManager.UserSettings.PropertyChanged -= OnUserSettingsPropertyChanged;
        }

        if (summaryRefreshTimer is not null)
        {
            summaryRefreshTimer.Tick -= OnSummaryRefreshTimerTick;
            summaryRefreshTimer.Stop();
            summaryRefreshTimer = null;
        }

        CancelPendingChartRefresh();
    }
}

public sealed record CombatAggregationOptionViewModel(int Seconds)
{
    public string Label => $"{Seconds}s";
}

public sealed record CombatChartWindowOptionViewModel(TimeSpan? Duration, string Label);

public enum CombatChartMetricKind
{
    Damage,
    Fame,
    Silver
}

public sealed record CombatChartMetricOptionViewModel(CombatChartMetricKind Kind, string Label);

public enum CombatPlayerFilterKind
{
    All,
    Party,
    Players,
    Mobs
}

public sealed record CombatPlayerFilterOptionViewModel(CombatPlayerFilterKind Kind, string Label);

public sealed record AggregatedCombatBucket(
    DateTime StartedAtUtc,
    long DamageDealt,
    long DamageReceived,
    long HealingDone,
    long HealingReceived,
    long FameGained,
    long SilverGained);

public sealed record CombatPlayerRowViewModel(
    string EntityKey,
    string Name,
    CombatEntityRole Role,
    string RoleLabel,
    int RoleSortOrder,
    long DamageDealt,
    double? DamageDealtPartyPercent,
    string DamageDealtPartyPercentText,
    double DamageDealtPerSecond,
    long DamageReceived,
    double DamageReceivedPerSecond,
    long HealingDone,
    double? HealingDonePartyPercent,
    string HealingDonePartyPercentText,
    double HealingDonePerSecond,
    long HealingReceived,
    double HealingReceivedPerSecond,
    long SilverGained);

public sealed record CombatEncounterListItemViewModel(
    string EncounterKey,
    int EncounterNumber,
    string LocationName,
    DateTime StartedAtUtc,
    string Status,
    TimeSpan Duration,
    long FameGained,
    long SilverGained,
    long DamageDealt,
    double DamageDealtPerSecond,
    long DamageReceived,
    double DamageReceivedPerSecond,
    long HealingDone,
    double HealingDonePerSecond,
    long HealingReceived,
    double HealingReceivedPerSecond,
    bool IsActive,
    CombatEncounterSnapshot Encounter)
{
    public string DurationDisplay
    {
        get
        {
            var roundedDuration = TimeSpan.FromSeconds(Math.Round(
                Math.Max(0, Duration.TotalSeconds),
                MidpointRounding.AwayFromZero));

            return roundedDuration.TotalHours >= 1
                ? roundedDuration.ToString(@"h\:mm\:ss")
                : roundedDuration.ToString(@"mm\:ss");
        }
    }
}

public sealed class CombatMetricTotals
{
    public long DamageDealt { get; set; }
    public long DamageReceived { get; set; }
    public long HealingDone { get; set; }
    public long HealingReceived { get; set; }
    public long FameGained { get; set; }
    public long SilverGained { get; set; }
}

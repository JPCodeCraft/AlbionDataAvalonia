using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Combat.Models;
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
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace AlbionDataAvalonia.ViewModels;

public partial class CombatViewModel : ViewModelBase, IDisposable
{
    private readonly CombatTrackerService? combatTracker;
    private DispatcherTimer? activeEncounterRefreshTimer;
    private CombatTrackerSnapshot currentSnapshot;
    private CombatEncounterSnapshot? selectedEncounterSnapshot;
    private IReadOnlyList<CombatBreakdownRow> currentDamageDealtRows = Array.Empty<CombatBreakdownRow>();
    private IReadOnlyList<CombatBreakdownRow> currentDamageReceivedRows = Array.Empty<CombatBreakdownRow>();
    private IReadOnlyList<CombatBreakdownRow> currentHealingDoneRows = Array.Empty<CombatBreakdownRow>();
    private IReadOnlyList<CombatBreakdownRow> currentHealingReceivedRows = Array.Empty<CombatBreakdownRow>();
    private bool applyingSnapshot;
    private bool applyingPlayerSelection;
    private bool userSelectedEncounter;
    private bool userClearedEncounterSelection;
    private string? lastChartRenderSignature;

    [ObservableProperty]
    private string encounterStatus = "Idle";

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
    private CombatAggregationOptionViewModel selectedAggregation;

    [ObservableProperty]
    private CombatSideFilterViewModel selectedSideFilter;

    [ObservableProperty]
    private CombatChartWindowOptionViewModel selectedChartWindow;

    [ObservableProperty]
    private CombatChartMetricOptionViewModel selectedChartMetric;

    [ObservableProperty]
    private int partyMemberCount;

    [ObservableProperty]
    private bool showMissingPlayerWarning = true;

    [ObservableProperty]
    private string localPlayerText = "Player not set";

    [ObservableProperty]
    private CombatPlayerRowViewModel? selectedPlayer;

    [ObservableProperty]
    private CombatEncounterListItemViewModel? selectedEncounter;

    public ObservableCollection<CombatPlayerRowViewModel> Players { get; } = new();
    public ObservableCollection<CombatEncounterListItemViewModel> Encounters { get; } = new();
    public ObservableCollection<CombatEntityViewModel> TrackedEntities { get; } = new();
    public ObservableCollection<CombatBreakdownRow> DamageDealtRows { get; } = new();
    public ObservableCollection<CombatBreakdownRow> DamageReceivedRows { get; } = new();
    public ObservableCollection<CombatBreakdownRow> HealingDoneRows { get; } = new();
    public ObservableCollection<CombatBreakdownRow> HealingReceivedRows { get; } = new();
    public ObservableCollection<CombatAggregationOptionViewModel> AggregationOptions { get; } = new();
    public ObservableCollection<CombatSideFilterViewModel> SideFilterOptions { get; } = new();
    public ObservableCollection<CombatChartWindowOptionViewModel> ChartWindowOptions { get; } = new();
    public ObservableCollection<CombatChartMetricOptionViewModel> ChartMetricOptions { get; } = new();

    [ObservableProperty]
    private ISeries[] chartSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private Axis[] chartXAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] chartYAxes = Array.Empty<Axis>();

    public CombatViewModel()
    {
        currentSnapshot = CombatTrackerSnapshot.Empty();
        selectedAggregation = InitializeAggregationOptions();
        selectedSideFilter = InitializeSideFilterOptions();
        selectedChartWindow = InitializeChartWindowOptions();
        selectedChartMetric = InitializeChartMetricOptions();
    }

    public CombatViewModel(CombatTrackerService combatTracker)
    {
        this.combatTracker = combatTracker;
        currentSnapshot = combatTracker.CurrentSnapshot;
        selectedAggregation = InitializeAggregationOptions();
        selectedSideFilter = InitializeSideFilterOptions();
        selectedChartWindow = InitializeChartWindowOptions();
        selectedChartMetric = InitializeChartMetricOptions();
        ApplySnapshot(currentSnapshot);
        combatTracker.SnapshotChanged += OnSnapshotChanged;
    }

    partial void OnSelectedPlayerChanged(CombatPlayerRowViewModel? value)
    {
        if (applyingPlayerSelection)
        {
            return;
        }

        ApplyCurrentFilters();
    }

    partial void OnSelectedEncounterChanged(CombatEncounterListItemViewModel? value)
    {
        if (!applyingSnapshot)
        {
            userSelectedEncounter = value is not null;
            userClearedEncounterSelection = value is null;
        }

        ApplySelectedEncounter(value?.Encounter);
    }

    partial void OnSelectedAggregationChanged(CombatAggregationOptionViewModel value)
    {
        RefreshChart();
    }

    partial void OnSelectedSideFilterChanged(CombatSideFilterViewModel value)
    {
        ApplyCurrentFilters();
    }

    partial void OnSelectedChartWindowChanged(CombatChartWindowOptionViewModel value)
    {
        RefreshChart();
    }

    partial void OnSelectedChartMetricChanged(CombatChartMetricOptionViewModel value)
    {
        RefreshChart();
    }

    [RelayCommand]
    private void Reset()
    {
        combatTracker?.Reset();
    }

    [RelayCommand]
    private void ClearSelectedEncounter()
    {
        userSelectedEncounter = false;
        userClearedEncounterSelection = true;
        SelectedEncounter = null;
        ApplySelectedEncounter(null);
    }

    [RelayCommand]
    private void ClearSelectedPlayer()
    {
        if (SelectedPlayer is null)
        {
            ApplyCurrentFilters();
            return;
        }

        SelectedPlayer = null;
    }

    private void OnSnapshotChanged(CombatTrackerSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
    }

    private void OnActiveEncounterRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!currentSnapshot.IsEncounterActive)
        {
            StopActiveEncounterRefreshTimer();
            return;
        }

        RefreshDisplayedSummary(DateTime.UtcNow);
    }

    private void ApplySnapshot(CombatTrackerSnapshot snapshot)
    {
        currentSnapshot = snapshot;
        EncounterStatus = snapshot.IsEncounterActive ? "Active" : snapshot.EncounterEndedAtUtc is null ? "Idle" : "Ended";
        PartyMemberCount = snapshot.PartyMemberCount;
        ShowMissingPlayerWarning = !snapshot.HasLocalPlayer;
        LocalPlayerText = snapshot.LocalPlayer is null
            ? "Player not set"
            : FormatEntity(snapshot.LocalPlayer);
        Replace(TrackedEntities, snapshot.TrackedEntities.Select(x => new CombatEntityViewModel(
            x.Name,
            x.IsLocalPlayer ? "Player" : "Party")));

        var selectedEncounterKey = SelectedEncounter?.EncounterKey;
        Replace(Encounters, snapshot.Encounters.Select(CreateEncounterItem));

        var selectedEncounter = userSelectedEncounter && !string.IsNullOrEmpty(selectedEncounterKey)
            ? Encounters.FirstOrDefault(x => x.EncounterKey == selectedEncounterKey)
            : null;
        if (!userClearedEncounterSelection)
        {
            selectedEncounter ??= Encounters.FirstOrDefault(x => x.IsActive) ?? Encounters.FirstOrDefault();
        }

        applyingSnapshot = true;
        SelectedEncounter = selectedEncounter;
        applyingSnapshot = false;
        ApplySelectedEncounter(selectedEncounter?.Encounter);
        UpdateActiveEncounterRefreshTimer(snapshot.IsEncounterActive);
    }

    private void UpdateActiveEncounterRefreshTimer(bool isEncounterActive)
    {
        if (!isEncounterActive)
        {
            StopActiveEncounterRefreshTimer();
            return;
        }

        activeEncounterRefreshTimer ??= CreateActiveEncounterRefreshTimer();
        if (!activeEncounterRefreshTimer.IsEnabled)
        {
            activeEncounterRefreshTimer.Start();
        }
    }

    private DispatcherTimer CreateActiveEncounterRefreshTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += OnActiveEncounterRefreshTimerTick;
        return timer;
    }

    private void StopActiveEncounterRefreshTimer()
    {
        activeEncounterRefreshTimer?.Stop();
    }

    private void RefreshBreakdowns()
    {
        RefreshBreakdowns(CreateEntityPredicate());
    }

    private void RefreshBreakdowns(Func<string, bool> includeEntity)
    {
        Replace(DamageDealtRows, FilterRows(currentDamageDealtRows, includeEntity));
        Replace(DamageReceivedRows, FilterRows(currentDamageReceivedRows, includeEntity));
        Replace(HealingDoneRows, FilterRows(currentHealingDoneRows, includeEntity));
        Replace(HealingReceivedRows, FilterRows(currentHealingReceivedRows, includeEntity));
    }

    private static IEnumerable<CombatBreakdownRow> FilterRows(
        IReadOnlyList<CombatBreakdownRow> rows,
        Func<string, bool> includeEntity)
    {
        return rows.Where(x => includeEntity(x.PlayerEntityKey));
    }

    private void ApplySelectedEncounter(CombatEncounterSnapshot? encounter)
    {
        selectedEncounterSnapshot = encounter;
        var encounters = encounter is null
            ? currentSnapshot.Encounters
            : new[] { encounter };

        EncounterStatus = encounter is null
            ? encounters.Count == 0 ? "Idle" : "All encounters"
            : encounter.IsActive ? "Active" : "Ended";
        RefreshDisplayedSummary(DateTime.UtcNow);
        currentDamageDealtRows = AggregateBreakdownRows(encounters.SelectMany(x => x.DamageDealt));
        currentDamageReceivedRows = AggregateBreakdownRows(encounters.SelectMany(x => x.DamageReceived));
        currentHealingDoneRows = AggregateBreakdownRows(encounters.SelectMany(x => x.HealingDone));
        currentHealingReceivedRows = AggregateBreakdownRows(encounters.SelectMany(x => x.HealingReceived));

        ApplyCurrentFilters();
    }

    private void RefreshDisplayedSummary(DateTime nowUtc)
    {
        var encounters = selectedEncounterSnapshot is null
            ? currentSnapshot.Encounters
            : new[] { selectedEncounterSnapshot };
        var encounterArray = encounters.ToArray();
        var elapsed = SumElapsed(encounterArray, nowUtc);
        var totals = AggregateTotals(encounterArray, CreateEntityPredicate());

        ElapsedText = FormatDuration(elapsed);
        DamagePerSecondText = FormatRate(CalculateRate(totals.DamageDealt, elapsed));
        DamageReceivedPerSecondText = FormatRate(CalculateRate(totals.DamageReceived, elapsed));
        HealingPerSecondText = FormatRate(CalculateRate(totals.HealingDone, elapsed));
        HealingReceivedPerSecondText = FormatRate(CalculateRate(totals.HealingReceived, elapsed));
    }

    private static CombatEncounterListItemViewModel CreateEncounterItem(CombatEncounterSnapshot encounter)
    {
        var duration = encounter.Elapsed;
        return new CombatEncounterListItemViewModel(
            encounter.EncounterKey,
            $"#{encounter.EncounterNumber}",
            FormatUtcTime(encounter.StartedAtUtc),
            encounter.IsActive ? "Active" : "Ended",
            FormatDuration(duration),
            FormatAmount(encounter.TotalDamageDealt),
            FormatRate(CalculateRate(encounter.TotalDamageDealt, duration)),
            FormatAmount(encounter.TotalDamageReceived),
            FormatRate(CalculateRate(encounter.TotalDamageReceived, duration)),
            FormatAmount(encounter.TotalHealingDone),
            FormatRate(CalculateRate(encounter.TotalHealingDone, duration)),
            FormatAmount(encounter.TotalHealingReceived),
            FormatRate(CalculateRate(encounter.TotalHealingReceived, duration)),
            encounter.IsActive,
            encounter);
    }

    private void ApplyCurrentFilters()
    {
        var encounters = GetDisplayedEncounters().ToArray();
        var selectedKey = SelectedPlayer?.EntityKey;
        var includeEntity = CreateEntityPredicate(selectedKey);
        var duration = SumElapsed(encounters, DateTime.UtcNow);
        var playerSummaries = AggregatePlayers(encounters)
            .Where(x => includeEntity(x.EntityKey))
            .ToArray();
        var players = playerSummaries
            .Select(player => CreatePlayerRow(player, duration))
            .ToArray();

        if (!string.IsNullOrEmpty(selectedKey) && playerSummaries.All(x => x.EntityKey != selectedKey))
        {
            applyingPlayerSelection = true;
            try
            {
                SelectedPlayer = null;
            }
            finally
            {
                applyingPlayerSelection = false;
            }

            ApplyCurrentFilters();
            return;
        }

        applyingPlayerSelection = true;
        try
        {
            Replace(Players, players);
            if (!string.IsNullOrEmpty(selectedKey))
            {
                SelectedPlayer = Players.FirstOrDefault(x => x.EntityKey == selectedKey);
            }
        }
        finally
        {
            applyingPlayerSelection = false;
        }

        RefreshDisplayedSummary(DateTime.UtcNow);
        RefreshBreakdowns(includeEntity);
        RefreshChart(includeEntity);
    }

    private void RefreshChart()
    {
        RefreshChart(CreateEntityPredicate());
    }

    private void RefreshChart(Func<string, bool> includeEntity)
    {
        var encounters = GetDisplayedEncounters();
        var buckets = AggregateBuckets(encounters, SelectedAggregation.Seconds, includeEntity).ToArray();
        var signature = CreateChartRenderSignature(buckets);
        if (signature == lastChartRenderSignature)
        {
            return;
        }

        lastChartRenderSignature = signature;
        if (buckets.Length == 0)
        {
            ChartSeries = Array.Empty<ISeries>();
            ChartXAxes = CreateChartXAxes(buckets, SelectedAggregation.Seconds, SelectedChartWindow.Duration);
            ChartYAxes = CreateChartYAxes(0, SelectedChartMetric.Kind);
            return;
        }

        var maxValue = buckets
            .SelectMany(GetChartValues)
            .DefaultIfEmpty(0)
            .Max();

        ChartSeries = CreateChartSeries(buckets, SelectedChartMetric.Kind, SelectedAggregation.Seconds);
        ChartXAxes = CreateChartXAxes(buckets, SelectedAggregation.Seconds, SelectedChartWindow.Duration);
        ChartYAxes = CreateChartYAxes(maxValue, SelectedChartMetric.Kind);
    }

    private IEnumerable<AggregatedCombatBucket> AggregateBuckets(
        IEnumerable<CombatEncounterSnapshot> encounters,
        int aggregationSeconds,
        Func<string, bool> includeEntity)
    {
        var aggregationTicks = TimeSpan.FromSeconds(aggregationSeconds).Ticks;
        var groupedBuckets = encounters
            .SelectMany(encounter => encounter.TimeBuckets.Select(bucket =>
            {
                var playerTotals = bucket.PlayerTotals
                    .Where(player => includeEntity(player.EntityKey))
                    .ToArray();

                return new
                {
                    TimestampUtc = encounter.StartedAtUtc.Add(bucket.StartOffset),
                    DamageDealt = playerTotals.Sum(player => player.DamageDealt),
                    DamageReceived = playerTotals.Sum(player => player.DamageReceived),
                    HealingDone = playerTotals.Sum(player => player.HealingDone),
                    HealingReceived = playerTotals.Sum(player => player.HealingReceived)
                };
            }))
            .GroupBy(x => x.TimestampUtc.Ticks / aggregationTicks)
            .OrderBy(x => x.Key)
            .Select(x =>
            {
                return new AggregatedCombatBucket(
                    new DateTime(x.Key * aggregationTicks, DateTimeKind.Utc),
                    x.Sum(bucket => bucket.DamageDealt),
                    x.Sum(bucket => bucket.DamageReceived),
                    x.Sum(bucket => bucket.HealingDone),
                    x.Sum(bucket => bucket.HealingReceived));
            })
            .ToArray();

        if (groupedBuckets.Length == 0)
        {
            return groupedBuckets;
        }

        return FillMissingBuckets(groupedBuckets, aggregationSeconds);
    }

    private static IEnumerable<AggregatedCombatBucket> FillMissingBuckets(
        IReadOnlyList<AggregatedCombatBucket> buckets,
        int aggregationSeconds)
    {
        var bucketsByTicks = buckets.ToDictionary(x => x.StartedAtUtc.Ticks);
        var aggregationTicks = TimeSpan.FromSeconds(aggregationSeconds).Ticks;
        var firstTicks = buckets[0].StartedAtUtc.Ticks;
        var lastTicks = buckets[^1].StartedAtUtc.Ticks;

        for (var ticks = firstTicks; ticks <= lastTicks; ticks += aggregationTicks)
        {
            if (bucketsByTicks.TryGetValue(ticks, out var bucket))
            {
                yield return bucket;
                continue;
            }

            yield return new AggregatedCombatBucket(new DateTime(ticks, DateTimeKind.Utc), 0, 0, 0, 0);
        }
    }

    private IEnumerable<CombatEncounterSnapshot> GetDisplayedEncounters()
    {
        return selectedEncounterSnapshot is null
            ? currentSnapshot.Encounters
            : new[] { selectedEncounterSnapshot };
    }

    private Func<string, bool> CreateEntityPredicate(string? selectedEntityKey = null)
    {
        selectedEntityKey ??= SelectedPlayer?.EntityKey;
        if (!string.IsNullOrEmpty(selectedEntityKey))
        {
            return entityKey => entityKey == selectedEntityKey;
        }

        var friendEntityKeys = GetFriendEntityKeys();
        return SelectedSideFilter.Kind switch
        {
            CombatSideFilterKind.Friend => entityKey => friendEntityKeys.Contains(entityKey),
            CombatSideFilterKind.Foe => entityKey => !friendEntityKeys.Contains(entityKey),
            CombatSideFilterKind.Both => _ => true,
            _ => _ => true
        };
    }

    private HashSet<string> GetFriendEntityKeys()
    {
        return currentSnapshot.TrackedEntities
            .Where(x => x.IsLocalPlayer || x.IsPartyMember)
            .Select(x => x.EntityKey)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<CombatPlayerSummary> AggregatePlayers(IEnumerable<CombatEncounterSnapshot> encounters)
    {
        return encounters
            .SelectMany(x => x.Players)
            .GroupBy(x => x.EntityKey)
            .Select(x => new CombatPlayerSummary(
                x.Key,
                x.First().Name,
                x.Sum(player => player.DamageDealt),
                x.Sum(player => player.DamageReceived),
                x.Sum(player => player.HealingDone),
                x.Sum(player => player.HealingReceived)))
            .Where(x => x.DamageDealt > 0 || x.DamageReceived > 0 || x.HealingDone > 0 || x.HealingReceived > 0)
            .OrderByDescending(x => x.DamageDealt)
            .ThenByDescending(x => x.HealingDone)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CombatPlayerRowViewModel CreatePlayerRow(CombatPlayerSummary player, TimeSpan duration)
    {
        return new CombatPlayerRowViewModel(
            player.EntityKey,
            player.Name,
            FormatAmount(player.DamageDealt),
            FormatRate(CalculateRate(player.DamageDealt, duration)),
            FormatAmount(player.DamageReceived),
            FormatRate(CalculateRate(player.DamageReceived, duration)),
            FormatAmount(player.HealingDone),
            FormatRate(CalculateRate(player.HealingDone, duration)),
            FormatAmount(player.HealingReceived),
            FormatRate(CalculateRate(player.HealingReceived, duration)));
    }

    private static CombatMetricTotals AggregateTotals(
        IEnumerable<CombatEncounterSnapshot> encounters,
        Func<string, bool> includeEntity)
    {
        var totals = new CombatMetricTotals();
        foreach (var player in AggregatePlayers(encounters).Where(x => includeEntity(x.EntityKey)))
        {
            totals.DamageDealt += player.DamageDealt;
            totals.DamageReceived += player.DamageReceived;
            totals.HealingDone += player.HealingDone;
            totals.HealingReceived += player.HealingReceived;
        }

        return totals;
    }

    private static IReadOnlyList<CombatBreakdownRow> AggregateBreakdownRows(IEnumerable<CombatBreakdownRow> rows)
    {
        return rows
            .GroupBy(x => new
            {
                x.PlayerEntityKey,
                x.PlayerName,
                x.OtherEntityKey,
                x.OtherName,
                x.SpellKey,
                x.SpellLabel
            })
            .Select(x => new CombatBreakdownRow(
                x.Key.PlayerEntityKey,
                x.Key.PlayerName,
                x.Key.OtherEntityKey,
                x.Key.OtherName,
                x.Key.SpellKey,
                x.Key.SpellLabel,
                x.Sum(row => row.Amount)))
            .Where(x => x.Amount > 0)
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => x.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.OtherName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TimeSpan SumElapsed(IEnumerable<CombatEncounterSnapshot> encounters, DateTime nowUtc)
    {
        return TimeSpan.FromTicks(encounters.Sum(x => GetElapsed(x, nowUtc).Ticks));
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

    private IEnumerable<double> GetChartValues(AggregatedCombatBucket bucket)
    {
        yield return GetChartValue(bucket.DamageDealt, SelectedChartMetric.Kind, SelectedAggregation.Seconds);
        yield return GetChartValue(bucket.DamageReceived, SelectedChartMetric.Kind, SelectedAggregation.Seconds);
        yield return GetChartValue(bucket.HealingDone, SelectedChartMetric.Kind, SelectedAggregation.Seconds);
        yield return GetChartValue(bucket.HealingReceived, SelectedChartMetric.Kind, SelectedAggregation.Seconds);
    }

    private string CreateChartRenderSignature(IReadOnlyList<AggregatedCombatBucket> buckets)
    {
        if (buckets.Count == 0)
        {
            return "empty";
        }

        var builder = new StringBuilder();
        builder.Append(selectedEncounterSnapshot?.EncounterKey ?? "all")
            .Append('|')
            .Append(SelectedPlayer?.EntityKey ?? "all")
            .Append('|')
            .Append(SelectedSideFilter.Kind)
            .Append('|')
            .Append(SelectedAggregation.Seconds)
            .Append('|')
            .Append(SelectedChartMetric.Kind)
            .Append('|')
            .Append(SelectedChartWindow.Duration?.Ticks)
            .Append('|')
            .Append(buckets.Count);

        foreach (var bucket in buckets)
        {
            builder.Append('|')
                .Append(bucket.StartedAtUtc.Ticks)
                .Append(':')
                .Append(bucket.DamageDealt)
                .Append(':')
                .Append(bucket.DamageReceived)
                .Append(':')
                .Append(bucket.HealingDone)
                .Append(':')
                .Append(bucket.HealingReceived);
        }

        return builder.ToString();
    }

    private static double GetChartValue(long amount, CombatChartMetricKind metricKind, int aggregationSeconds)
    {
        return metricKind == CombatChartMetricKind.PerSecond
            ? CalculateRate(amount, TimeSpan.FromSeconds(aggregationSeconds))
            : amount;
    }

    private static ISeries[] CreateChartSeries(
        IReadOnlyList<AggregatedCombatBucket> buckets,
        CombatChartMetricKind metricKind,
        int aggregationSeconds)
    {
        var suffix = metricKind == CombatChartMetricKind.PerSecond ? "/s" : string.Empty;
        return new ISeries[]
        {
            CreateLineSeries($"Damage dealt{suffix}", buckets, x => GetChartValue(x.DamageDealt, metricKind, aggregationSeconds), metricKind, SKColors.DeepSkyBlue),
            CreateLineSeries($"Damage received{suffix}", buckets, x => GetChartValue(x.DamageReceived, metricKind, aggregationSeconds), metricKind, SKColors.LightCoral),
            CreateLineSeries($"Healing done{suffix}", buckets, x => GetChartValue(x.HealingDone, metricKind, aggregationSeconds), metricKind, SKColors.LightGreen),
            CreateLineSeries($"Healing received{suffix}", buckets, x => GetChartValue(x.HealingReceived, metricKind, aggregationSeconds), metricKind, SKColors.MediumTurquoise)
        };
    }

    private static LineSeries<DateTimePoint> CreateLineSeries(
        string name,
        IReadOnlyList<AggregatedCombatBucket> buckets,
        Func<AggregatedCombatBucket, double> selector,
        CombatChartMetricKind metricKind,
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
                FormatChartValue(chartPoint.Coordinate.PrimaryValue, metricKind),
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
        TimeSpan? chartWindow)
    {
        double? minLimit = null;
        double? maxLimit = null;
        if (chartWindow is { } window && buckets.Count > 0)
        {
            var firstTicks = buckets[0].StartedAtUtc.Ticks;
            var latestTicks = buckets[^1].StartedAtUtc.Ticks;
            minLimit = Math.Max(firstTicks, latestTicks - window.Ticks);
            maxLimit = latestTicks;
        }

        return new[]
        {
            new Axis
            {
                Name = "Time",
                Labeler = FormatUtcAxisTick,
                MinStep = TimeSpan.FromSeconds(aggregationSeconds).Ticks,
                MinLimit = minLimit,
                MaxLimit = maxLimit,
                LabelsRotation = 0
            }
        };
    }

    private static Axis[] CreateChartYAxes(double maxValue, CombatChartMetricKind metricKind)
    {
        return new[]
        {
            new Axis
            {
                Name = metricKind == CombatChartMetricKind.PerSecond ? "Per second" : "Amount",
                Labeler = value => FormatChartValue(value, metricKind),
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

    private static string FormatChartValue(double value, CombatChartMetricKind metricKind)
    {
        return metricKind == CombatChartMetricKind.PerSecond
            ? FormatRate(value)
            : value.ToString("N0");
    }

    private static double CalculateRate(long amount, TimeSpan duration)
    {
        return duration.TotalSeconds > 0
            ? amount / duration.TotalSeconds
            : 0;
    }

    private static string FormatEntity(CombatEntitySnapshot entity)
    {
        return entity.Name;
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
        var options = new[]
        {
            new CombatAggregationOptionViewModel(1),
            new CombatAggregationOptionViewModel(3),
            new CombatAggregationOptionViewModel(5),
            new CombatAggregationOptionViewModel(10),
            new CombatAggregationOptionViewModel(30),
            new CombatAggregationOptionViewModel(60)
        };

        Replace(AggregationOptions, options);
        return options[0];
    }

    private CombatSideFilterViewModel InitializeSideFilterOptions()
    {
        var options = new[]
        {
            new CombatSideFilterViewModel(CombatSideFilterKind.Friend, "Friend"),
            new CombatSideFilterViewModel(CombatSideFilterKind.Foe, "Foe"),
            new CombatSideFilterViewModel(CombatSideFilterKind.Both, "Both")
        };

        Replace(SideFilterOptions, options);
        return options[0];
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
            new CombatChartWindowOptionViewModel(null, "Unlimited")
        };

        Replace(ChartWindowOptions, options);
        return options[^1];
    }

    private CombatChartMetricOptionViewModel InitializeChartMetricOptions()
    {
        var options = new[]
        {
            new CombatChartMetricOptionViewModel(CombatChartMetricKind.Total, "Total"),
            new CombatChartMetricOptionViewModel(CombatChartMetricKind.PerSecond, "Per second")
        };

        Replace(ChartMetricOptions, options);
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

        if (activeEncounterRefreshTimer is not null)
        {
            activeEncounterRefreshTimer.Tick -= OnActiveEncounterRefreshTimerTick;
            activeEncounterRefreshTimer.Stop();
            activeEncounterRefreshTimer = null;
        }
    }
}

public sealed record CombatAggregationOptionViewModel(int Seconds)
{
    public string Label => $"{Seconds}s";
}

public enum CombatSideFilterKind
{
    Friend,
    Foe,
    Both
}

public sealed record CombatSideFilterViewModel(CombatSideFilterKind Kind, string Label);

public sealed record CombatChartWindowOptionViewModel(TimeSpan? Duration, string Label);

public enum CombatChartMetricKind
{
    Total,
    PerSecond
}

public sealed record CombatChartMetricOptionViewModel(CombatChartMetricKind Kind, string Label);

public sealed record AggregatedCombatBucket(
    DateTime StartedAtUtc,
    long DamageDealt,
    long DamageReceived,
    long HealingDone,
    long HealingReceived);

public sealed record CombatEntityViewModel(
    string Name,
    string Role);

public sealed record CombatPlayerRowViewModel(
    string EntityKey,
    string Name,
    string DamageDealt,
    string DamageDealtPerSecond,
    string DamageReceived,
    string DamageReceivedPerSecond,
    string HealingDone,
    string HealingDonePerSecond,
    string HealingReceived,
    string HealingReceivedPerSecond);

public sealed record CombatEncounterListItemViewModel(
    string EncounterKey,
    string Label,
    string StartedText,
    string Status,
    string Duration,
    string DamageDealt,
    string DamageDealtPerSecond,
    string DamageReceived,
    string DamageReceivedPerSecond,
    string HealingDone,
    string HealingDonePerSecond,
    string HealingReceived,
    string HealingReceivedPerSecond,
    bool IsActive,
    CombatEncounterSnapshot Encounter);

public sealed class CombatMetricTotals
{
    public long DamageDealt { get; set; }
    public long DamageReceived { get; set; }
    public long HealingDone { get; set; }
    public long HealingReceived { get; set; }
}

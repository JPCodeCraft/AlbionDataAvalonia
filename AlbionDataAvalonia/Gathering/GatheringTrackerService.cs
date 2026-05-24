using AlbionDataAvalonia.Gathering.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace AlbionDataAvalonia.Gathering;

public sealed class GatheringTrackerService : IDisposable
{
    private static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FishingFinalizationGracePeriod = TimeSpan.FromMilliseconds(750);

    private readonly object sync = new();
    private readonly SettingsManager settingsManager;
    private readonly PlayerState playerState;
    private readonly ItemsIdsService itemsIdsService;
    private readonly ItemEstimatedMarketValueService estimatedMarketValues;
    private readonly Dictionary<GatheringItemKey, GatheringItemAggregate> itemAggregates = new();
    private readonly Dictionary<DateTime, GatheringMinuteBucket> minuteBuckets = new();
    private readonly List<PauseInterval> pauseIntervals = new();
    private readonly HashSet<MissingEstimatedMarketValueWarning> missingEstimatedMarketValueWarnings = new();

    private DateTime sessionStartedAtUtc = DateTime.UtcNow;
    private FishingAttempt? activeFishingAttempt;
    private Timer? fishingFinalizeTimer;
    private int fishingFinalizeVersion;
    private bool isDisabled;
    private bool isPaused;

    public event Action<GatheringTrackerSnapshot>? SnapshotChanged;

    public GatheringTrackerService(
        SettingsManager settingsManager,
        PlayerState playerState,
        ItemsIdsService itemsIdsService,
        ItemEstimatedMarketValueService estimatedMarketValues)
    {
        this.settingsManager = settingsManager;
        this.playerState = playerState;
        this.itemsIdsService = itemsIdsService;
        this.estimatedMarketValues = estimatedMarketValues;
        isDisabled = settingsManager.UserSettings.DisableGatheringTracker;
        this.settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        this.estimatedMarketValues.EstimatedMarketValueChanged += OnEstimatedMarketValueChanged;

        if (isDisabled)
        {
            ResetSessionCore(DateTime.UtcNow);
        }
    }

    public GatheringTrackerSnapshot CurrentSnapshot
    {
        get
        {
            lock (sync)
            {
                return BuildSnapshot(DateTime.UtcNow);
            }
        }
    }

    public void RecordHarvest(
        long userObjectId,
        int itemId,
        int standardAmount,
        int gatheringBonusAmount,
        int premiumBonusAmount,
        DateTime receivedAtUtc)
    {
        if (playerState.UserObjectId <= 0 || userObjectId <= 0 || userObjectId != playerState.UserObjectId)
        {
            return;
        }

        var amount = standardAmount + gatheringBonusAmount + premiumBonusAmount;
        if (itemId <= 0 || amount <= 0)
        {
            return;
        }

        GatheringTrackerSnapshot snapshot;
        lock (sync)
        {
            if (isDisabled || isPaused)
            {
                return;
            }

            RecordAggregatedItem(itemId, 1, amount, receivedAtUtc, GatheringSource.Harvest);
            snapshot = BuildSnapshot(receivedAtUtc);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void StartFishing(long eventId, long usedRodObjectId, DateTime receivedAtUtc)
    {
        GatheringTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (isDisabled || isPaused)
            {
                return;
            }

            CancelScheduledFishingFinalizationCore();
            if (activeFishingAttempt is { IsClosedForEvents: true })
            {
                snapshot = FinalizeFishingCore(receivedAtUtc);
            }

            activeFishingAttempt = new FishingAttempt(eventId, usedRodObjectId, receivedAtUtc);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void DiscoverFishingItem(NewItem item)
    {
        lock (sync)
        {
            if (isDisabled
                || isPaused
                || activeFishingAttempt is not { IsClosedForEvents: false } attempt
                || item.ObjectId is null
                || item.ObjectId == attempt.UsedRodObjectId)
            {
                return;
            }

            attempt.DiscoveredItems.Add(new FishingDiscoveredItem(
                item.ObjectId.Value,
                item.ItemIndex,
                item.Quality <= 0 ? 1 : item.Quality,
                item.EstimatedMarketValue));
        }
    }

    public void ConfirmFishingReward(int itemId, int quantity)
    {
        lock (sync)
        {
            if (isDisabled
                || isPaused
                || activeFishingAttempt is not { } attempt
                || itemId <= 0
                || quantity <= 0)
            {
                Log.Debug(
                    "Ignored fishing reward. ActiveAttempt={HasActiveAttempt} ItemId={ItemId} Quantity={Quantity}",
                    activeFishingAttempt is not null,
                    itemId,
                    quantity);
                return;
            }

            var discovered = attempt.DiscoveredItems.FirstOrDefault(x =>
                x.ItemId == itemId
                && !attempt.ConfirmedRewards.Any(y => y.DiscoveredObjectId == x.ObjectId));
            if (discovered is null)
            {
                Log.Debug(
                    "Ignored fishing reward because no matching discovered item exists. EventId={EventId} ItemId={ItemId} Quantity={Quantity} DiscoveredCount={DiscoveredCount}",
                    attempt.EventId,
                    itemId,
                    quantity,
                    attempt.DiscoveredItems.Count);
                return;
            }

            attempt.ConfirmedRewards.Add(new FishingConfirmedReward(
                discovered.ObjectId,
                discovered.ItemId,
                discovered.Quality,
                quantity));

            if (discovered.EstimatedMarketValue > 0)
            {
                estimatedMarketValues.Update(discovered.ItemId, discovered.Quality, discovered.EstimatedMarketValue);
            }
        }
    }

    public void MarkFishingSucceeded(bool succeeded)
    {
        lock (sync)
        {
            if (isDisabled || activeFishingAttempt is null)
            {
                return;
            }

            activeFishingAttempt.IsSucceeded = succeeded;
            if (!succeeded)
            {
                CancelScheduledFishingFinalizationCore();
                activeFishingAttempt = null;
            }
        }
    }

    public void ScheduleFishingFinalization(DateTime receivedAtUtc)
    {
        lock (sync)
        {
            if (isDisabled || activeFishingAttempt is null)
            {
                return;
            }

            activeFishingAttempt.IsClosedForEvents = true;
            ScheduleFishingFinalizationCore();
        }
    }

    public void SetPaused(bool paused)
    {
        GatheringTrackerSnapshot snapshot;
        lock (sync)
        {
            if (isDisabled)
            {
                isPaused = false;
                return;
            }

            if (isPaused == paused)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            isPaused = paused;
            if (paused)
            {
                CancelScheduledFishingFinalizationCore();
                activeFishingAttempt = null;
                pauseIntervals.Add(new PauseInterval(nowUtc));
            }
            else if (pauseIntervals.Count > 0 && pauseIntervals[^1].EndedAtUtc is null)
            {
                pauseIntervals[^1].EndedAtUtc = nowUtc;
            }

            snapshot = BuildSnapshot(nowUtc);
        }

        SnapshotChanged?.Invoke(snapshot);
        Log.Information(paused ? "Gathering tracker paused." : "Gathering tracker resumed.");
    }

    public void Reset()
    {
        GatheringTrackerSnapshot snapshot;
        lock (sync)
        {
            ResetSessionCore(DateTime.UtcNow);
            snapshot = BuildSnapshot(sessionStartedAtUtc);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void Dispose()
    {
        settingsManager.UserSettings.PropertyChanged -= OnUserSettingsPropertyChanged;
        estimatedMarketValues.EstimatedMarketValueChanged -= OnEstimatedMarketValueChanged;
        lock (sync)
        {
            CancelScheduledFishingFinalizationCore();
        }
    }

    private void OnEstimatedMarketValueChanged(GatheringItemKey key)
    {
        GatheringTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (isDisabled)
            {
                return;
            }

            if (itemAggregates.ContainsKey(key))
            {
                snapshot = BuildSnapshot(DateTime.UtcNow);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.DisableGatheringTracker))
        {
            return;
        }

        GatheringTrackerSnapshot snapshot;
        lock (sync)
        {
            var disableGatheringTracker = settingsManager.UserSettings.DisableGatheringTracker;
            if (isDisabled == disableGatheringTracker)
            {
                return;
            }

            isDisabled = disableGatheringTracker;
            if (isDisabled)
            {
                ResetSessionCore(DateTime.UtcNow);
                Log.Information("Gathering tracker disabled; gathering session data was reset.");
            }
            else
            {
                sessionStartedAtUtc = DateTime.UtcNow;
                Log.Information("Gathering tracker enabled.");
            }

            snapshot = BuildSnapshot(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    private void ResetSessionCore(DateTime nowUtc)
    {
        itemAggregates.Clear();
        minuteBuckets.Clear();
        pauseIntervals.Clear();
        missingEstimatedMarketValueWarnings.Clear();
        CancelScheduledFishingFinalizationCore();
        activeFishingAttempt = null;
        isPaused = false;
        sessionStartedAtUtc = nowUtc;
    }

    private void RecordAggregatedItem(
        int itemId,
        int quality,
        long amount,
        DateTime occurredAtUtc,
        GatheringSource source)
    {
        if (itemId <= 0 || amount <= 0)
        {
            return;
        }

        quality = quality <= 0 ? 1 : quality;
        var key = new GatheringItemKey(itemId, quality);
        if (!itemAggregates.TryGetValue(key, out var itemAggregate))
        {
            var itemData = itemsIdsService.GetItemById(itemId);
            itemAggregate = new GatheringItemAggregate(
                itemId,
                quality,
                itemData.UniqueName,
                itemData.UsName);
            itemAggregates[key] = itemAggregate;
        }

        if (estimatedMarketValues.Get(itemId, quality) is null
            && missingEstimatedMarketValueWarnings.Add(new MissingEstimatedMarketValueWarning(key, source)))
        {
            Log.Warning(
                "Missing estimated market value for gathered or fished item. Source={Source} ItemId={ItemId} Quality={Quality} ItemUniqueName={ItemUniqueName} ItemName={ItemName}",
                source,
                itemId,
                quality,
                itemAggregate.ItemUniqueName,
                itemAggregate.ItemName);
        }

        itemAggregate.Amount += amount;

        var bucketStartedAtUtc = GetBucketStart(occurredAtUtc);
        if (!minuteBuckets.TryGetValue(bucketStartedAtUtc, out var bucket))
        {
            bucket = new GatheringMinuteBucket(bucketStartedAtUtc);
            minuteBuckets[bucketStartedAtUtc] = bucket;
        }

        bucket.Amount += amount;
        bucket.ItemAmounts.TryGetValue(key, out var bucketItemAmount);
        bucket.ItemAmounts[key] = bucketItemAmount + amount;
    }

    private GatheringTrackerSnapshot BuildSnapshot(DateTime nowUtc)
    {
        var activeElapsed = GetActiveElapsed(nowUtc);
        var activeMinutes = Math.Max(activeElapsed.TotalMinutes, 1d / 60d);

        var summaryRows = itemAggregates
            .Values
            .Select(item =>
            {
                var emv = estimatedMarketValues.Get(item.ItemId, item.Quality);
                return new GatheringSummaryRow(
                    item.ItemId,
                    item.Quality,
                    item.ItemUniqueName,
                    item.ItemName,
                    item.Amount,
                    emv,
                    emv is null ? null : emv.Value * item.Amount,
                    item.Amount / activeMinutes,
                    item.Amount / activeMinutes * 60d);
            })
            .OrderByDescending(x => x.TotalEstimatedMarketValue ?? 0)
            .ThenByDescending(x => x.Amount)
            .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var bucketRows = minuteBuckets
            .Values
            .Select(bucket =>
            {
                long totalEstimatedMarketValue = 0;
                var hasEstimatedMarketValue = false;

                foreach (var (itemKey, amount) in bucket.ItemAmounts)
                {
                    var emv = estimatedMarketValues.Get(itemKey.ItemId, itemKey.Quality);
                    if (emv is null)
                    {
                        continue;
                    }

                    hasEstimatedMarketValue = true;
                    totalEstimatedMarketValue += emv.Value * amount;
                }

                long? bucketEstimatedMarketValue = hasEstimatedMarketValue ? totalEstimatedMarketValue : null;
                return new GatheringBucketRow(
                    bucket.BucketStartedAtUtc,
                    bucket.Amount,
                    bucketEstimatedMarketValue,
                    bucketEstimatedMarketValue is null ? null : bucketEstimatedMarketValue.Value * 60);
            })
            .OrderByDescending(x => x.BucketStartedAtUtc)
            .ToArray();

        return new GatheringTrackerSnapshot(
            isPaused,
            sessionStartedAtUtc,
            activeElapsed,
            summaryRows.Sum(x => x.Amount),
            summaryRows.Sum(x => x.TotalEstimatedMarketValue ?? 0),
            summaryRows,
            bucketRows);
    }

    private TimeSpan GetActiveElapsed(DateTime nowUtc)
    {
        var elapsed = nowUtc - sessionStartedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        foreach (var interval in pauseIntervals)
        {
            var pauseEnd = interval.EndedAtUtc ?? nowUtc;
            if (pauseEnd <= interval.StartedAtUtc)
            {
                continue;
            }

            elapsed -= pauseEnd - interval.StartedAtUtc;
        }

        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private static DateTime GetBucketStart(DateTime utc)
    {
        var ticks = utc.Ticks - utc.Ticks % BucketSize.Ticks;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private void ScheduleFishingFinalizationCore()
    {
        var version = ++fishingFinalizeVersion;
        fishingFinalizeTimer?.Dispose();
        fishingFinalizeTimer = new Timer(
            OnFishingFinalizeTimerElapsed,
            version,
            FishingFinalizationGracePeriod,
            Timeout.InfiniteTimeSpan);
    }

    private void CancelScheduledFishingFinalizationCore()
    {
        fishingFinalizeVersion++;
        fishingFinalizeTimer?.Dispose();
        fishingFinalizeTimer = null;
    }

    private void OnFishingFinalizeTimerElapsed(object? state)
    {
        var version = state is int value ? value : 0;
        GatheringTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (version != fishingFinalizeVersion)
            {
                return;
            }

            fishingFinalizeTimer?.Dispose();
            fishingFinalizeTimer = null;
            snapshot = FinalizeFishingCore(DateTime.UtcNow);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private GatheringTrackerSnapshot? FinalizeFishingCore(DateTime receivedAtUtc)
    {
        if (activeFishingAttempt is null)
        {
            return null;
        }

        var attempt = activeFishingAttempt;
        activeFishingAttempt = null;

        if (isPaused || !attempt.IsSucceeded)
        {
            return null;
        }

        foreach (var reward in attempt.ConfirmedRewards)
        {
            RecordAggregatedItem(
                reward.ItemId,
                reward.Quality,
                reward.Quantity,
                attempt.StartedAtUtc,
                GatheringSource.Fishing);
        }

        if (attempt.ConfirmedRewards.Count == 0)
        {
            Log.Debug(
                "Fishing attempt finalized with no confirmed rewards. EventId={EventId} DiscoveredCount={DiscoveredCount}",
                attempt.EventId,
                attempt.DiscoveredItems.Count);
            return null;
        }

        return BuildSnapshot(receivedAtUtc);
    }

    private sealed class GatheringItemAggregate
    {
        public GatheringItemAggregate(
            int itemId,
            int quality,
            string itemUniqueName,
            string itemName)
        {
            ItemId = itemId;
            Quality = quality;
            ItemUniqueName = itemUniqueName;
            ItemName = itemName;
        }

        public int ItemId { get; }
        public int Quality { get; }
        public string ItemUniqueName { get; }
        public string ItemName { get; }
        public long Amount { get; set; }
    }

    private sealed class GatheringMinuteBucket
    {
        public GatheringMinuteBucket(DateTime bucketStartedAtUtc)
        {
            BucketStartedAtUtc = bucketStartedAtUtc;
        }

        public DateTime BucketStartedAtUtc { get; }
        public long Amount { get; set; }
        public Dictionary<GatheringItemKey, long> ItemAmounts { get; } = new();
    }

    private sealed class FishingAttempt
    {
        public FishingAttempt(long eventId, long usedRodObjectId, DateTime startedAtUtc)
        {
            EventId = eventId;
            UsedRodObjectId = usedRodObjectId;
            StartedAtUtc = startedAtUtc;
        }

        public long EventId { get; }
        public long UsedRodObjectId { get; }
        public DateTime StartedAtUtc { get; }
        public bool IsSucceeded { get; set; }
        public bool IsClosedForEvents { get; set; }
        public List<FishingDiscoveredItem> DiscoveredItems { get; } = new();
        public List<FishingConfirmedReward> ConfirmedRewards { get; } = new();
    }

    private sealed record FishingDiscoveredItem(
        long ObjectId,
        int ItemId,
        int Quality,
        long EstimatedMarketValue);

    private sealed record FishingConfirmedReward(
        long DiscoveredObjectId,
        int ItemId,
        int Quality,
        int Quantity);

    private enum GatheringSource
    {
        Harvest,
        Fishing
    }

    private readonly record struct MissingEstimatedMarketValueWarning(
        GatheringItemKey ItemKey,
        GatheringSource Source);

    private sealed class PauseInterval
    {
        public PauseInterval(DateTime startedAtUtc)
        {
            StartedAtUtc = startedAtUtc;
        }

        public DateTime StartedAtUtc { get; }
        public DateTime? EndedAtUtc { get; set; }
    }
}

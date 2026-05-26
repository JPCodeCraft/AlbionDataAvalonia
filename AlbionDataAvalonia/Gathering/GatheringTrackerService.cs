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
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Gathering;

public sealed class GatheringTrackerService : IDisposable
{
    private static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FishingFinalizationGracePeriod = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(10);

    private readonly object sync = new();
    private readonly SettingsManager settingsManager;
    private readonly PlayerState playerState;
    private readonly ItemsIdsService itemsIdsService;
    private readonly ItemEstimatedMarketValueService estimatedMarketValues;
    private readonly GatheringSessionPersistenceService sessionPersistence;
    private readonly Dictionary<GatheringItemKey, GatheringItemAggregate> itemAggregates = new();
    private readonly Dictionary<DateTime, GatheringMinuteBucket> minuteBuckets = new();
    private readonly List<PauseInterval> pauseIntervals = new();
    private readonly HashSet<MissingEstimatedMarketValueWarning> missingEstimatedMarketValueWarnings = new();

    private Guid? activeSessionId;
    private DateTime sessionStartedAtUtc = DateTime.UtcNow;
    private DateTime? lastActivityAtUtc;
    private GatheringSessionSource sessionSource = GatheringSessionSource.Unknown;
    private FishingAttempt? activeFishingAttempt;
    private Timer? fishingFinalizeTimer;
    private Timer? inactivityTimer;
    private int fishingFinalizeVersion;
    private int inactivityTimerVersion;
    private bool isDisabled;
    private bool isPaused;
    private bool isClosingSession;

    public event Action<GatheringTrackerSnapshot>? SnapshotChanged;

    public GatheringTrackerService(
        SettingsManager settingsManager,
        PlayerState playerState,
        ItemsIdsService itemsIdsService,
        ItemEstimatedMarketValueService estimatedMarketValues,
        GatheringSessionPersistenceService sessionPersistence)
    {
        this.settingsManager = settingsManager;
        this.playerState = playerState;
        this.itemsIdsService = itemsIdsService;
        this.estimatedMarketValues = estimatedMarketValues;
        this.sessionPersistence = sessionPersistence;
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

    public async Task InitializeSessionRecoveryAsync()
    {
        if (isDisabled)
        {
            await sessionPersistence.DeleteUnfinishedCheckpointAsync();
            return;
        }

        var checkpoint = await sessionPersistence.LoadUnfinishedCheckpointAsync();
        if (checkpoint is null)
        {
            return;
        }

        if (!IsValidCheckpoint(checkpoint))
        {
            Log.Warning("Discarding invalid unfinished gathering session checkpoint. SessionId={SessionId}", checkpoint.SessionId);
            await sessionPersistence.DeleteUnfinishedCheckpointAsync(checkpoint.SessionId);
            return;
        }

        Log.Information(
            "Closing unfinished gathering session on startup. SessionId={SessionId}",
            checkpoint.SessionId);
        var completed = sessionPersistence.BuildCompletedSnapshotFromCheckpoint(
            checkpoint,
            checkpoint.LastActivityAtUtc);
        if (await sessionPersistence.SaveCompletedSessionAsync(completed))
        {
            await sessionPersistence.DeleteUnfinishedCheckpointAsync(checkpoint.SessionId);
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
        GatheringSessionCheckpoint? checkpoint;
        lock (sync)
        {
            if (isDisabled || isPaused || isClosingSession)
            {
                return;
            }

            EnsureSessionStartedCore(receivedAtUtc, GatheringSessionSource.Gathering);
            RecordAggregatedItem(itemId, 1, amount, receivedAtUtc, GatheringSessionSource.Gathering, null);
            lastActivityAtUtc = receivedAtUtc;
            sessionSource = CombineSources(sessionSource, GatheringSessionSource.Gathering);
            ScheduleInactivityAutoCloseCore(receivedAtUtc);
            snapshot = BuildSnapshot(receivedAtUtc);
            checkpoint = BuildCheckpoint(receivedAtUtc);
        }

        SnapshotChanged?.Invoke(snapshot);
        SaveCheckpointInBackground(checkpoint);
    }

    public void StartFishing(long eventId, long usedRodObjectId, DateTime receivedAtUtc)
    {
        GatheringTrackerSnapshot? snapshot = null;
        GatheringSessionCheckpoint? checkpoint = null;
        lock (sync)
        {
            if (isDisabled || isPaused || isClosingSession)
            {
                return;
            }

            CancelScheduledFishingFinalizationCore();
            if (activeFishingAttempt is { IsClosedForEvents: true })
            {
                snapshot = FinalizeFishingCore(receivedAtUtc, out checkpoint);
            }

            activeFishingAttempt = new FishingAttempt(eventId, usedRodObjectId, receivedAtUtc);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
            SaveCheckpointInBackground(checkpoint);
        }
    }

    public void DiscoverFishingItem(NewItem item)
    {
        lock (sync)
        {
            if (isDisabled
                || isPaused
                || isClosingSession
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
                item.EstimatedMarketValue > 0 ? item.EstimatedMarketValue : null));
        }
    }

    public void ConfirmFishingReward(int itemId, int quantity)
    {
        lock (sync)
        {
            if (isDisabled
                || isPaused
                || isClosingSession
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
                quantity,
                discovered.EstimatedMarketValue));

            if (discovered.EstimatedMarketValue is > 0)
            {
                estimatedMarketValues.Update(discovered.ItemId, discovered.Quality, discovered.EstimatedMarketValue.Value);
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
        GatheringSessionCheckpoint? checkpoint = null;
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
            checkpoint = BuildCheckpoint(nowUtc);
        }

        SnapshotChanged?.Invoke(snapshot);
        SaveCheckpointInBackground(checkpoint);
        Log.Information(paused ? "Gathering tracker paused." : "Gathering tracker resumed.");
    }

    public void Reset()
    {
        _ = DiscardCurrentSessionAsync();
    }

    public async Task CloseAndSaveCurrentSessionAsync()
    {
        await CloseCurrentSessionAsync("manually", DateTime.UtcNow);
    }

    public async Task DiscardCurrentSessionAsync()
    {
        Guid? sessionId;
        GatheringTrackerSnapshot snapshot;
        lock (sync)
        {
            sessionId = activeSessionId;
            ResetSessionCore(DateTime.UtcNow);
            snapshot = BuildSnapshot(sessionStartedAtUtc);
        }

        await sessionPersistence.DeleteUnfinishedCheckpointAsync(sessionId);
        SnapshotChanged?.Invoke(snapshot);
        Log.Information("Gathering session discarded. SessionId={SessionId}", sessionId);
    }

    public void Dispose()
    {
        settingsManager.UserSettings.PropertyChanged -= OnUserSettingsPropertyChanged;
        estimatedMarketValues.EstimatedMarketValueChanged -= OnEstimatedMarketValueChanged;
        lock (sync)
        {
            CancelScheduledFishingFinalizationCore();
            CancelInactivityTimerCore();
        }
    }

    private void OnEstimatedMarketValueChanged(GatheringItemKey key)
    {
        GatheringTrackerSnapshot? snapshot = null;
        GatheringSessionCheckpoint? checkpoint = null;
        lock (sync)
        {
            if (isDisabled || !itemAggregates.TryGetValue(key, out var itemAggregate))
            {
                return;
            }

            itemAggregate.EstimatedMarketValue = estimatedMarketValues.Get(key.ItemId, key.Quality);
            snapshot = BuildSnapshot(DateTime.UtcNow);
            checkpoint = BuildCheckpoint(DateTime.UtcNow);
        }

        SnapshotChanged?.Invoke(snapshot);
        SaveCheckpointInBackground(checkpoint);
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.DisableGatheringTracker))
        {
            return;
        }

        GatheringTrackerSnapshot snapshot;
        Guid? sessionId = null;
        var deleteCheckpoint = false;
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
                sessionId = activeSessionId;
                deleteCheckpoint = true;
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

        if (deleteCheckpoint)
        {
            _ = sessionPersistence.DeleteUnfinishedCheckpointAsync(sessionId);
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    private async Task CloseCurrentSessionAsync(string reason, DateTime endedAtUtc)
    {
        GatheringCompletedSessionSnapshot? completed;
        Guid? sessionId;
        lock (sync)
        {
            if (isClosingSession || activeSessionId is null || lastActivityAtUtc is null || itemAggregates.Count == 0)
            {
                return;
            }

            isClosingSession = true;
            CancelInactivityTimerCore();
            completed = BuildCompletedSnapshot(endedAtUtc);
            sessionId = activeSessionId;
        }

        Log.Information("Gathering session {Reason} closed. SessionId={SessionId}", reason, sessionId);
        var saved = await sessionPersistence.SaveCompletedSessionAsync(completed);
        if (!saved)
        {
            lock (sync)
            {
                isClosingSession = false;
                ScheduleInactivityAutoCloseCore(DateTime.UtcNow);
            }

            return;
        }

        await sessionPersistence.DeleteUnfinishedCheckpointAsync(sessionId);

        GatheringTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (activeSessionId == sessionId)
            {
                ResetSessionCore(DateTime.UtcNow);
                snapshot = BuildSnapshot(sessionStartedAtUtc);
            }

            isClosingSession = false;
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private void ResetSessionCore(DateTime nowUtc)
    {
        itemAggregates.Clear();
        minuteBuckets.Clear();
        pauseIntervals.Clear();
        missingEstimatedMarketValueWarnings.Clear();
        CancelScheduledFishingFinalizationCore();
        CancelInactivityTimerCore();
        activeFishingAttempt = null;
        isPaused = false;
        isClosingSession = false;
        activeSessionId = null;
        lastActivityAtUtc = null;
        sessionSource = GatheringSessionSource.Unknown;
        sessionStartedAtUtc = nowUtc;
    }

    private void EnsureSessionStartedCore(DateTime startedAtUtc, GatheringSessionSource source)
    {
        if (activeSessionId is not null)
        {
            return;
        }

        activeSessionId = Guid.NewGuid();
        sessionStartedAtUtc = startedAtUtc;
        lastActivityAtUtc = startedAtUtc;
        sessionSource = source;
        Log.Information("Gathering session started. SessionId={SessionId} Source={Source}", activeSessionId, source);
    }

    private void RecordAggregatedItem(
        int itemId,
        int quality,
        long amount,
        DateTime occurredAtUtc,
        GatheringSessionSource source,
        long? estimatedMarketValue)
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

        estimatedMarketValue ??= estimatedMarketValues.Get(itemId, quality);
        itemAggregate.EstimatedMarketValue = estimatedMarketValue;
        itemAggregate.Source = CombineSources(itemAggregate.Source, source);

        if (estimatedMarketValue is null
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
        var hasActiveSession = activeSessionId is not null && itemAggregates.Values.Sum(x => x.Amount) > 0;
        var activeElapsed = hasActiveSession ? GetActiveElapsed(nowUtc) : TimeSpan.Zero;
        var activeMinutes = Math.Max(activeElapsed.TotalMinutes, 1d / 60d);
        var activeHours = activeElapsed.TotalHours;

        var summaryRows = itemAggregates
            .Values
            .Select(item =>
            {
                var emv = item.EstimatedMarketValue;
                long? totalEstimatedMarketValue = emv is null ? null : emv.Value * item.Amount;
                return new GatheringSummaryRow(
                    item.ItemId,
                    item.Quality,
                    item.ItemUniqueName,
                    item.ItemName,
                    item.Amount,
                    emv,
                    totalEstimatedMarketValue,
                    item.Amount / activeMinutes,
                    item.Amount / activeMinutes * 60d,
                    totalEstimatedMarketValue is null || activeHours <= 0
                        ? null
                        : (long)Math.Round(totalEstimatedMarketValue.Value / activeHours));
            })
            .OrderByDescending(x => x.TotalEstimatedMarketValue ?? 0)
            .ThenByDescending(x => x.Amount)
            .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var bucketRows = minuteBuckets
            .Values
            .Select(bucket =>
            {
                var bucketEstimatedMarketValue = CalculateBucketEstimatedMarketValue(bucket);
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
            playerState.UserObjectId > 0,
            hasActiveSession,
            sessionStartedAtUtc,
            activeElapsed,
            summaryRows.Sum(x => x.Amount),
            summaryRows.Sum(x => x.TotalEstimatedMarketValue ?? 0),
            summaryRows,
            bucketRows);
    }

    private GatheringCompletedSessionSnapshot BuildCompletedSnapshot(DateTime endedAtUtc)
    {
        var activeElapsed = GetActiveElapsed(endedAtUtc);
        var items = itemAggregates
            .Values
            .Select(item => new GatheringCompletedSessionItemSnapshot(
                item.ItemId,
                item.Quality,
                item.ItemUniqueName,
                item.ItemName,
                item.Amount,
                item.EstimatedMarketValue,
                item.EstimatedMarketValue is null ? null : item.EstimatedMarketValue.Value * item.Amount,
                item.Source))
            .OrderByDescending(x => x.TotalEstimatedMarketValue ?? 0)
            .ThenByDescending(x => x.Amount)
            .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var totalValue = items.Sum(x => x.TotalEstimatedMarketValue ?? 0);
        var silverPerHour = activeElapsed.TotalSeconds <= 0 || totalValue <= 0
            ? 0
            : (long)Math.Round(totalValue / activeElapsed.TotalHours);

        return new GatheringCompletedSessionSnapshot(
            activeSessionId!.Value,
            sessionStartedAtUtc,
            endedAtUtc,
            lastActivityAtUtc ?? endedAtUtc,
            activeElapsed,
            items.Sum(x => x.Amount),
            totalValue,
            silverPerHour,
            sessionSource,
            items);
    }

    private GatheringSessionCheckpoint? BuildCheckpoint(DateTime updatedAtUtc)
    {
        if (activeSessionId is null || lastActivityAtUtc is null || itemAggregates.Count == 0)
        {
            return null;
        }

        var payload = new GatheringSessionCheckpointPayload(
            itemAggregates.Values
                .Select(x => new GatheringSessionCheckpointItem(
                    x.ItemId,
                    x.Quality,
                    x.ItemUniqueName,
                    x.ItemName,
                    x.Amount,
                    x.EstimatedMarketValue,
                    x.Source))
                .ToList(),
            pauseIntervals
                .Select(x => new GatheringSessionCheckpointPauseInterval(x.StartedAtUtc, x.EndedAtUtc))
                .ToList());

        return new GatheringSessionCheckpoint(
            activeSessionId.Value,
            sessionStartedAtUtc,
            lastActivityAtUtc.Value,
            updatedAtUtc,
            isPaused,
            sessionSource,
            payload);
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

    private long? CalculateBucketEstimatedMarketValue(GatheringMinuteBucket bucket)
    {
        long totalEstimatedMarketValue = 0;
        var hasEstimatedMarketValue = false;

        foreach (var (itemKey, amount) in bucket.ItemAmounts)
        {
            if (!itemAggregates.TryGetValue(itemKey, out var item) || item.EstimatedMarketValue is null)
            {
                continue;
            }

            hasEstimatedMarketValue = true;
            totalEstimatedMarketValue += item.EstimatedMarketValue.Value * amount;
        }

        return hasEstimatedMarketValue ? totalEstimatedMarketValue : null;
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

    private void ScheduleInactivityAutoCloseCore(DateTime nowUtc)
    {
        if (lastActivityAtUtc is null || activeSessionId is null)
        {
            CancelInactivityTimerCore();
            return;
        }

        var dueTime = lastActivityAtUtc.Value + InactivityTimeout - nowUtc;
        if (dueTime < TimeSpan.Zero)
        {
            dueTime = TimeSpan.Zero;
        }

        var version = ++inactivityTimerVersion;
        inactivityTimer?.Dispose();
        inactivityTimer = new Timer(
            OnInactivityTimerElapsed,
            version,
            dueTime,
            Timeout.InfiniteTimeSpan);
    }

    private void CancelInactivityTimerCore()
    {
        inactivityTimerVersion++;
        inactivityTimer?.Dispose();
        inactivityTimer = null;
    }

    private void OnFishingFinalizeTimerElapsed(object? state)
    {
        var version = state is int value ? value : 0;
        GatheringTrackerSnapshot? snapshot = null;
        GatheringSessionCheckpoint? checkpoint = null;
        lock (sync)
        {
            if (version != fishingFinalizeVersion)
            {
                return;
            }

            fishingFinalizeTimer?.Dispose();
            fishingFinalizeTimer = null;
            snapshot = FinalizeFishingCore(DateTime.UtcNow, out checkpoint);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
            SaveCheckpointInBackground(checkpoint);
        }
    }

    private void OnInactivityTimerElapsed(object? state)
    {
        var version = state is int value ? value : 0;
        var shouldClose = false;
        var nowUtc = DateTime.UtcNow;
        var endedAtUtc = nowUtc;
        lock (sync)
        {
            if (version != inactivityTimerVersion || activeSessionId is null || lastActivityAtUtc is null || isClosingSession)
            {
                return;
            }

            shouldClose = nowUtc - lastActivityAtUtc.Value >= InactivityTimeout;
            endedAtUtc = lastActivityAtUtc.Value + InactivityTimeout;
            if (!shouldClose)
            {
                ScheduleInactivityAutoCloseCore(nowUtc);
            }
        }

        if (shouldClose)
        {
            Log.Information("Gathering session automatically closed because of inactivity.");
            _ = CloseCurrentSessionAsync("automatically", endedAtUtc);
        }
    }

    private GatheringTrackerSnapshot? FinalizeFishingCore(
        DateTime receivedAtUtc,
        out GatheringSessionCheckpoint? checkpoint)
    {
        checkpoint = null;
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

        if (attempt.ConfirmedRewards.Count == 0)
        {
            Log.Debug(
                "Fishing attempt finalized with no confirmed rewards. EventId={EventId} DiscoveredCount={DiscoveredCount}",
                attempt.EventId,
                attempt.DiscoveredItems.Count);
            return null;
        }

        EnsureSessionStartedCore(attempt.StartedAtUtc, GatheringSessionSource.Fishing);
        foreach (var reward in attempt.ConfirmedRewards)
        {
            RecordAggregatedItem(
                reward.ItemId,
                reward.Quality,
                reward.Quantity,
                attempt.StartedAtUtc,
                GatheringSessionSource.Fishing,
                reward.EstimatedMarketValue);
        }

        lastActivityAtUtc = receivedAtUtc;
        sessionSource = CombineSources(sessionSource, GatheringSessionSource.Fishing);
        ScheduleInactivityAutoCloseCore(receivedAtUtc);
        checkpoint = BuildCheckpoint(receivedAtUtc);
        return BuildSnapshot(receivedAtUtc);
    }

    private static bool IsValidCheckpoint(GatheringSessionCheckpoint checkpoint)
    {
        return checkpoint.SessionId != Guid.Empty
            && checkpoint.Payload.Items.Count > 0
            && checkpoint.Payload.Items.Sum(x => x.Amount) > 0
            && checkpoint.StartedAtUtc <= checkpoint.LastActivityAtUtc;
    }

    private static GatheringSessionSource CombineSources(
        GatheringSessionSource current,
        GatheringSessionSource next)
    {
        if (current is GatheringSessionSource.Unknown)
        {
            return next;
        }

        if (next is GatheringSessionSource.Unknown || current == next)
        {
            return current;
        }

        return GatheringSessionSource.Mixed;
    }

    private void SaveCheckpointInBackground(GatheringSessionCheckpoint? checkpoint)
    {
        if (checkpoint is null)
        {
            return;
        }

        _ = sessionPersistence.SaveUnfinishedCheckpointAsync(checkpoint);
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
        public long? EstimatedMarketValue { get; set; }
        public GatheringSessionSource Source { get; set; } = GatheringSessionSource.Unknown;
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
        long? EstimatedMarketValue);

    private sealed record FishingConfirmedReward(
        long DiscoveredObjectId,
        int ItemId,
        int Quality,
        int Quantity,
        long? EstimatedMarketValue);

    private readonly record struct MissingEstimatedMarketValueWarning(
        GatheringItemKey ItemKey,
        GatheringSessionSource Source);

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

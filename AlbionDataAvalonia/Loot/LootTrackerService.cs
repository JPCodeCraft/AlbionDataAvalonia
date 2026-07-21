using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Loot.Models;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Party.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.State.Events;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace AlbionDataAvalonia.Loot;

public sealed class LootTrackerService : IDisposable
{
    // Chest loot arrives through several partial packet views. Local loots usually
    // have item object ids, restricted party chest loots may only have item
    // type/amount, and public chests can reassign or merge stacks into new public
    // object ids. The transient caches below correlate those views by chest source,
    // container id, item object id, and item type so missing public-container
    // removals can still be recorded as guessed party loot.
    private const int EmptyLootChestState = 8;

    private static readonly TimeSpan TransientRetention = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DetachedPublicContainerEmptyUpdateWindow = TimeSpan.FromSeconds(30);

    private readonly object sync = new();
    private readonly List<LootRecord> records = new();
    private readonly Dictionary<long, DiscoveredLootItem> discoveredItems = new();
    private readonly Dictionary<long, LootSource> sources = new();
    private readonly HashSet<long> confirmedLootChestObjectIds = new();
    private readonly Dictionary<Guid, LootContainer> containers = new();
    private readonly Dictionary<Guid, PendingLootContainer> pendingContainers = new();
    private readonly Dictionary<long, RecentDetachedLootContainer> recentDetachedLootContainers = new();
    private readonly Dictionary<long, RecordedItemObject> recordedItemObjectIds = new();
    private readonly Dictionary<long, PendingPartyLootItem> pendingPartyLootItems = new();
    private readonly Dictionary<long, List<RecentPartyLootItemType>> recentPartyLootItemTypes = new();
    private readonly Dictionary<long, RecentLootChestUpdate> recentLootChestUpdates = new();
    private readonly Dictionary<long, RecentEmptyLootChestUpdate> recentEmptyLootChestUpdates = new();
    private readonly Dictionary<long, RecentInventoryDelete> recentInventoryDeletes = new();
    private readonly List<RecentPickupCorrelation> recentLocalPickups = new();
    private readonly List<RecentPickupCorrelation> recentBroadcastPickups = new();
    private readonly List<PendingLocalMoveBySlot> pendingLocalMovesBySlot = new();
    private readonly SettingsManager settingsManager;
    private readonly PartyTrackerService partyTracker;
    private readonly ItemsIdsService itemsIdsService;
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly ItemEstimatedMarketValueBackendLoader itemEstimatedMarketValueBackendLoader;
    private readonly PlayerState playerState;

    private bool isDisabled;
    private bool isPaused;

    public event Action<LootTrackerSnapshot>? SnapshotChanged;

    public LootTrackerService(
        SettingsManager settingsManager,
        PartyTrackerService partyTracker,
        ItemsIdsService itemsIdsService,
        ItemEstimatedMarketValueService itemEstimatedMarketValues,
        ItemEstimatedMarketValueBackendLoader itemEstimatedMarketValueBackendLoader,
        PlayerState playerState)
    {
        this.settingsManager = settingsManager;
        this.partyTracker = partyTracker;
        this.itemsIdsService = itemsIdsService;
        this.itemEstimatedMarketValues = itemEstimatedMarketValues;
        this.itemEstimatedMarketValueBackendLoader = itemEstimatedMarketValueBackendLoader;
        this.playerState = playerState;

        isDisabled = settingsManager.UserSettings.DisableLootTracker;
        settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        itemEstimatedMarketValues.EstimatedMarketValuesChanged += OnEstimatedMarketValuesChanged;
        playerState.OnPlayerStateChanged += OnPlayerStateChanged;
        partyTracker.SnapshotChanged += OnPartySnapshotChanged;
    }

    public LootTrackerSnapshot CurrentSnapshot
    {
        get
        {
            lock (sync)
            {
                return BuildSnapshot();
            }
        }
    }

    public void Dispose()
    {
        settingsManager.UserSettings.PropertyChanged -= OnUserSettingsPropertyChanged;
        itemEstimatedMarketValues.EstimatedMarketValuesChanged -= OnEstimatedMarketValuesChanged;
        playerState.OnPlayerStateChanged -= OnPlayerStateChanged;
        partyTracker.SnapshotChanged -= OnPartySnapshotChanged;
    }

    public void SetPaused(bool paused)
    {
        LootTrackerSnapshot? snapshot = null;
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

            isPaused = paused;
            ClearTransientStateCore();
            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
        Log.Information(paused ? "Loot tracker paused." : "Loot tracker resumed.");
    }

    public void Clear()
    {
        LootTrackerSnapshot snapshot;
        lock (sync)
        {
            // Clear only the visible log. Active loot containers can stay open after
            // the user clears the table, and wiping them makes later pickups look
            // like unknown bank/container moves.
            records.Clear();
            recentLocalPickups.Clear();
            recentBroadcastPickups.Clear();
            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void ResetTransientState()
    {
        lock (sync)
        {
            ClearTransientStateCore();
        }
    }

    public void IdentifyLootSource(long objectId, string? sourceName)
    {
        if (objectId <= 0)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            if (IsConfirmedLootChestSourceCore(objectId))
            {
                PruneTransientStateCore(DateTime.UtcNow);
                return;
            }

            var nowUtc = DateTime.UtcNow;
            sources[objectId] = CreateSource(sourceName, LootSourceKind.Unknown);
            snapshot = ActivatePendingContainersForSourceCore(objectId, nowUtc);
            PruneTransientStateCore(nowUtc);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void IdentifyLootChest(long objectId, string? uniqueName, string? uniqueNameWithLocation)
    {
        if (objectId <= 0)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var name = !string.IsNullOrWhiteSpace(uniqueName)
                ? uniqueName
                : uniqueNameWithLocation;
            snapshot = ConfirmLootChestSourceCore(objectId, name, DateTime.UtcNow);
            PruneTransientStateCore(DateTime.UtcNow);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void MarkLootChest(long objectId)
    {
        if (objectId <= 0)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            snapshot = ConfirmLootChestSourceCore(objectId, null, DateTime.UtcNow);
            PruneTransientStateCore(DateTime.UtcNow);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void UpdateLootChest(long objectId, IReadOnlyList<Guid> playerGuids, int state)
    {
        if (objectId <= 0)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            snapshot = ConfirmLootChestSourceCore(objectId, null, nowUtc);

            if (playerGuids.Count > 0)
            {
                recentLootChestUpdates[objectId] = new RecentLootChestUpdate(
                    playerGuids.Where(guid => guid != Guid.Empty).Distinct().ToArray(),
                    nowUtc);
            }

            if (state == EmptyLootChestState)
            {
                recentEmptyLootChestUpdates[objectId] = new RecentEmptyLootChestUpdate(nowUtc);
                snapshot = RecordDetachedPublicContainerRemainingItemsCore(objectId, nowUtc) ?? snapshot;
            }

            PruneTransientStateCore(nowUtc);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void AttachContainer(long objectId, Guid containerId, Guid privateContainerId, IReadOnlyList<long> slotItems)
    {
        if (objectId <= 0 || containerId == Guid.Empty)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (!IsKnownLootSourceCore(objectId))
            {
                var pendingContainer = new PendingLootContainer(
                    objectId,
                    containerId,
                    privateContainerId,
                    slotItems.ToArray(),
                    nowUtc);
                pendingContainers[containerId] = pendingContainer;
                if (privateContainerId != Guid.Empty)
                {
                    pendingContainers[privateContainerId] = pendingContainer;
                }

                PruneTransientStateCore(nowUtc);
                return;
            }

            snapshot = AttachConfirmedContainerCore(
                objectId,
                containerId,
                privateContainerId,
                slotItems,
                nowUtc);
            PruneTransientStateCore(nowUtc);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void DetachContainer(Guid containerId)
    {
        if (containerId == Guid.Empty)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            if (!containers.TryGetValue(containerId, out var container))
            {
                return;
            }

            if (container.PrivateContainerId == Guid.Empty)
            {
                var nowUtc = DateTime.UtcNow;
                recentDetachedLootContainers[container.SourceObjectId] = new RecentDetachedLootContainer(
                    container,
                    nowUtc);
                snapshot = RecordDetachedPublicContainerAfterRecentEmptyUpdateCore(
                    container.SourceObjectId,
                    nowUtc);
            }

            foreach (var key in containers
                .Where(entry => ReferenceEquals(entry.Value, container))
                .Select(entry => entry.Key)
                .ToArray())
            {
                containers.Remove(key);
            }

            PruneTransientStateCore(DateTime.UtcNow);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void DiscoverItem(NewItem item)
    {
        if (item.ObjectId is not { } objectId || objectId <= 0 || item.ItemIndex <= 0)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var sourceObjectId = containers.Values
                .FirstOrDefault(container => container.SlotItems.Contains(objectId))
                ?.SourceObjectId;
            discoveredItems[objectId] = new DiscoveredLootItem(
                objectId,
                item.ItemIndex,
                Math.Max(1, item.Quantity),
                Math.Max(1, item.Quality),
                item.ItemUniqueName,
                item.ItemUsName,
                item.EstimatedMarketValue > 0 ? item.EstimatedMarketValue : null,
                item.BlackMarketEstimatedMarketValue is > 0 ? item.BlackMarketEstimatedMarketValue : null,
                sourceObjectId,
                nowUtc);
            snapshot = ReplayPendingLocalMovesForItemCore(objectId, nowUtc);
            PruneTransientStateCore(nowUtc);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void RecordOtherPickup(
        string? sourceName,
        string? playerName,
        bool isSilver,
        int itemId,
        long amount)
    {
        if (isSilver || itemId <= 0 || amount <= 0 || string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            PruneTransientStateCore(nowUtc);
            var normalizedPlayerName = playerName.Trim();
            var source = CreateSource(sourceName, LootSourceKind.Unknown);
            if (IsLocalPlayer(normalizedPlayerName)
                && TryMatchCorrelation(recentLocalPickups, itemId, amount, nowUtc, out var localCorrelation))
            {
                localCorrelation.Matched = true;
                UpdateRecordSourceCore(localCorrelation.RecordId, source);
                snapshot = BuildSnapshot();
            }
            else
            {
                var item = FindBestDiscoveredItemCore(itemId, amount);
                var record = CreateRecordCore(
                    normalizedPlayerName,
                    source,
                    item?.ObjectId,
                    itemId,
                    amount,
                    item?.Quality,
                    item?.UniqueName,
                    item?.Name,
                    item?.EstimatedMarketValue,
                    item?.BlackMarketEstimatedMarketValue,
                    nowUtc);
                records.Add(record);
                if (IsLocalPlayer(normalizedPlayerName))
                {
                    recentBroadcastPickups.Add(new RecentPickupCorrelation(
                        record.Id,
                        itemId,
                        amount,
                        nowUtc));
                }

                snapshot = BuildSnapshot();
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void TrackPartyLootItems(
        long sourceObjectId,
        IReadOnlyList<long> itemObjectIds,
        IReadOnlyList<int> itemIds,
        IReadOnlyList<int> qualities,
        IReadOnlyList<int> amounts,
        IReadOnlyList<string> playerNames)
    {
        if (sourceObjectId <= 0)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            PruneTransientStateCore(nowUtc);
            TrackRecentPartyLootItemTypesCore(sourceObjectId, itemIds, qualities, amounts, nowUtc);
            snapshot = ConfirmLootChestSourceCore(sourceObjectId, null, nowUtc);
            for (var index = 0; index < itemObjectIds.Count; index++)
            {
                var itemObjectId = itemObjectIds[index];
                var itemId = index < itemIds.Count ? itemIds[index] : 0;
                var amount = index < amounts.Count ? amounts[index] : 0;
                var playerName = index < playerNames.Count ? playerNames[index]?.Trim() : string.Empty;
                if (itemObjectId <= 0
                    || itemId <= 0
                    || amount <= 0
                    || string.IsNullOrWhiteSpace(playerName))
                {
                    continue;
                }

                var pendingItem = new PendingPartyLootItem(
                    sourceObjectId,
                    itemObjectId,
                    itemId,
                    amount,
                    playerName!,
                    nowUtc);
                pendingPartyLootItems[itemObjectId] = pendingItem;
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void RecordPartyLootItemsRemoved(long sourceObjectId, IReadOnlyList<long> itemObjectIds)
    {
        if (itemObjectIds.Count == 0)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            PruneTransientStateCore(nowUtc);
            snapshot = ConfirmLootChestSourceCore(sourceObjectId, null, nowUtc);
            foreach (var itemObjectId in itemObjectIds.Where(id => id > 0).Distinct())
            {
                if (!pendingPartyLootItems.TryGetValue(itemObjectId, out var pendingItem))
                {
                    continue;
                }

                pendingPartyLootItems.Remove(itemObjectId);
                if (IsRecentlyRecordedCore(itemObjectId, pendingItem.ItemId, pendingItem.Amount, nowUtc)
                    || HasRecentRecordCore(pendingItem.PlayerName, pendingItem.ItemId, pendingItem.Amount, nowUtc))
                {
                    recordedItemObjectIds[itemObjectId] = new RecordedItemObject(
                        pendingItem.ItemId,
                        pendingItem.Amount,
                        nowUtc);
                    continue;
                }

                var resolvedSourceObjectId = pendingItem.SourceObjectId > 0
                    ? pendingItem.SourceObjectId
                    : sourceObjectId;
                var source = sources.TryGetValue(resolvedSourceObjectId, out var identifiedSource)
                    ? identifiedSource
                    : CreateSource("Loot Chest", LootSourceKind.Chest);
                discoveredItems.TryGetValue(itemObjectId, out var discoveredItem);
                var record = CreateRecordCore(
                    pendingItem.PlayerName,
                    source,
                    itemObjectId,
                    pendingItem.ItemId,
                    pendingItem.Amount,
                    discoveredItem?.Quality,
                    discoveredItem?.UniqueName,
                    discoveredItem?.Name,
                    discoveredItem?.EstimatedMarketValue,
                    discoveredItem?.BlackMarketEstimatedMarketValue,
                    nowUtc);
                records.Add(record);
                recordedItemObjectIds[itemObjectId] = new RecordedItemObject(
                    pendingItem.ItemId,
                    pendingItem.Amount,
                    nowUtc);
                snapshot = BuildSnapshot();
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void RecordPartyLootItemTypesRemoved(
        long sourceObjectId,
        IReadOnlyList<int> itemIds,
        IReadOnlyList<int> amounts,
        IReadOnlyList<int> qualities)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            PruneTransientStateCore(nowUtc);
            snapshot = ConfirmLootChestSourceCore(sourceObjectId, null, nowUtc);
            for (var index = 0; index < itemIds.Count; index++)
            {
                var itemId = itemIds[index];
                var amount = GetIndexedValue(amounts, index, 1);
                var quality = GetIndexedValue(qualities, index, 0);
                var playerName = ResolveRecentLootChestPlayerNameCore(sourceObjectId);
                if (itemId <= 0 || amount <= 0)
                {
                    continue;
                }

                RemoveRecentPartyLootItemTypeCore(sourceObjectId, itemId, amount);
                var discoveredItem = FindBestDiscoveredItemCore(sourceObjectId, itemId, amount);
                if (discoveredItem is not null
                    && IsRecentlyRecordedCore(discoveredItem.ObjectId, itemId, amount, nowUtc))
                {
                    continue;
                }

                if (HasRecentRecordCore(playerName, itemId, amount, nowUtc))
                {
                    if (discoveredItem is not null)
                    {
                        recordedItemObjectIds[discoveredItem.ObjectId] = new RecordedItemObject(
                            itemId,
                            amount,
                            nowUtc);
                    }

                    continue;
                }

                var source = sources.TryGetValue(sourceObjectId, out var identifiedSource)
                    ? identifiedSource
                    : CreateSource("Loot Chest", LootSourceKind.Chest);
                var record = CreateRecordCore(
                    playerName,
                    source,
                    discoveredItem?.ObjectId,
                    itemId,
                    amount,
                    discoveredItem?.Quality ?? (quality > 0 ? quality : null),
                    discoveredItem?.UniqueName,
                    discoveredItem?.Name,
                    discoveredItem?.EstimatedMarketValue,
                    discoveredItem?.BlackMarketEstimatedMarketValue,
                    nowUtc);
                records.Add(record);
                if (discoveredItem is not null)
                {
                    recordedItemObjectIds[discoveredItem.ObjectId] = new RecordedItemObject(
                        itemId,
                        amount,
                        nowUtc);
                }

                snapshot = BuildSnapshot();
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void RecordInventoryDeleteItem(long itemObjectId)
    {
        if (itemObjectId <= 0)
        {
            return;
        }

        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            PruneTransientStateCore(nowUtc);
            snapshot = RecordUnknownLootOwnerItemObjectCore(itemObjectId, nowUtc);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void RecordLocalMoveBySlot(int sourceSlot, Guid sourceContainerId, Guid destinationContainerId)
    {
        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            if (sourceContainerId == Guid.Empty)
            {
                Log.Warning(
                    "Loot tracker skipped local move by slot because source container id is empty. SourceSlot: {SourceSlot}, DestinationContainerId: {DestinationContainerId}",
                    sourceSlot,
                    destinationContainerId);
                return;
            }

            if (sourceContainerId == destinationContainerId)
            {
                return;
            }

            if (!containers.TryGetValue(sourceContainerId, out var container))
            {
                var nowUtc = DateTime.UtcNow;
                pendingLocalMovesBySlot.Add(new PendingLocalMoveBySlot(
                    sourceSlot,
                    sourceContainerId,
                    destinationContainerId,
                    nowUtc));
                PruneTransientStateCore(nowUtc);
                Log.Debug(
                    "Loot tracker queued local move by slot because source container is not tied to a known loot source yet. SourceSlot: {SourceSlot}, SourceContainerId: {SourceContainerId}, DestinationContainerId: {DestinationContainerId}",
                    sourceSlot,
                    sourceContainerId,
                    destinationContainerId);
                return;
            }

            if (sourceSlot < 0 || sourceSlot >= container.SlotItems.Count)
            {
                Log.Warning(
                    "Loot tracker skipped local move by slot because source slot was outside the container. SourceSlot: {SourceSlot}, SlotCount: {SlotCount}, SourceContainerId: {SourceContainerId}, SourceObjectId: {SourceObjectId}",
                    sourceSlot,
                    container.SlotItems.Count,
                    sourceContainerId,
                    container.SourceObjectId);
                return;
            }

            snapshot = RecordLocalItemObjectCore(container.SlotItems[sourceSlot], container.SourceObjectId);
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void RecordLocalMoveGivenItems(
        Guid sourceContainerId,
        Guid destinationContainerId,
        IReadOnlyList<long> itemObjectIds)
    {
        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            if (sourceContainerId == Guid.Empty)
            {
                Log.Warning(
                    "Loot tracker received local move given items with empty source container id. Only items already linked to a loot source will be recorded. ItemObjectIds: {ItemObjectIds}, DestinationContainerId: {DestinationContainerId}",
                    itemObjectIds,
                    destinationContainerId);
            }

            if (sourceContainerId != Guid.Empty && sourceContainerId == destinationContainerId)
            {
                return;
            }

            LootContainer? container = null;
            if (sourceContainerId != Guid.Empty && !containers.TryGetValue(sourceContainerId, out container))
            {
                Log.Warning(
                    "Loot tracker received local move given items from unknown source container. Only items already linked to a loot source will be recorded. ItemObjectIds: {ItemObjectIds}, SourceContainerId: {SourceContainerId}, DestinationContainerId: {DestinationContainerId}",
                    itemObjectIds,
                    sourceContainerId,
                    destinationContainerId);
            }

            var containerItems = container?.SlotItems.ToHashSet();
            foreach (var itemObjectId in itemObjectIds.Distinct())
            {
                var sourceObjectId = container?.SourceObjectId ?? 0;
                var discoveredSourceObjectId = discoveredItems.TryGetValue(itemObjectId, out var discoveredItem)
                    ? discoveredItem.SourceObjectId
                    : null;
                if (discoveredSourceObjectId is { } linkedSourceObjectId)
                {
                    sourceObjectId = linkedSourceObjectId;
                }

                if (containerItems is not null && !containerItems.Contains(itemObjectId))
                {
                    if (discoveredSourceObjectId is null)
                    {
                        Log.Warning(
                            "Loot tracker skipped local move given item because item object id was not in the source container and was not linked to a loot source. ItemObjectId: {ItemObjectId}, SourceContainerId: {SourceContainerId}, SourceObjectId: {SourceObjectId}",
                            itemObjectId,
                            sourceContainerId,
                            sourceObjectId);
                        continue;
                    }

                    Log.Warning(
                        "Loot tracker will record local move given item from discovered item data because item object id was not in the source container. ItemObjectId: {ItemObjectId}, SourceContainerId: {SourceContainerId}, SourceObjectId: {SourceObjectId}",
                        itemObjectId,
                        sourceContainerId,
                        sourceObjectId);
                }

                if (container is null && discoveredSourceObjectId is null)
                {
                    Log.Warning(
                        "Loot tracker skipped local move given item because source container was not known and item object id was not linked to a loot source. ItemObjectId: {ItemObjectId}, SourceContainerId: {SourceContainerId}",
                        itemObjectId,
                        sourceContainerId);
                    continue;
                }

                snapshot = RecordLocalItemObjectCore(itemObjectId, sourceObjectId) ?? snapshot;
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private LootTrackerSnapshot? ConfirmLootChestSourceCore(long objectId, string? sourceName, DateTime nowUtc)
    {
        var resolvedName = !string.IsNullOrWhiteSpace(sourceName)
            ? sourceName
            : sources.TryGetValue(objectId, out var existingSource) && existingSource.Kind == LootSourceKind.Chest
                ? existingSource.Name
                : "Loot Chest";
        sources[objectId] = CreateSource(resolvedName, LootSourceKind.Chest);
        confirmedLootChestObjectIds.Add(objectId);
        return ActivatePendingContainersForSourceCore(objectId, nowUtc);
    }

    private bool IsConfirmedLootChestSourceCore(long objectId)
    {
        return confirmedLootChestObjectIds.Contains(objectId)
            && sources.TryGetValue(objectId, out var source)
            && source.Kind == LootSourceKind.Chest;
    }

    private bool IsKnownLootSourceCore(long objectId)
    {
        return sources.ContainsKey(objectId);
    }

    private LootTrackerSnapshot? ActivatePendingContainersForSourceCore(long sourceObjectId, DateTime nowUtc)
    {
        LootTrackerSnapshot? snapshot = null;
        foreach (var pendingContainer in pendingContainers.Values
            .Where(container => container.SourceObjectId == sourceObjectId)
            .DistinctBy(container => container.ContainerId)
            .ToArray())
        {
            foreach (var key in pendingContainers
                .Where(entry => ReferenceEquals(entry.Value, pendingContainer))
                .Select(entry => entry.Key)
                .ToArray())
            {
                pendingContainers.Remove(key);
            }

            snapshot = AttachConfirmedContainerCore(
                pendingContainer.SourceObjectId,
                pendingContainer.ContainerId,
                pendingContainer.PrivateContainerId,
                pendingContainer.SlotItems,
                nowUtc) ?? snapshot;
        }

        return snapshot;
    }

    private LootTrackerSnapshot? AttachConfirmedContainerCore(
        long objectId,
        Guid containerId,
        Guid privateContainerId,
        IReadOnlyList<long> slotItems,
        DateTime nowUtc)
    {
        LootTrackerSnapshot? snapshot = null;
        if (privateContainerId == Guid.Empty)
        {
            snapshot = RecordDetachedPublicContainerMissingItemsCore(objectId, slotItems, nowUtc);
        }

        var container = new LootContainer(
            objectId,
            containerId,
            privateContainerId,
            slotItems.ToArray(),
            nowUtc);
        containers[containerId] = container;
        if (privateContainerId != Guid.Empty)
        {
            containers[privateContainerId] = container;
        }

        foreach (var itemObjectId in slotItems.Where(itemObjectId => itemObjectId > 0))
        {
            if (discoveredItems.TryGetValue(itemObjectId, out var item))
            {
                item.SourceObjectId = objectId;
            }
        }

        snapshot = ReplayPendingLocalMovesForContainerCore(container, nowUtc) ?? snapshot;

        if (privateContainerId == Guid.Empty)
        {
            AttachPublicContainerItemTypesCore(objectId, slotItems, nowUtc);
        }

        return snapshot;
    }

    private LootTrackerSnapshot? ReplayPendingLocalMovesForItemCore(long itemObjectId, DateTime nowUtc)
    {
        var container = containers.Values
            .DistinctBy(value => value.ContainerId)
            .FirstOrDefault(value => value.SlotItems.Contains(itemObjectId));
        return container is null
            ? null
            : ReplayPendingLocalMovesForContainerCore(container, nowUtc);
    }

    private LootTrackerSnapshot? ReplayPendingLocalMovesForContainerCore(LootContainer container, DateTime nowUtc)
    {
        LootTrackerSnapshot? snapshot = null;
        foreach (var pendingMove in pendingLocalMovesBySlot
            .Where(move => move.SourceContainerId == container.ContainerId
                || move.SourceContainerId == container.PrivateContainerId)
            .ToArray())
        {
            if (pendingMove.DestinationContainerId == pendingMove.SourceContainerId)
            {
                pendingLocalMovesBySlot.Remove(pendingMove);
                continue;
            }

            if (pendingMove.SourceSlot < 0 || pendingMove.SourceSlot >= container.SlotItems.Count)
            {
                pendingLocalMovesBySlot.Remove(pendingMove);
                Log.Warning(
                    "Loot tracker skipped queued local move by slot because source slot was outside the confirmed loot container. SourceSlot: {SourceSlot}, SlotCount: {SlotCount}, SourceContainerId: {SourceContainerId}, SourceObjectId: {SourceObjectId}",
                    pendingMove.SourceSlot,
                    container.SlotItems.Count,
                    pendingMove.SourceContainerId,
                    container.SourceObjectId);
                continue;
            }

            var itemObjectId = container.SlotItems[pendingMove.SourceSlot];
            if (itemObjectId <= 0)
            {
                pendingLocalMovesBySlot.Remove(pendingMove);
                continue;
            }

            if (!discoveredItems.ContainsKey(itemObjectId))
            {
                continue;
            }

            snapshot = RecordLocalItemObjectCore(itemObjectId, container.SourceObjectId) ?? snapshot;
            pendingLocalMovesBySlot.Remove(pendingMove);
        }

        if (snapshot is not null)
        {
            PruneTransientStateCore(nowUtc);
        }

        return snapshot;
    }

    private void TrackRecentPartyLootItemTypesCore(
        long sourceObjectId,
        IReadOnlyList<int> itemIds,
        IReadOnlyList<int> qualities,
        IReadOnlyList<int> amounts,
        DateTime nowUtc)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        var itemTypes = new List<RecentPartyLootItemType>();
        for (var index = 0; index < itemIds.Count; index++)
        {
            var itemId = itemIds[index];
            var amount = GetIndexedValue(amounts, index, 0);
            var quality = GetIndexedValue(qualities, index, 0);
            if (itemId <= 0 || amount <= 0)
            {
                continue;
            }

            itemTypes.Add(new RecentPartyLootItemType(
                itemId,
                amount,
                quality > 0 ? quality : null,
                nowUtc));
        }

        if (itemTypes.Count > 0)
        {
            recentPartyLootItemTypes[sourceObjectId] = itemTypes;
        }
    }

    private void AttachPublicContainerItemTypesCore(long sourceObjectId, IReadOnlyList<long> slotItems, DateTime nowUtc)
    {
        if (!recentPartyLootItemTypes.TryGetValue(sourceObjectId, out var itemTypes))
        {
            return;
        }

        var itemObjectIds = slotItems.Where(id => id > 0).ToArray();
        if (itemObjectIds.Length == 0)
        {
            return;
        }

        var remainingItemTypes = GetRemainingPublicItemTypesCore(sourceObjectId, itemObjectIds, itemTypes, nowUtc);
        if (itemObjectIds.Length != remainingItemTypes.Count)
        {
            Log.Debug(
                "Loot tracker did not map public container item types because slot count did not match remembered party loot item count. SourceObjectId: {SourceObjectId}, SlotCount: {SlotCount}, ItemTypeCount: {ItemTypeCount}",
                sourceObjectId,
                itemObjectIds.Length,
                remainingItemTypes.Count);
            return;
        }

        for (var index = 0; index < itemObjectIds.Length; index++)
        {
            var itemObjectId = itemObjectIds[index];
            if (discoveredItems.ContainsKey(itemObjectId))
            {
                continue;
            }

            var itemType = remainingItemTypes[index];
            discoveredItems[itemObjectId] = new DiscoveredLootItem(
                itemObjectId,
                itemType.ItemId,
                itemType.Amount,
                itemType.Quality ?? 0,
                string.Empty,
                string.Empty,
                null,
                null,
                sourceObjectId,
                nowUtc);
        }

        Log.Debug(
            "Loot tracker mapped public container item types from remembered party loot data. SourceObjectId: {SourceObjectId}, ItemCount: {ItemCount}",
            sourceObjectId,
            itemObjectIds.Length);
        recentPartyLootItemTypes.Remove(sourceObjectId);
    }

    private IReadOnlyList<RecentPartyLootItemType> GetRemainingPublicItemTypesCore(
        long sourceObjectId,
        IReadOnlyCollection<long> publicItemObjectIds,
        IReadOnlyList<RecentPartyLootItemType> rememberedItemTypes,
        DateTime nowUtc)
    {
        var remainingItemTypes = new List<RecentPartyLootItemType>(rememberedItemTypes);
        remainingItemTypes.AddRange(discoveredItems.Values
            .Where(item => item.SourceObjectId == sourceObjectId
                && !publicItemObjectIds.Contains(item.ObjectId)
                && !recordedItemObjectIds.ContainsKey(item.ObjectId))
            .Select(item => new RecentPartyLootItemType(
                item.ItemId,
                item.Amount,
                item.Quality > 0 ? item.Quality : null,
                nowUtc)));

        return remainingItemTypes
            .GroupBy(itemType => new { itemType.ItemId, itemType.Quality })
            .Select(group => new RecentPartyLootItemType(
                group.Key.ItemId,
                group.Sum(itemType => itemType.Amount),
                group.Key.Quality,
                group.Max(itemType => itemType.SeenAtUtc)))
            .ToArray();
    }

    private void RemoveRecentPartyLootItemTypeCore(long sourceObjectId, int itemId, long amount)
    {
        if (!recentPartyLootItemTypes.TryGetValue(sourceObjectId, out var itemTypes))
        {
            return;
        }

        var index = itemTypes.FindIndex(itemType => itemType.ItemId == itemId && itemType.Amount == amount);
        if (index < 0)
        {
            index = itemTypes.FindIndex(itemType => itemType.ItemId == itemId);
        }

        if (index >= 0)
        {
            itemTypes.RemoveAt(index);
        }

        if (itemTypes.Count == 0)
        {
            recentPartyLootItemTypes.Remove(sourceObjectId);
        }
    }

    private LootTrackerSnapshot? RecordDetachedPublicContainerMissingItemsCore(
        long sourceObjectId,
        IReadOnlyCollection<long> attachedSlotItems,
        DateTime nowUtc)
    {
        if (!recentDetachedLootContainers.TryGetValue(sourceObjectId, out var detachedContainer))
        {
            return null;
        }

        recentDetachedLootContainers.Remove(sourceObjectId);
        if (nowUtc - detachedContainer.DetachedAtUtc > TransientRetention)
        {
            return null;
        }

        var attachedItemObjectIds = attachedSlotItems
            .Where(id => id > 0)
            .ToHashSet();
        var missingItemObjectIds = detachedContainer.Container.SlotItems
            .Where(id => id > 0 && !attachedItemObjectIds.Contains(id))
            .Distinct()
            .ToArray();

        LootTrackerSnapshot? snapshot = null;
        var recordedCount = 0;
        foreach (var itemObjectId in missingItemObjectIds)
        {
            var itemSnapshot = RecordUnknownLootOwnerItemObjectCore(itemObjectId, nowUtc);
            if (itemSnapshot is not null)
            {
                snapshot = itemSnapshot;
                recordedCount++;
            }
        }

        if (missingItemObjectIds.Length > 0)
        {
            Log.Debug(
                "Loot tracker processed public container items missing after reattach. SourceObjectId: {SourceObjectId}, MissingCount: {MissingCount}, RecordedCount: {RecordedCount}",
                sourceObjectId,
                missingItemObjectIds.Length,
                recordedCount);
        }

        return snapshot;
    }

    private LootTrackerSnapshot? RecordDetachedPublicContainerRemainingItemsCore(long sourceObjectId, DateTime nowUtc)
    {
        if (!recentDetachedLootContainers.TryGetValue(sourceObjectId, out var detachedContainer))
        {
            return null;
        }

        if (nowUtc - detachedContainer.DetachedAtUtc > DetachedPublicContainerEmptyUpdateWindow)
        {
            recentDetachedLootContainers.Remove(sourceObjectId);
            Log.Debug(
                "Loot tracker skipped remaining public container items after empty chest update because detached container was stale. SourceObjectId: {SourceObjectId}, DetachedAgeSeconds: {DetachedAgeSeconds}",
                sourceObjectId,
                (nowUtc - detachedContainer.DetachedAtUtc).TotalSeconds);
            return null;
        }

        LootTrackerSnapshot? snapshot = null;
        var recordedCount = 0;
        foreach (var itemObjectId in detachedContainer.Container.SlotItems
            .Where(id => id > 0)
            .Distinct())
        {
            var itemSnapshot = RecordUnknownLootOwnerItemObjectCore(itemObjectId, nowUtc);
            if (itemSnapshot is not null)
            {
                snapshot = itemSnapshot;
                recordedCount++;
            }
        }

        Log.Debug(
            "Loot tracker processed remaining public container items after empty chest update. SourceObjectId: {SourceObjectId}, SlotCount: {SlotCount}, RecordedCount: {RecordedCount}",
            sourceObjectId,
            detachedContainer.Container.SlotItems.Count(id => id > 0),
            recordedCount);
        recentDetachedLootContainers.Remove(sourceObjectId);
        return snapshot;
    }

    private LootTrackerSnapshot? RecordDetachedPublicContainerAfterRecentEmptyUpdateCore(long sourceObjectId, DateTime nowUtc)
    {
        if (!recentEmptyLootChestUpdates.TryGetValue(sourceObjectId, out var emptyUpdate))
        {
            return null;
        }

        if (nowUtc - emptyUpdate.SeenAtUtc > DetachedPublicContainerEmptyUpdateWindow)
        {
            recentEmptyLootChestUpdates.Remove(sourceObjectId);
            Log.Debug(
                "Loot tracker skipped remaining public container items after detach because empty chest update was stale. SourceObjectId: {SourceObjectId}, EmptyUpdateAgeSeconds: {EmptyUpdateAgeSeconds}",
                sourceObjectId,
                (nowUtc - emptyUpdate.SeenAtUtc).TotalSeconds);
            return null;
        }

        recentEmptyLootChestUpdates.Remove(sourceObjectId);
        return RecordDetachedPublicContainerRemainingItemsCore(sourceObjectId, nowUtc);
    }

    private LootTrackerSnapshot? RecordUnknownLootOwnerItemObjectCore(long itemObjectId, DateTime nowUtc)
    {
        if (recentInventoryDeletes.ContainsKey(itemObjectId)
            || recordedItemObjectIds.ContainsKey(itemObjectId)
            || !discoveredItems.TryGetValue(itemObjectId, out var item)
            || item.SourceObjectId is not { } sourceObjectId
            || !IsConfirmedLootChestSourceCore(sourceObjectId)
            || !sources.TryGetValue(sourceObjectId, out var source)
            || source.Kind != LootSourceKind.Chest)
        {
            return null;
        }

        var record = CreateRecordCore(
            "Unknown",
            source,
            itemObjectId,
            item.ItemId,
            item.Amount,
            item.Quality,
            item.UniqueName,
            item.Name,
            item.EstimatedMarketValue,
            item.BlackMarketEstimatedMarketValue,
            nowUtc);
        records.Add(record);
        recordedItemObjectIds[itemObjectId] = new RecordedItemObject(item.ItemId, item.Amount, nowUtc);
        recentInventoryDeletes[itemObjectId] = new RecentInventoryDelete(nowUtc);
        return BuildSnapshot();
    }

    private LootTrackerSnapshot? RecordLocalItemObjectCore(long itemObjectId, long sourceObjectId)
    {
        var nowUtc = DateTime.UtcNow;
        PruneTransientStateCore(nowUtc);
        if (itemObjectId <= 0)
        {
            Log.Warning(
                "Loot tracker skipped local item record because item object id was invalid. ItemObjectId: {ItemObjectId}, SourceObjectId: {SourceObjectId}",
                itemObjectId,
                sourceObjectId);
            return null;
        }

        if (!discoveredItems.TryGetValue(itemObjectId, out var item))
        {
            Log.Warning(
                "Loot tracker skipped local item record because item object id was not discovered. ItemObjectId: {ItemObjectId}, SourceObjectId: {SourceObjectId}",
                itemObjectId,
                sourceObjectId);
            return null;
        }

        if (sourceObjectId <= 0)
        {
            Log.Warning(
                "Loot tracker skipped local item record because item object id was not linked to a loot source. ItemObjectId: {ItemObjectId}, SourceObjectId: {SourceObjectId}, ItemId: {ItemId}, Amount: {Amount}",
                itemObjectId,
                sourceObjectId,
                item.ItemId,
                item.Amount);
            return null;
        }

        if (IsRecentlyRecordedCore(itemObjectId, item.ItemId, item.Amount, nowUtc))
        {
            Log.Warning(
                "Loot tracker skipped local item record because the same item object id was already recorded recently. ItemObjectId: {ItemObjectId}, SourceObjectId: {SourceObjectId}, ItemId: {ItemId}, Amount: {Amount}",
                itemObjectId,
                sourceObjectId,
                item.ItemId,
                item.Amount);
            return null;
        }

        if (!IsKnownLootSourceCore(sourceObjectId))
        {
            Log.Warning(
                "Loot tracker skipped local item record because source object id was not identified as loot. ItemObjectId: {ItemObjectId}, SourceObjectId: {SourceObjectId}, ItemId: {ItemId}, Amount: {Amount}",
                itemObjectId,
                sourceObjectId,
                item.ItemId,
                item.Amount);
            return null;
        }

        if (TryMatchCorrelation(
            recentBroadcastPickups,
            item.ItemId,
            item.Amount,
            nowUtc,
            out var broadcastCorrelation))
        {
            broadcastCorrelation.Matched = true;
            UpdateRecordItemObjectCore(broadcastCorrelation.RecordId, itemObjectId, item);
            recordedItemObjectIds[itemObjectId] = new RecordedItemObject(item.ItemId, item.Amount, nowUtc);
            return BuildSnapshot();
        }

        var playerName = GetLocalPlayerName();
        if (string.IsNullOrWhiteSpace(playerName))
        {
            Log.Warning(
                "Loot tracker skipped local item record because local player was not detected. ItemObjectId: {ItemObjectId}, SourceObjectId: {SourceObjectId}, ItemId: {ItemId}, Amount: {Amount}",
                itemObjectId,
                sourceObjectId,
                item.ItemId,
                item.Amount);
            return null;
        }

        var source = sources.TryGetValue(sourceObjectId, out var identifiedSource)
            ? identifiedSource
            : CreateSource(null, LootSourceKind.Unknown);
        var record = CreateRecordCore(
            playerName,
            source,
            itemObjectId,
            item.ItemId,
            item.Amount,
            item.Quality,
            item.UniqueName,
            item.Name,
            item.EstimatedMarketValue,
            item.BlackMarketEstimatedMarketValue,
            nowUtc);
        records.Add(record);
        recordedItemObjectIds[itemObjectId] = new RecordedItemObject(item.ItemId, item.Amount, nowUtc);
        recentLocalPickups.Add(new RecentPickupCorrelation(
            record.Id,
            item.ItemId,
            item.Amount,
            nowUtc));
        return BuildSnapshot();
    }

    private LootRecord CreateRecordCore(
        string playerName,
        LootSource source,
        long? itemObjectId,
        int itemId,
        long amount,
        int? quality,
        string? uniqueName,
        string? name,
        long? discoveredEstimatedMarketValue,
        long? discoveredBlackMarketEstimatedMarketValue,
        DateTime nowUtc)
    {
        var itemData = itemsIdsService.GetItemById(itemId);
        var resolvedQuality = quality is > 0 ? quality.Value : (int?)null;
        var resolvedUniqueName = IsKnownItemText(uniqueName)
            ? uniqueName!
            : itemData.UniqueName;
        var resolvedName = IsKnownItemText(name)
            ? name!
            : itemData.UsName;
        var serverId = playerState.AlbionServer?.Id;
        var estimatedMarketValue = GetEstimatedMarketValue(
            serverId,
            itemId,
            resolvedUniqueName,
            resolvedQuality,
            discoveredEstimatedMarketValue,
            discoveredBlackMarketEstimatedMarketValue);
        var location = playerState.Location;
        var locationName = location.FriendlyName;

        return new LootRecord(
            Guid.NewGuid(),
            nowUtc,
            playerName,
            string.Equals(playerName, "Unknown", StringComparison.OrdinalIgnoreCase)
                ? null
                : partyTracker.IsPartyMember(playerName),
            source.Kind,
            source.Name,
            serverId,
            locationName,
            itemObjectId,
            itemId,
            resolvedUniqueName,
            resolvedName,
            resolvedQuality,
            amount,
            estimatedMarketValue,
            estimatedMarketValue * amount);
    }

    private DiscoveredLootItem? FindBestDiscoveredItemCore(int itemId, long amount)
    {
        return discoveredItems.Values
            .Where(item => item.ItemId == itemId && item.Amount == amount)
            .OrderByDescending(item => item.SeenAtUtc)
            .FirstOrDefault()
            ?? discoveredItems.Values
                .Where(item => item.ItemId == itemId)
                .OrderByDescending(item => item.SeenAtUtc)
                .FirstOrDefault();
    }

    private DiscoveredLootItem? FindBestDiscoveredItemCore(long sourceObjectId, int itemId, long amount)
    {
        if (sourceObjectId <= 0)
        {
            return FindBestDiscoveredItemCore(itemId, amount);
        }

        return discoveredItems.Values
            .Where(item => item.SourceObjectId == sourceObjectId
                && item.ItemId == itemId
                && item.Amount == amount)
            .OrderByDescending(item => item.SeenAtUtc)
            .FirstOrDefault()
            ?? discoveredItems.Values
                .Where(item => item.SourceObjectId == sourceObjectId && item.ItemId == itemId)
                .OrderByDescending(item => item.SeenAtUtc)
                .FirstOrDefault()
            ?? FindBestDiscoveredItemCore(itemId, amount);
    }

    private static T GetIndexedValue<T>(IReadOnlyList<T> values, int index, T fallback)
    {
        if (values.Count == 0)
        {
            return fallback;
        }

        if (index < values.Count)
        {
            return values[index];
        }

        return values.Count == 1
            ? values[0]
            : fallback;
    }

    private void UpdateRecordSourceCore(Guid recordId, LootSource source)
    {
        var index = records.FindIndex(record => record.Id == recordId);
        if (index < 0)
        {
            return;
        }

        var record = records[index];
        records[index] = record with
        {
            SourceKind = source.Kind,
            SourceName = source.Name
        };
    }

    private void UpdateRecordItemObjectCore(Guid recordId, long itemObjectId, DiscoveredLootItem item)
    {
        var index = records.FindIndex(record => record.Id == recordId);
        if (index < 0)
        {
            return;
        }

        var record = records[index];
        var estimatedMarketValue = GetEstimatedMarketValue(
            record.ServerId,
            item.ItemId,
            IsKnownItemText(item.UniqueName) ? item.UniqueName : record.ItemUniqueName,
            item.Quality,
            item.EstimatedMarketValue,
            item.BlackMarketEstimatedMarketValue) ?? record.EstimatedMarketValue;
        records[index] = record with
        {
            ItemObjectId = itemObjectId,
            ItemUniqueName = IsKnownItemText(item.UniqueName) ? item.UniqueName : record.ItemUniqueName,
            ItemName = IsKnownItemText(item.Name) ? item.Name : record.ItemName,
            Quality = item.Quality,
            EstimatedMarketValue = estimatedMarketValue,
            TotalEstimatedMarketValue = estimatedMarketValue * record.Amount
        };
    }

    private void OnEstimatedMarketValuesChanged(IReadOnlyCollection<ItemEstimatedMarketValueKey> keys)
    {
        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            var keysByItem = keys
                .GroupBy(key => new ItemEstimatedMarketValueItemKey(key.ServerId, key.ItemId))
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(key => key.Quality).ToHashSet());
            var averageEstimatedMarketValues = new Dictionary<ItemEstimatedMarketValueItemKey, long?>();
            var changed = false;
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record.ServerId is null)
                {
                    continue;
                }

                var itemKey = new ItemEstimatedMarketValueItemKey(record.ServerId.Value, record.ItemId);
                if (!keysByItem.TryGetValue(itemKey, out var qualities))
                {
                    continue;
                }

                long? recordEstimatedMarketValue;
                if (record.Quality is { } knownQuality
                    && qualities.Contains(knownQuality))
                {
                    recordEstimatedMarketValue = GetPreferredEstimatedMarketValue(
                        itemEstimatedMarketValues.Get(
                            itemKey.ServerId,
                            itemKey.ItemId,
                            knownQuality));
                }
                else if (record.Quality is null
                    && qualities.Any(quality => quality is >= 1 and <= 4))
                {
                    if (!averageEstimatedMarketValues.TryGetValue(itemKey, out var averageEstimatedMarketValue))
                    {
                        averageEstimatedMarketValue = GetAverageEstimatedMarketValue(itemKey.ServerId, itemKey.ItemId);
                        averageEstimatedMarketValues[itemKey] = averageEstimatedMarketValue;
                    }

                    recordEstimatedMarketValue = averageEstimatedMarketValue;
                }
                else
                {
                    continue;
                }

                if (recordEstimatedMarketValue is null
                    || record.EstimatedMarketValue == recordEstimatedMarketValue)
                {
                    continue;
                }

                records[i] = record with
                {
                    EstimatedMarketValue = recordEstimatedMarketValue,
                    TotalEstimatedMarketValue = recordEstimatedMarketValue * record.Amount
                };
                changed = true;
            }

            if (changed)
            {
                snapshot = BuildSnapshot();
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private readonly record struct ItemEstimatedMarketValueItemKey(int ServerId, int ItemId);

    private long? GetEstimatedMarketValue(
        int? serverId,
        int itemId,
        string itemUniqueName,
        int? quality,
        long? discoveredEstimatedMarketValue,
        long? discoveredBlackMarketEstimatedMarketValue)
    {
        ItemEstimatedMarketValues? cachedValues = null;
        if (serverId is not null && quality is { } knownQuality)
        {
            cachedValues = itemEstimatedMarketValues.Get(serverId.Value, itemId, knownQuality);
        }

        if (discoveredBlackMarketEstimatedMarketValue is > 0)
        {
            return discoveredBlackMarketEstimatedMarketValue;
        }

        if (cachedValues?.BlackMarketEmv is > 0)
        {
            return cachedValues.Value.BlackMarketEmv;
        }

        if (discoveredEstimatedMarketValue is > 0)
        {
            return discoveredEstimatedMarketValue;
        }

        var estimatedMarketValue = cachedValues?.NormalEmv;
        if (estimatedMarketValue is null && serverId is not null && quality is null)
        {
            estimatedMarketValue = GetAverageEstimatedMarketValue(serverId.Value, itemId);
        }

        if (estimatedMarketValue is null && serverId is not null)
        {
            itemEstimatedMarketValueBackendLoader.QueueMissingEstimatedMarketValue(
                serverId.Value,
                itemId,
                itemUniqueName,
                quality);
        }

        return estimatedMarketValue;
    }

    private long? GetAverageEstimatedMarketValue(int serverId, int itemId)
    {
        var values = Enumerable.Range(1, 4)
            .Select(quality => itemEstimatedMarketValues.Get(serverId, itemId, quality))
            .Select(GetPreferredEstimatedMarketValue)
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        return (long)Math.Round(values.Average());
    }

    private static long? GetPreferredEstimatedMarketValue(ItemEstimatedMarketValues? values)
    {
        return values is { } value
            ? value.BlackMarketEmv ?? value.NormalEmv
            : null;
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.DisableLootTracker))
        {
            return;
        }

        var disabled = settingsManager.UserSettings.DisableLootTracker;
        LootTrackerSnapshot snapshot;
        lock (sync)
        {
            if (isDisabled == disabled)
            {
                return;
            }

            isDisabled = disabled;
            isPaused = false;
            records.Clear();
            ClearTransientStateCore();
            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
        Log.Information(disabled
            ? "Loot tracker disabled; all loot tracker data was reset."
            : "Loot tracker enabled.");
    }

    private bool CanCaptureCore()
    {
        return !isDisabled && !isPaused;
    }

    private bool IsLocalPlayer(string playerName)
    {
        var localPlayerName = GetLocalPlayerName();
        return !string.IsNullOrWhiteSpace(localPlayerName)
            && string.Equals(localPlayerName, playerName, StringComparison.OrdinalIgnoreCase);
    }

    private string GetLocalPlayerName()
    {
        var partyLocalName = partyTracker.CurrentSnapshot.Members
            .FirstOrDefault(member => member.IsLocalPlayer)
            ?.Name;
        if (IsKnownPlayerName(partyLocalName))
        {
            return partyLocalName!;
        }

        var playerName = playerState.PlayerName?.Trim();
        return IsKnownPlayerName(playerName)
            ? playerName!
            : string.Empty;
    }

    private static bool IsKnownPlayerName(string? playerName)
    {
        return !string.IsNullOrWhiteSpace(playerName)
            && !string.Equals(playerName, "Not set", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasLocalPlayerCore()
    {
        var partyLocalName = partyTracker.CurrentSnapshot.Members
            .FirstOrDefault(member => member.IsLocalPlayer)
            ?.Name;
        return IsKnownPlayerName(partyLocalName)
            || IsKnownPlayerName(playerState.PlayerName);
    }

    private string ResolveRecentLootChestPlayerNameCore(long sourceObjectId)
    {
        if (sourceObjectId <= 0
            || !recentLootChestUpdates.TryGetValue(sourceObjectId, out var chestUpdate))
        {
            return "Unknown";
        }

        var members = partyTracker.CurrentSnapshot.Members;
        var matchedMembers = chestUpdate.PlayerGuids
            .Select(guid => members.FirstOrDefault(member => member.UserGuid == guid))
            .Where(member => member is not null && IsKnownPlayerName(member.Name))
            .Select(member => member!)
            .DistinctBy(member => member.UserGuid)
            .ToArray();
        if (matchedMembers.Length == 1)
        {
            return matchedMembers[0].Name;
        }

        var nonLocalMembers = matchedMembers
            .Where(member => !member.IsLocalPlayer)
            .ToArray();
        return nonLocalMembers.Length == 1
            ? nonLocalMembers[0].Name
            : "Unknown";
    }

    private void OnPlayerStateChanged(object? sender, PlayerStateEventArgs e)
    {
        LootTrackerSnapshot snapshot;
        lock (sync)
        {
            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    private void OnPartySnapshotChanged(PartyTrackerSnapshot snapshot)
    {
        LootTrackerSnapshot lootSnapshot;
        lock (sync)
        {
            lootSnapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(lootSnapshot);
    }

    private static bool TryMatchCorrelation(
        IEnumerable<RecentPickupCorrelation> correlations,
        int itemId,
        long amount,
        DateTime nowUtc,
        out RecentPickupCorrelation correlation)
    {
        correlation = correlations
            .Where(candidate => !candidate.Matched
                && candidate.ItemId == itemId
                && candidate.Amount == amount
                && nowUtc - candidate.RecordedAtUtc <= CorrelationWindow)
            .OrderBy(candidate => candidate.RecordedAtUtc)
            .FirstOrDefault()!;
        return correlation is not null;
    }

    private bool IsRecentlyRecordedCore(long itemObjectId, int itemId, long amount, DateTime nowUtc)
    {
        return recordedItemObjectIds.TryGetValue(itemObjectId, out var recordedItem)
            && recordedItem.ItemId == itemId
            && recordedItem.Amount == amount
            && nowUtc - recordedItem.RecordedAtUtc <= CorrelationWindow;
    }

    private bool HasRecentRecordCore(string playerName, int itemId, long amount, DateTime nowUtc)
    {
        return records.Any(record =>
            string.Equals(record.PlayerName, playerName, StringComparison.OrdinalIgnoreCase)
            && record.ItemId == itemId
            && record.Amount == amount
            && nowUtc - record.PickedUpAtUtc <= CorrelationWindow);
    }

    private static LootSource CreateSource(string? sourceName, LootSourceKind preferredKind)
    {
        var normalizedName = sourceName?.Trim() ?? string.Empty;
        var upperName = normalizedName.ToUpperInvariant();
        var kind = preferredKind;
        if (kind == LootSourceKind.Unknown)
        {
            if (upperName is "MOB" or "@MOB" || upperName.StartsWith("@MOB", StringComparison.Ordinal))
            {
                kind = LootSourceKind.Mob;
            }
            else if (upperName.Contains("CHEST") || upperName.Contains("TREASURE"))
            {
                kind = LootSourceKind.Chest;
            }
            else if (!string.IsNullOrWhiteSpace(normalizedName))
            {
                kind = LootSourceKind.Player;
            }
        }

        var displayName = kind switch
        {
            LootSourceKind.Mob => "Mob",
            LootSourceKind.Chest when string.IsNullOrWhiteSpace(normalizedName) => "Chest",
            _ when string.IsNullOrWhiteSpace(normalizedName) => "Unknown",
            _ => normalizedName
        };
        return new LootSource(kind, displayName, DateTime.UtcNow);
    }

    private static bool IsKnownItemText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, "Unset", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("Unknown Item", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearTransientStateCore()
    {
        discoveredItems.Clear();
        sources.Clear();
        confirmedLootChestObjectIds.Clear();
        containers.Clear();
        pendingContainers.Clear();
        recentDetachedLootContainers.Clear();
        recordedItemObjectIds.Clear();
        pendingPartyLootItems.Clear();
        recentPartyLootItemTypes.Clear();
        recentLootChestUpdates.Clear();
        recentEmptyLootChestUpdates.Clear();
        recentInventoryDeletes.Clear();
        recentLocalPickups.Clear();
        recentBroadcastPickups.Clear();
        pendingLocalMovesBySlot.Clear();
    }

    private void PruneTransientStateCore(DateTime nowUtc)
    {
        foreach (var objectId in discoveredItems
            .Where(entry => nowUtc - entry.Value.SeenAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            discoveredItems.Remove(objectId);
        }

        foreach (var objectId in sources
            .Where(entry => nowUtc - entry.Value.SeenAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            sources.Remove(objectId);
            confirmedLootChestObjectIds.Remove(objectId);
        }

        foreach (var containerId in pendingContainers
            .Where(entry => nowUtc - entry.Value.SeenAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            pendingContainers.Remove(containerId);
        }

        foreach (var containerId in containers
            .Where(entry => nowUtc - entry.Value.SeenAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            containers.Remove(containerId);
        }

        foreach (var sourceObjectId in recentDetachedLootContainers
            .Where(entry => nowUtc - entry.Value.DetachedAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            recentDetachedLootContainers.Remove(sourceObjectId);
        }

        foreach (var itemObjectId in recordedItemObjectIds
            .Where(entry => nowUtc - entry.Value.RecordedAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            recordedItemObjectIds.Remove(itemObjectId);
        }

        foreach (var itemObjectId in pendingPartyLootItems
            .Where(entry => nowUtc - entry.Value.SeenAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            pendingPartyLootItems.Remove(itemObjectId);
        }

        foreach (var sourceObjectId in recentPartyLootItemTypes
            .Where(entry => entry.Value.All(itemType => nowUtc - itemType.SeenAtUtc > TransientRetention))
            .Select(entry => entry.Key)
            .ToArray())
        {
            recentPartyLootItemTypes.Remove(sourceObjectId);
        }

        foreach (var sourceObjectId in recentLootChestUpdates
            .Where(entry => nowUtc - entry.Value.SeenAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            recentLootChestUpdates.Remove(sourceObjectId);
        }

        foreach (var sourceObjectId in recentEmptyLootChestUpdates
            .Where(entry => nowUtc - entry.Value.SeenAtUtc > DetachedPublicContainerEmptyUpdateWindow)
            .Select(entry => entry.Key)
            .ToArray())
        {
            recentEmptyLootChestUpdates.Remove(sourceObjectId);
        }

        foreach (var itemObjectId in recentInventoryDeletes
            .Where(entry => nowUtc - entry.Value.RecordedAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            recentInventoryDeletes.Remove(itemObjectId);
        }

        pendingLocalMovesBySlot.RemoveAll(move =>
            nowUtc - move.SeenAtUtc > TransientRetention);
        recentLocalPickups.RemoveAll(correlation =>
            nowUtc - correlation.RecordedAtUtc > CorrelationWindow);
        recentBroadcastPickups.RemoveAll(correlation =>
            nowUtc - correlation.RecordedAtUtc > CorrelationWindow);
    }

    private LootTrackerSnapshot BuildSnapshot()
    {
        return new LootTrackerSnapshot(
            isDisabled,
            isPaused,
            HasLocalPlayerCore(),
            records.ToArray());
    }

    private sealed class DiscoveredLootItem
    {
        public DiscoveredLootItem(
            long objectId,
            int itemId,
            long amount,
            int quality,
            string uniqueName,
            string name,
            long? estimatedMarketValue,
            long? blackMarketEstimatedMarketValue,
            long? sourceObjectId,
            DateTime seenAtUtc)
        {
            ObjectId = objectId;
            ItemId = itemId;
            Amount = amount;
            Quality = quality;
            UniqueName = uniqueName;
            Name = name;
            EstimatedMarketValue = estimatedMarketValue;
            BlackMarketEstimatedMarketValue = blackMarketEstimatedMarketValue;
            SourceObjectId = sourceObjectId;
            SeenAtUtc = seenAtUtc;
        }

        public long ObjectId { get; }
        public int ItemId { get; }
        public long Amount { get; }
        public int Quality { get; }
        public string UniqueName { get; }
        public string Name { get; }
        public long? EstimatedMarketValue { get; }
        public long? BlackMarketEstimatedMarketValue { get; }
        public long? SourceObjectId { get; set; }
        public DateTime SeenAtUtc { get; }
    }

    private sealed record LootSource(
        LootSourceKind Kind,
        string Name,
        DateTime SeenAtUtc);

    private sealed record LootContainer(
        long SourceObjectId,
        Guid ContainerId,
        Guid PrivateContainerId,
        IReadOnlyList<long> SlotItems,
        DateTime SeenAtUtc);

    private sealed record PendingLootContainer(
        long SourceObjectId,
        Guid ContainerId,
        Guid PrivateContainerId,
        IReadOnlyList<long> SlotItems,
        DateTime SeenAtUtc);

    private sealed record RecentDetachedLootContainer(
        LootContainer Container,
        DateTime DetachedAtUtc);

    private sealed record RecordedItemObject(
        int ItemId,
        long Amount,
        DateTime RecordedAtUtc);

    private sealed record PendingPartyLootItem(
        long SourceObjectId,
        long ItemObjectId,
        int ItemId,
        long Amount,
        string PlayerName,
        DateTime SeenAtUtc);

    private sealed record RecentPartyLootItemType(
        int ItemId,
        long Amount,
        int? Quality,
        DateTime SeenAtUtc);

    private sealed record RecentLootChestUpdate(
        IReadOnlyList<Guid> PlayerGuids,
        DateTime SeenAtUtc);

    private sealed record RecentEmptyLootChestUpdate(DateTime SeenAtUtc);

    private sealed record RecentInventoryDelete(DateTime RecordedAtUtc);

    private sealed record PendingLocalMoveBySlot(
        int SourceSlot,
        Guid SourceContainerId,
        Guid DestinationContainerId,
        DateTime SeenAtUtc);

    private sealed class RecentPickupCorrelation
    {
        public RecentPickupCorrelation(Guid recordId, int itemId, long amount, DateTime recordedAtUtc)
        {
            RecordId = recordId;
            ItemId = itemId;
            Amount = amount;
            RecordedAtUtc = recordedAtUtc;
        }

        public Guid RecordId { get; }
        public int ItemId { get; }
        public long Amount { get; }
        public DateTime RecordedAtUtc { get; }
        public bool Matched { get; set; }
    }
}

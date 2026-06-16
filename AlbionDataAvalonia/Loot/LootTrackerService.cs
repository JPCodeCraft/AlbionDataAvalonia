using AlbionDataAvalonia.Gathering.Models;
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
    private static readonly TimeSpan TransientRetention = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromSeconds(3);

    private readonly object sync = new();
    private readonly List<LootRecord> records = new();
    private readonly Dictionary<long, DiscoveredLootItem> discoveredItems = new();
    private readonly Dictionary<long, LootSource> sources = new();
    private readonly Dictionary<Guid, LootContainer> containers = new();
    private readonly Dictionary<long, RecordedItemObject> recordedItemObjectIds = new();
    private readonly List<RecentPickupCorrelation> recentLocalPickups = new();
    private readonly List<RecentPickupCorrelation> recentBroadcastPickups = new();
    private readonly SettingsManager settingsManager;
    private readonly PartyTrackerService partyTracker;
    private readonly ItemsIdsService itemsIdsService;
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly PlayerState playerState;

    private bool isDisabled;
    private bool isPaused;

    public event Action<LootTrackerSnapshot>? SnapshotChanged;

    public LootTrackerService(
        SettingsManager settingsManager,
        PartyTrackerService partyTracker,
        ItemsIdsService itemsIdsService,
        ItemEstimatedMarketValueService itemEstimatedMarketValues,
        PlayerState playerState)
    {
        this.settingsManager = settingsManager;
        this.partyTracker = partyTracker;
        this.itemsIdsService = itemsIdsService;
        this.itemEstimatedMarketValues = itemEstimatedMarketValues;
        this.playerState = playerState;

        isDisabled = settingsManager.UserSettings.DisableLootTracker;
        settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        itemEstimatedMarketValues.EstimatedMarketValueChanged += OnEstimatedMarketValueChanged;
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
        itemEstimatedMarketValues.EstimatedMarketValueChanged -= OnEstimatedMarketValueChanged;
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
            records.Clear();
            ClearTransientStateCore();
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

        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            sources[objectId] = CreateSource(sourceName, LootSourceKind.Unknown);
            PruneTransientStateCore(DateTime.UtcNow);
        }
    }

    public void IdentifyLootChest(long objectId, string? uniqueName, string? uniqueNameWithLocation)
    {
        if (objectId <= 0)
        {
            return;
        }

        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var name = !string.IsNullOrWhiteSpace(uniqueName)
                ? uniqueName
                : uniqueNameWithLocation;
            sources[objectId] = CreateSource(name, LootSourceKind.Chest);
            PruneTransientStateCore(DateTime.UtcNow);
        }
    }

    public void AttachContainer(long objectId, Guid containerId, Guid privateContainerId, IReadOnlyList<long> slotItems)
    {
        if (objectId <= 0 || containerId == Guid.Empty)
        {
            return;
        }

        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

            var container = new LootContainer(
                objectId,
                containerId,
                privateContainerId,
                slotItems.ToArray(),
                DateTime.UtcNow);
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

            PruneTransientStateCore(DateTime.UtcNow);
        }
    }

    public void DiscoverItem(NewItem item)
    {
        if (item.ObjectId is not { } objectId || objectId <= 0 || item.ItemIndex <= 0)
        {
            return;
        }

        lock (sync)
        {
            if (!CanCaptureCore())
            {
                return;
            }

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
                sourceObjectId,
                DateTime.UtcNow);
            PruneTransientStateCore(DateTime.UtcNow);
        }
    }

    public void RecordOtherPickup(
        string? sourceName,
        string? playerName,
        bool isSilver,
        int itemId,
        int amount)
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
                    item?.Quality ?? 1,
                    item?.UniqueName,
                    item?.Name,
                    item?.EstimatedMarketValue,
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
                Log.Warning(
                    "Loot tracker skipped local move by slot because source container was not known. SourceSlot: {SourceSlot}, SourceContainerId: {SourceContainerId}, DestinationContainerId: {DestinationContainerId}",
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
                    "Loot tracker will record local move given items with unknown source because source container id is empty. ItemObjectIds: {ItemObjectIds}, DestinationContainerId: {DestinationContainerId}",
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
                    "Loot tracker will record local move given items from discovered item data because source container was not known. ItemObjectIds: {ItemObjectIds}, SourceContainerId: {SourceContainerId}, DestinationContainerId: {DestinationContainerId}",
                    itemObjectIds,
                    sourceContainerId,
                    destinationContainerId);
            }

            var containerItems = container?.SlotItems.ToHashSet();
            foreach (var itemObjectId in itemObjectIds.Distinct())
            {
                var sourceObjectId = container?.SourceObjectId ?? 0;
                if (discoveredItems.TryGetValue(itemObjectId, out var discoveredItem)
                    && discoveredItem.SourceObjectId is { } discoveredSourceObjectId)
                {
                    sourceObjectId = discoveredSourceObjectId;
                }

                if (containerItems is not null && !containerItems.Contains(itemObjectId))
                {
                    Log.Warning(
                        "Loot tracker will record local move given item from discovered item data because item object id was not in the source container. ItemObjectId: {ItemObjectId}, SourceContainerId: {SourceContainerId}, SourceObjectId: {SourceObjectId}",
                        itemObjectId,
                        sourceContainerId,
                        sourceObjectId);
                }

                snapshot = RecordLocalItemObjectCore(itemObjectId, sourceObjectId) ?? snapshot;
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
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

        if (recordedItemObjectIds.TryGetValue(itemObjectId, out var recordedItem)
            && recordedItem.ItemId == item.ItemId
            && recordedItem.Amount == item.Amount
            && nowUtc - recordedItem.RecordedAtUtc <= CorrelationWindow)
        {
            Log.Warning(
                "Loot tracker skipped local item record because the same item object id was already recorded recently. ItemObjectId: {ItemObjectId}, SourceObjectId: {SourceObjectId}, ItemId: {ItemId}, Amount: {Amount}",
                itemObjectId,
                sourceObjectId,
                item.ItemId,
                item.Amount);
            return null;
        }

        if (!sources.ContainsKey(sourceObjectId))
        {
            Log.Warning(
                "Loot tracker will record local item with unknown source because source object id was not identified. ItemObjectId: {ItemObjectId}, SourceObjectId: {SourceObjectId}, ItemId: {ItemId}, Amount: {Amount}",
                itemObjectId,
                sourceObjectId,
                item.ItemId,
                item.Amount);
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
        int amount,
        int quality,
        string? uniqueName,
        string? name,
        long? discoveredEstimatedMarketValue,
        DateTime nowUtc)
    {
        var itemData = itemsIdsService.GetItemById(itemId);
        var resolvedQuality = Math.Max(1, quality);
        var estimatedMarketValue = discoveredEstimatedMarketValue is > 0
            ? discoveredEstimatedMarketValue
            : itemEstimatedMarketValues.Get(itemId, resolvedQuality);
        var resolvedUniqueName = IsKnownItemText(uniqueName)
            ? uniqueName!
            : itemData.UniqueName;
        var resolvedName = IsKnownItemText(name)
            ? name!
            : itemData.UsName;
        var location = playerState.Location;
        var locationName = location.FriendlyName;

        return new LootRecord(
            Guid.NewGuid(),
            nowUtc,
            playerName,
            partyTracker.IsPartyMember(playerName),
            source.Kind,
            source.Name,
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

    private DiscoveredLootItem? FindBestDiscoveredItemCore(int itemId, int amount)
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
        var estimatedMarketValue = record.EstimatedMarketValue
            ?? item.EstimatedMarketValue
            ?? itemEstimatedMarketValues.Get(item.ItemId, item.Quality);
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

    private void OnEstimatedMarketValueChanged(GatheringItemKey key)
    {
        LootTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            var estimatedMarketValue = itemEstimatedMarketValues.Get(key.ItemId, key.Quality);
            if (estimatedMarketValue is not > 0)
            {
                return;
            }

            var changed = false;
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record.ItemId != key.ItemId
                    || record.Quality != key.Quality
                    || record.EstimatedMarketValue is not null)
                {
                    continue;
                }

                records[i] = record with
                {
                    EstimatedMarketValue = estimatedMarketValue,
                    TotalEstimatedMarketValue = estimatedMarketValue * record.Amount
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
        int amount,
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
        containers.Clear();
        recordedItemObjectIds.Clear();
        recentLocalPickups.Clear();
        recentBroadcastPickups.Clear();
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
        }

        foreach (var containerId in containers
            .Where(entry => nowUtc - entry.Value.SeenAtUtc > TransientRetention)
            .Select(entry => entry.Key)
            .ToArray())
        {
            containers.Remove(containerId);
        }

        foreach (var itemObjectId in recordedItemObjectIds
            .Where(entry => nowUtc - entry.Value.RecordedAtUtc > CorrelationWindow)
            .Select(entry => entry.Key)
            .ToArray())
        {
            recordedItemObjectIds.Remove(itemObjectId);
        }

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
            int amount,
            int quality,
            string uniqueName,
            string name,
            long? estimatedMarketValue,
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
            SourceObjectId = sourceObjectId;
            SeenAtUtc = seenAtUtc;
        }

        public long ObjectId { get; }
        public int ItemId { get; }
        public int Amount { get; }
        public int Quality { get; }
        public string UniqueName { get; }
        public string Name { get; }
        public long? EstimatedMarketValue { get; }
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

    private sealed record RecordedItemObject(
        int ItemId,
        int Amount,
        DateTime RecordedAtUtc);

    private sealed class RecentPickupCorrelation
    {
        public RecentPickupCorrelation(Guid recordId, int itemId, int amount, DateTime recordedAtUtc)
        {
            RecordId = recordId;
            ItemId = itemId;
            Amount = amount;
            RecordedAtUtc = recordedAtUtc;
        }

        public Guid RecordId { get; }
        public int ItemId { get; }
        public int Amount { get; }
        public DateTime RecordedAtUtc { get; }
        public bool Matched { get; set; }
    }
}

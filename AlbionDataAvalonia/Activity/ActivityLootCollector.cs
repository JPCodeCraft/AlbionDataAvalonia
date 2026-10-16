using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

/// <summary>Bounded correlations for loot sources and the captured character's inventory.</summary>
internal sealed partial class ActivityLootCollector : IDisposable
{
    private const int EmptyLootChestState = 8;
    private static readonly TimeSpan MetadataLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan EmptyChestWindow = TimeSpan.FromSeconds(30);
    private readonly ActivityTrackerService tracker;
    private readonly ItemEstimatedMarketValueService values;
    private readonly ItemsIdsService itemNames;
    private readonly Dictionary<long, Item> items = new();
    private readonly Dictionary<PendingInventoryItemKey, Item> pendingInventoryMetadata = new();
    private readonly Dictionary<long, Source> sources = new();
    private readonly Dictionary<Guid, Container> containers = new();
    private readonly Dictionary<long, Guid> membership = new();
    private readonly List<Candidate> candidates = new();
    private readonly List<Gain> gains = new();
    private readonly Dictionary<long, LocalIntroduction> localIntroductions = new();
    private readonly List<ResourceClaim> resourceClaims = new();
    private readonly List<RecentPickup> recent = new();
    // Party chests name the player allowed to take each item and, for type-only
    // removals, the players allowed to open the chest.
    private readonly Dictionary<long, Allocation> allocations = new();
    private readonly Dictionary<long, ChestRecipients> chestRecipients = new();
    // Public chest contents seen before closing; items missing later were taken by someone.
    private readonly Dictionary<long, DetachedChest> detachedChests = new();
    private readonly Dictionary<long, DateTime> emptyChests = new();
    private readonly Dictionary<long, RecordedObject> recordedObjects = new();
    private DateTime nonLootMoveUntil;
    private Guid? inventory;
    private DateTime lastCacheSweep;

    public ActivityLootCollector(ActivityTrackerService tracker, ItemEstimatedMarketValueService values, ItemsIdsService itemNames)
    {
        this.tracker = tracker;
        this.values = values;
        this.itemNames = itemNames;
        values.EstimatedMarketValuesChanged += PricesChanged;
    }

    public void Join(Guid? containerId, IReadOnlyList<long> existingItems)
    {
        Reset();
        inventory = containerId;
        if (inventory is { } id && id != Guid.Empty)
            foreach (var objectId in existingItems.Where(x => x > 0)) membership[objectId] = id;
    }

    public void Join(JoinResponse packet, DateTime at)
    {
        // Inventory item descriptions can precede Join in the same server burst.
        // Retain only fresh descriptions of identities confirmed by this Join.
        var baselines = packet.MainInventoryItemObjectIds.Where(x => x > 0).Distinct()
            .Select(objectId => pendingInventoryMetadata.GetValueOrDefault(PendingKey(packet.ConnectionId, packet.MessageIdentity, objectId)))
            .Where(item => item is not null && item.LastSeen <= at && at - item.LastSeen <= TimeSpan.FromSeconds(10))
            .Cast<Item>().ToArray();
        Join(packet.MainInventoryContainerId, packet.MainInventoryItemObjectIds);
        foreach (var baseline in baselines) items[baseline.ObjectId] = baseline;
    }

    internal void ObservePendingInventoryMetadata(BaseEvent packet, DateTime at)
    {
        var value = packet switch
        {
            NewSimpleItemEvent item => item.Item,
            NewEquipmentItemEvent item => item.Item,
            NewFurnitureItemEvent item => item.Item,
            NewJournalItemEvent item => item.Item,
            NewLaborerItemEvent item => item.Item,
            NewKillTrophyItemEvent item => item.Item,
            NewSiegeBannerItemEvent item => item.Item,
            _ => null
        };
        if (value is null || value.ObjectId is not > 0 || value.ItemIndex <= 0) return;
        var key = PendingKey(packet.ConnectionId, packet.MessageIdentity, value.ObjectId.Value);
        var previous = pendingInventoryMetadata.GetValueOrDefault(key);
        if (previous is not null && previous.LastSeen > at) return;
        if (previous is null && pendingInventoryMetadata.Count >= 4096) return;
        var sameItem = previous?.Id == value.ItemIndex ? previous : null;
        var validatedSimple = packet is NewSimpleItemEvent { HasValidAcquisitionMetadata: true };
        pendingInventoryMetadata[key] = new Item(value.ObjectId.Value, value.ItemIndex,
            value.Quality > 0 ? value.Quality : sameItem?.Quality,
            value.Quantity > 0 ? value.Quantity : sameItem?.Quantity ?? 0,
            value.ItemUniqueName == "Unset" ? sameItem?.UniqueName : value.ItemUniqueName,
            value.ItemUsName == "Unset" ? sameItem?.Name : value.ItemUsName, at,
            value.Quantity > 0 ? at : sameItem?.QuantitySeenAt ?? DateTime.MinValue, validatedSimple,
            Positive(value.EstimatedMarketValue) ?? sameItem?.Emv,
            Positive(value.BlackMarketEstimatedMarketValue) ?? sameItem?.BlackMarketEmv);
    }

    private static PendingInventoryItemKey PendingKey(string connection, PhotonMessageIdentity? identity, long objectId)
        => new(connection, identity?.ReceiverGeneration, identity?.PeerId, identity?.Challenge, objectId);

    public void Observe(BaseOperation packet, DateTime at, bool record = true)
    {
        if (inventory is not { } main) return;
        switch (packet)
        {
            case InventoryMoveItemRequest move when AcceptsAcquisition(move.DestinationContainerId, at):
                if (TryContainer(move.SourceContainerId, at, out var container)
                    && move.SourceSlot >= 0 && move.SourceSlot < container.Slots.Length && TrySource(container.SourceId, at, out _))
                {
                    if (record) Intent(container.Slots[move.SourceSlot], container, null, move.DestinationContainerId, at);
                }
                else if (move.DestinationContainerId == main) nonLootMoveUntil = at.AddSeconds(10);
                break;
            case InventoryMoveGivenItemsRequest move when AcceptsAcquisition(move.DestinationContainerId, at):
                var given = TryContainer(move.SourceContainerId, at, out var named) && TrySource(named.SourceId, at, out _) ? named : null;
                var linked = false;
                for (var index = 0; index < move.ItemObjectIds.Count; index++)
                {
                    // An omitted source container is resolved through each item's known membership.
                    var objectId = move.ItemObjectIds[index];
                    var from = given ?? (TryContainer(Membership(objectId, at), at, out var member)
                        && TrySource(member.SourceId, at, out _) ? member : null);
                    if (from is null) continue;
                    linked = true;
                    if (record && candidates.Count < 256)
                        Intent(objectId, from, move.Quantities is { } amounts && index < amounts.Count ? amounts[index] : null,
                            move.DestinationContainerId, at);
                }
                if (!linked && move.DestinationContainerId == main) nonLootMoveUntil = at.AddSeconds(10);
                break;
        }
    }

    public void Observe(BaseEvent packet, DateTime at, bool record = true)
    {
        switch (packet)
        {
            case NewLootEvent loot when loot.ObjectId > 0:
                IdentifySource(loot.ObjectId, loot.SourceName, SourceKind(loot.SourceName), at);
                break;
            case NewLootChestEvent chest when chest.ObjectId > 0:
                IdentifySource(chest.ObjectId,
                    string.IsNullOrWhiteSpace(chest.UniqueNameWithLocation) ? chest.UniqueName : chest.UniqueNameWithLocation, "Chest", at);
                break;
            case LootChestOpenedEvent opened when opened.ObjectId > 0:
                IdentifySource(opened.ObjectId, null, "Chest", at);
                break;
            case UpdateLootChestEvent updated when updated.ObjectId > 0:
                IdentifySource(updated.ObjectId, null, "Chest", at);
                // Players who still have reserved loot name who can take the next removed stack.
                var recipients = updated.RemainingLootGuids.Count > 0 ? updated.RemainingLootGuids : updated.PlayerGuids;
                if (recipients.Count > 0 && (chestRecipients.Count < 1024 || chestRecipients.ContainsKey(updated.ObjectId)))
                    chestRecipients[updated.ObjectId] = new ChestRecipients(recipients.ToArray(), at);
                RecordReservedPickups(partyChests.ObserveUpdate(updated, tracker.LocalActor.CharacterId, at), at, packet, record);
                if (record && updated.State == EmptyLootChestState)
                {
                    emptyChests[updated.ObjectId] = at;
                    GuessRemaining(updated.ObjectId, at, packet);
                }
                break;
            case AttachItemContainerEvent attach:
                Attach(attach, at, record);
                break;
            case DetachItemContainerEvent detach:
                // Detachment alone is visibility evidence. A public chest emptied around
                // the same time proves that its remaining items were taken.
                if (TryContainer(detach.ContainerId, at, out var detached))
                {
                    detached.LastSeen = at;
                    partyChests.ObserveClosed(detached.SourceId);
                    if (record && detached.IsPublic && TrySource(detached.SourceId, at, out var chest) && chest.Kind == "Chest")
                    {
                        detachedChests[detached.SourceId] = new DetachedChest(detached.Slots.Where(x => x > 0).Distinct().ToArray(), at);
                        if (emptyChests.Remove(detached.SourceId, out var emptied) && at - emptied <= EmptyChestWindow)
                            GuessRemaining(detached.SourceId, at, packet);
                    }
                }
                break;
            case InventoryStateEvent state when state.ContainerId is { } id:
                var observedItems = state.ItemObjectIds.Where(x => x > 0).ToHashSet();
                if (!record)
                    foreach (var missing in membership.Where(x => x.Value == id && !observedItems.Contains(x.Key)).Select(x => x.Key).ToArray())
                        ForgetMembership(missing);
                for (var index = 0; index < state.ItemObjectIds.Count; index++)
                    if (state.ItemObjectIds[index] > 0)
                        Put(state.ItemObjectIds[index], id, at, ActivityTrackerService.Origin(packet, "InventoryState", index), record);
                break;
            case InventoryPutItemEvent put when put.ItemObjectId > 0 && put.ContainerId != Guid.Empty:
                Put(put.ItemObjectId, put.ContainerId, at, ActivityTrackerService.Origin(packet, "InventoryPut"), record);
                break;
            case InventoryDeleteItemEvent deleted:
                Removed(deleted.ItemObjectId, at, ActivityTrackerService.Origin(packet, "InventoryDelete"), record: record);
                break;
            case PartyLootItemsEvent party:
                IdentifySource(party.SourceObjectId, null, "Chest", at);
                RecordReservedPickups(partyChests.ObserveList(party, at), at, packet, record);
                for (var index = 0; index < party.ItemObjectIds.Count; index++)
                {
                    if (index >= party.ItemIds.Count || index >= party.Amounts.Count || party.Amounts[index] <= 0) continue;
                    var objectId = party.ItemObjectIds[index];
                    // Stacks reserved for other members carry no object ID; the ledger tracks them by type.
                    if (objectId <= 0) continue;
                    var quality = index < party.Qualities.Count && party.Qualities[index] > 0 ? party.Qualities[index] : (int?)null;
                    Metadata(objectId, party.ItemIds[index], quality, party.Amounts[index], null, null, at, record: record);
                    if (TrySource(party.SourceObjectId, at, out _))
                        foreach (var container in containers.Values.Where(x => x.SourceId == party.SourceObjectId && at - x.LastSeen <= MetadataLifetime))
                            membership[objectId] = container.Id;
                    // The allocation names who may take the item; its removal from this list is that player's pickup.
                    var recipient = index < party.PlayerNames.Count ? party.PlayerNames[index]?.Trim() : null;
                    if (objectId > 0 && !string.IsNullOrWhiteSpace(recipient) && (allocations.Count < 4096 || allocations.ContainsKey(objectId)))
                        allocations[objectId] = new Allocation(party.SourceObjectId, recipient, at);
                }
                break;
            case PartyLootItemsRemovedEvent removed:
                IdentifySource(removed.SourceObjectId, null, "Chest", at);
                for (var index = 0; index < removed.ItemObjectIds.Count; index++)
                {
                    var objectId = removed.ItemObjectIds[index];
                    var origin = ActivityTrackerService.Origin(packet, "PartyRemoval", index);
                    if (allocations.Remove(objectId, out var allocation) && record && at - allocation.At <= MetadataLifetime)
                        Allocated(objectId, allocation, removed.SourceObjectId, at, origin);
                    else Removed(objectId, at, origin, removed.SourceObjectId, record);
                }
                break;
            case PartyLootItemTypesRemovedEvent removed:
                IdentifySource(removed.SourceObjectId, null, "Chest", at);
                if (!record) break;
                var typeRecipient = chestRecipients.TryGetValue(removed.SourceObjectId, out var allowed) && at - allowed.At <= MetadataLifetime
                    ? tracker.ResolvePartyRecipient(allowed.PlayerGuids) : null;
                for (var index = 0; index < removed.ItemIds.Count; index++)
                {
                    var amount = IndexedAmount(removed.Amounts, index);
                    TypeRemoval(removed.SourceObjectId, removed.ItemIds[index], amount, typeRecipient, at,
                        ActivityTrackerService.Origin(packet, "TypeRemoval", index));
                    partyChests.ObserveTypeRemoval(removed.SourceObjectId, removed.ItemIds[index], amount);
                }
                break;
            case OtherGrabbedLootEvent pickup:
                if (record) Broadcast(pickup, at);
                break;
            case NewSimpleItemEvent item when item.ObjectIdState == PacketFieldState.Valid && item.ItemIdState == PacketFieldState.Valid:
                if (item.QuantityState == PacketFieldState.Invalid && item.Item?.ObjectId is { } invalidQuantityObjectId
                    && items.TryGetValue(invalidQuantityObjectId, out var previousItem))
                    items[invalidQuantityObjectId] = previousItem with { QuantitySeenAt = DateTime.MinValue, ValidAcquisitionQuantity = false };
                NewItem(item.Item, at, item.HasValidAcquisitionMetadata, record);
                break;
            case NewEquipmentItemEvent item: NewItem(item.Item, at, record: record); break;
            case NewFurnitureItemEvent item: NewItem(item.Item, at, record: record); break;
            case NewJournalItemEvent item: NewItem(item.Item, at, record: record); break;
            case NewLaborerItemEvent item: NewItem(item.Item, at, record: record); break;
            case NewKillTrophyItemEvent item: NewItem(item.Item, at, record: record); break;
            case NewSiegeBannerItemEvent item: NewItem(item.Item, at, record: record); break;
        }
        if (record) ConfirmUniqueGains(at);
    }

    private void IdentifySource(long objectId, string? name, string kind, DateTime at)
    {
        if (objectId <= 0) return;
        var previous = TrySource(objectId, at, out var known) ? known : null;
        sources[objectId] = new Source(objectId,
            string.IsNullOrWhiteSpace(name) ? previous?.Name ?? (kind == "Chest" ? "Chest" : "Unknown") : name,
            kind == "Unknown" || string.IsNullOrWhiteSpace(name) && previous?.Kind is "Mob" or "Player"
                ? previous?.Kind ?? kind : kind, at);
    }

    private static string SourceKind(string? name)
    {
        var value = name?.Trim().ToUpperInvariant() ?? "";
        return value.StartsWith("@MOB", StringComparison.Ordinal) || value == "MOB" ? "Mob"
            : value.Contains("CHEST", StringComparison.Ordinal) || value.Contains("TREASURE", StringComparison.Ordinal) ? "Chest"
            : value.Length == 0 ? "Unknown" : "Player";
    }

    private void Attach(AttachItemContainerEvent packet, DateTime at, bool record)
    {
        var isPublic = packet.PrivateContainerId == Guid.Empty;
        if (record && isPublic && detachedChests.Remove(packet.ObjectId, out var closed) && at - closed.At <= MetadataLifetime)
        {
            var present = packet.SlotItems.Where(x => x > 0).ToHashSet();
            GuessTaken(packet.ObjectId, closed.Items.Where(x => !present.Contains(x)), at, packet);
        }
        foreach (var id in new[] { packet.ContainerId, packet.PrivateContainerId }.Where(x => x != Guid.Empty).Distinct())
        {
            var observedItems = packet.SlotItems.Where(x => x > 0).ToHashSet();
            if (!record)
                foreach (var missing in membership.Where(x => x.Value == id && !observedItems.Contains(x.Key)).Select(x => x.Key).ToArray())
                    ForgetMembership(missing);
            containers[id] = new Container(id, packet.ObjectId, packet.SlotItems.ToArray(), at, isPublic);
            foreach (var objectId in packet.SlotItems.Where(x => x > 0)) membership[objectId] = id;
        }
    }

    // The main inventory, or any other container that is not a known loot source
    // (bags, equipment), can receive the captured character's own pickups.
    private bool AcceptsAcquisition(Guid destination, DateTime at) => destination != Guid.Empty
        && (destination == inventory || !(TryContainer(destination, at, out var container) && TrySource(container.SourceId, at, out _)));

    private void Intent(long objectId, Container container, long? quantity, Guid destination, DateTime at)
    {
        if (objectId <= 0 || inventory == container.Id || !TrySource(container.SourceId, at, out var source)
            || candidates.Count >= 256) return;
        if (!container.Slots.Contains(objectId) && !(TryContainer(Membership(objectId, at), at, out var known)
            && known.SourceId == container.SourceId)) return;
        candidates.RemoveAll(x => x.ObjectId == objectId);
        candidates.Add(new Candidate(objectId, container.Id, source, ItemSnapshot(objectId, at), quantity, at, tracker.LocalActor,
            hasMoveIntent: true)
        { Destination = destination });
    }

    private Item ItemSnapshot(long objectId, DateTime at) => GetItem(objectId, at)
        ?? new Item(objectId, 0, null, 0, null, null, at, DateTime.MinValue);

    // Lookups enforce the original TTL even between broad maintenance sweeps. Stacks in the
    // main inventory stay known, so a later merge into an old stack is still seen as a gain.
    private Item? GetItem(long objectId, DateTime at) => items.TryGetValue(objectId, out var item)
        && (at - item.LastSeen <= MetadataLifetime || InMainInventory(objectId)) ? item : null;
    private bool InMainInventory(long objectId) => inventory is { } main
        && membership.TryGetValue(objectId, out var bound) && bound == main;
    private bool TryContainer(Guid id, DateTime at, out Container container) => containers.TryGetValue(id, out container!)
        && at - container.LastSeen <= MetadataLifetime;
    private bool TrySource(long id, DateTime at, out Source source) => sources.TryGetValue(id, out source!)
        && at - source.LastSeen <= MetadataLifetime;
    private Guid Membership(long objectId, DateTime at)
    {
        var bound = membership.GetValueOrDefault(objectId);
        if (bound != inventory && !TryContainer(bound, at, out _))
        { membership.Remove(objectId); return Guid.Empty; }
        return bound;
    }

    private void NewItem(NewItem? item, DateTime at, bool? validatedSimple = null, bool record = true)
    {
        if (item is null || item.ObjectId is not > 0 || item.ItemIndex <= 0) return;
        Metadata(item.ObjectId.Value, item.ItemIndex, item.Quality > 0 ? item.Quality : null,
            item.Quantity > 0 && validatedSimple != false ? item.Quantity : null,
            item.ItemUniqueName == "Unset" ? null : item.ItemUniqueName,
            item.ItemUsName == "Unset" ? null : item.ItemUsName, at, validatedSimple == true, record,
            Positive(item.EstimatedMarketValue), Positive(item.BlackMarketEstimatedMarketValue));
    }

    private static long? Positive(long? value) => value is > 0 ? value : null;

    private void Metadata(long objectId, int itemId, int? quality, long? quantity, string? uniqueName, string? name, DateTime at,
        bool validatedSimple = false, bool record = true, long? emv = null, long? blackMarketEmv = null)
    {
        var old = GetItem(objectId, at);
        var sameItem = old?.Id == itemId ? old : null;
        var next = new Item(objectId, itemId, quality ?? old?.Quality, quantity ?? old?.Quantity ?? 0,
            uniqueName ?? old?.UniqueName, name ?? old?.Name, at,
            quantity is > 0 ? at : old?.QuantitySeenAt ?? DateTime.MinValue,
            quantity is > 0 ? validatedSimple : old?.ValidAcquisitionQuantity ?? false,
            emv ?? sameItem?.Emv, blackMarketEmv ?? sameItem?.BlackMarketEmv);
        items[objectId] = next;
        if (!record) return;
        // Gains during inventory rearrangement still confirm loot, but cannot prove fishing rewards.
        var disrupted = at <= nonLootMoveUntil;
        if (Membership(objectId, at) == inventory && quantity is > 0 && validatedSimple
            && localIntroductions.Remove(objectId, out var introduction) && at - introduction.At <= TimeSpan.FromSeconds(2))
            AddGain(new Gain(objectId, quantity.Value, next, at, introduction.Context, true) { Disrupted = disrupted || introduction.Disrupted });
        else if (Membership(objectId, at) == inventory && old is not null
            && old.Id == itemId && next.Quantity > old.Quantity)
            AddGain(new Gain(objectId, next.Quantity - old.Quantity, next, at, tracker.Stamp(ActivityEventKind.Loot, at, null),
                next.ValidAcquisitionQuantity && old.ValidAcquisitionQuantity)
            { Disrupted = disrupted });
        if (quantity is { } observedQuantity)
            foreach (var candidate in candidates.Where(x => x.ObjectId == objectId && x.Baseline.Quantity > observedQuantity))
            {
                candidate.Decrement = candidate.Baseline.Quantity - observedQuantity;
                if (candidate.HasMoveIntent && !candidate.Confirmed && candidate.RemovedAt is null)
                {
                    candidate.RemovedAt = at;
                    candidate.Removal = tracker.Stamp(ActivityEventKind.Loot, at, null);
                }
            }
        foreach (var candidate in candidates.Where(x => x.ObjectId == objectId && x.Transfer is not null && !x.Confirmed).ToArray())
        {
            if (quantity is > 0 && at >= candidate.CreatedAt)
                Confirm(candidate, next, quantity.Value, candidate.Transfer!.CapturedAtUtc, candidate.Transfer.OriginKey, "ExactObjectTransfer");
            else
            {
                var amended = Enrich(candidate.Transfer!, next);
                if (amended != candidate.Transfer)
                    candidate.Transfer = tracker.AmendLoot(amended with { Revision = candidate.Transfer!.Revision + 1 });
            }
        }
        foreach (var pickup in recent.Where(x => x.ObjectId == objectId && x.Event.ItemId == itemId
            && at - x.At <= TimeSpan.FromMinutes(2)).ToArray())
        {
            var amended = Enrich(pickup.Event, next);
            if (amended != pickup.Event)
                pickup.Event = tracker.AmendLoot(amended with { Revision = pickup.Event.Revision + 1 });
        }
    }

    private void Put(long objectId, Guid destination, DateTime at, string? origin, bool record = true)
    {
        var previous = Membership(objectId, at);
        if (previous != destination) ForgetMembership(objectId);
        if (!record) { membership[objectId] = destination; return; }
        var intoInventory = destination == inventory;
        // A pickup into a bag or equipment is proven by our own request naming that destination.
        var requested = !intoInventory && candidates.Any(x => x.ObjectId == objectId && !x.Confirmed && x.Destination == destination);
        if (!intoInventory && !requested)
            candidates.RemoveAll(x => x.ObjectId == objectId &&
                (!TryContainer(destination, at, out var next) || next.SourceId != x.Source.Id));
        if (intoInventory || requested)
        {
            if (intoInventory && previous == Guid.Empty && localIntroductions.Count < 256)
                localIntroductions[objectId] = new LocalIntroduction(at, tracker.Stamp(ActivityEventKind.Loot, at, origin), at <= nonLootMoveUntil);
            var matches = candidates.Where(x => x.ObjectId == objectId && !x.Confirmed && at - x.CreatedAt <= TimeSpan.FromSeconds(10)
                && (intoInventory || x.Destination == destination)).ToArray();
            if (intoInventory && matches.Length == 0 && previous != inventory && candidates.Count < 256
                && !WasRecordedAsPickup(objectId, at)
                && TryContainer(previous, at, out var sourceContainer) && TrySource(sourceContainer.SourceId, at, out var source))
            {
                // Server membership can prove the transfer even when its request was not captured.
                var candidate = new Candidate(objectId, previous, source, ItemSnapshot(objectId, at), null, at, tracker.LocalActor);
                candidates.Add(candidate);
                matches = [candidate];
            }
            if (matches.Length == 1 && previous != destination && (previous == matches[0].ContainerId
                    || TryContainer(previous, at, out var sameSource) && sameSource.SourceId == matches[0].Source.Id
                    || matches[0].Removed))
            {
                // Server membership proves this same item object moved into a local container.
                // An unchanged whole stack need not receive another quantity packet after the move.
                var candidate = matches[0];
                var item = ItemSnapshot(objectId, at);
                var transfer = ActivityTrackerService.WithSource(tracker.Stamp(ActivityEventKind.Loot, at, origin), tracker.LocalActor) with
                {
                    Evidence = ActivityEvidence.Supplemental,
                    ItemUniqueName = item.UniqueName,
                    ItemName = item.Name ?? "Unknown item",
                    Quality = item.Quality,
                    Quantity = 0,
                    LootSourceName = candidate.Source.Name,
                    LootSourceKind = candidate.Source.Kind,
                    DetailsJson = JsonSerializer.Serialize(new
                    { Proof = "ExactObjectTransferAwaitingQuantity", HasConfirmedPickupEvidence = true, SourceObjectId = candidate.Source.Id, ItemObjectId = objectId })
                };
                if (item.Id > 0) transfer = Describe(transfer with { ItemName = item.Name ?? "" }, item.Id);
                candidate.Transfer = tracker.RecordLoot(transfer, hasConfirmedPickupEvidence: true);
                MarkRecorded(objectId, at);
                var wholeStack = candidate.IntentQuantity is null || candidate.IntentQuantity >= item.Quantity;
                if (item.QuantitySeenAt >= candidate.CreatedAt
                    || wholeStack && at - item.QuantitySeenAt <= MetadataLifetime)
                    Confirm(candidate, item, item.Quantity, at, origin, "ExactObjectTransfer");
            }
            else if (intoInventory && previous != destination
                && (previous == Guid.Empty || TryContainer(previous, at, out var from) && TrySource(from.SourceId, at, out _))
                && GetItem(objectId, at) is { } newItem)
            {
                AddGain(new Gain(objectId, newItem.Quantity, newItem, at, tracker.Stamp(ActivityEventKind.Loot, at, origin),
                    newItem.ValidAcquisitionQuantity && at - newItem.QuantitySeenAt <= TimeSpan.FromSeconds(2))
                { Disrupted = at <= nonLootMoveUntil });
                if (newItem.ValidAcquisitionQuantity && newItem.Quantity > 0) localIntroductions.Remove(objectId);
            }
        }
        membership[objectId] = destination;
    }

    private Candidate? Removed(long objectId, DateTime at, string? origin, long? sourceId = null, bool record = true,
        bool inferUnknown = true)
    {
        if (!record) { ForgetMembership(objectId); return null; }
        var bound = Membership(objectId, at);
        if (inventory is { } main && bound == main) membership.Remove(objectId);
        var item = ItemSnapshot(objectId, at);
        var candidate = candidates.FirstOrDefault(x => x.ObjectId == objectId && !x.Confirmed);
        if (candidate is null && candidates.Count < 256 && bound != inventory
            && TryContainer(bound, at, out var sourceContainer)
            && TrySource(sourceContainer.SourceId, at, out var source)
            && (sourceId is null || sourceId == source.Id))
        {
            candidate = new Candidate(objectId, bound, source, item, null, at, tracker.LocalActor);
            candidates.Add(candidate);
        }
        if (candidate is not null)
        {
            candidate.Removed = true;
            candidate.Decrement = candidate.Baseline.Quantity;
            candidate.RemovalOrigin = origin;
            candidate.RemovedAt = at;
            candidate.Removal = tracker.Stamp(ActivityEventKind.Loot, at, origin);
            return candidate;
        }
        if (inferUnknown && sourceId is { } explicitSource && TrySource(explicitSource, at, out _))
            AmbiguousRemoval(explicitSource, item.Id, item.Quantity, at, origin, objectId, item);
        // Inventory deletion alone cannot distinguish moving, splitting, destroying or consuming.
        return null;
    }

    private void Allocated(long objectId, Allocation allocation, long sourceId, DateTime at, string? origin)
    {
        var recipient = tracker.ResolveActor(null, allocation.PlayerName);
        var allocatedSource = allocation.SourceId > 0 ? allocation.SourceId : sourceId;
        if (recipient.Relationship == ActorRelationship.Self)
        {
            // A local transfer supplies the exact quantity; the allocation proves the recipient if it does not arrive.
            if (Removed(objectId, at, origin, allocatedSource, inferUnknown: false) is { } candidate)
            {
                candidate.RecipientProven = true;
                return;
            }
        }
        else
        {
            ForgetMembership(objectId);
            candidates.RemoveAll(x => x.ObjectId == objectId && !x.Confirmed);
        }
        if (GetItem(objectId, at) is { Id: > 0, Quantity: > 0 } item)
            RecordAttributed(recipient, allocatedSource, item.Id, item.Quantity, item, objectId, at, origin, "PartyLootAllocation");
    }

    private void TypeRemoval(long sourceId, int itemId, long quantity, ActivityActor? recipient, DateTime at, string? origin)
    {
        if (itemId <= 0 || quantity <= 0 || !TrySource(sourceId, at, out var source)) return;
        // The removal may describe a pickup already recorded from this chest, by anyone.
        if (RemovalExplanation(sourceId, source.Name, itemId, quantity, null, at) is { } explained)
        {
            explained.RemovalMatched = true;
            return;
        }
        // Our own pending request for this item type is the removal; the transfer or the
        // request-and-removal proof records it with the local player as recipient.
        var own = candidates.Where(x => x.HasMoveIntent && !x.Confirmed && !x.TypeRemovalClaimed && x.Source.Id == sourceId
                && x.Baseline.Id == itemId && at - x.CreatedAt <= TimeSpan.FromSeconds(10))
            .OrderByDescending(x => x.Baseline.Quantity == quantity).ThenBy(x => x.CreatedAt).FirstOrDefault();
        if (own is not null)
        {
            own.TypeRemovalClaimed = true;
            if (!own.Removed)
            {
                own.Removed = true;
                own.Decrement = quantity;
                own.RemovedAt = at;
                own.RemovalOrigin = origin;
                own.Removal = tracker.Stamp(ActivityEventKind.Loot, at, origin);
            }
            return;
        }
        var item = SourceItem(sourceId, itemId, quantity, at);
        var objectId = item?.ObjectId;
        // A stack reserved for another member has no object; the ledger still knows its quality.
        if (item is null && partyChests.IsReserved(sourceId, itemId))
            item = new Item(0, itemId, partyChests.Quality(sourceId, itemId), quantity, null, null, at, at,
                GameValue: partyChests.UnitValue(sourceId, itemId));
        if (recipient is not null)
            RecordAttributed(recipient, sourceId, itemId, quantity, item, objectId, at, origin, "PartyLootTypeRemoval");
        else AmbiguousRemoval(sourceId, itemId, quantity, at, origin, objectId, item);
    }

    // Type-only removals omit object IDs; prefer a known stack of that item with the removed amount.
    // Only a shared view can hold another player's stack; our private view holds our own loot.
    private Item? SourceItem(long sourceId, int itemId, long quantity, DateTime at) => items.Values
        .Where(x => x.Id == itemId && x.ObjectId > 0 && at - x.LastSeen <= MetadataLifetime && !WasRecorded(x.ObjectId, at)
            && membership.TryGetValue(x.ObjectId, out var bound) && containers.TryGetValue(bound, out var container)
            && container.SourceId == sourceId && container.IsPublic)
        .OrderByDescending(x => x.Quantity == quantity).ThenByDescending(x => x.LastSeen).FirstOrDefault();

    // Absent amounts mean one item; a single amount applies to every listed type.
    private static long IndexedAmount(IReadOnlyList<int> amounts, int index)
        => amounts.Count == 0 ? 1 : index < amounts.Count ? amounts[index] : amounts.Count == 1 ? amounts[0] : 1;

    private void RecordAttributed(ActivityActor recipient, long sourceId, int itemId, long quantity, Item? item, long? objectId,
        DateTime at, string? origin, string proof)
    {
        if (itemId <= 0 || quantity <= 0 || !TrySource(sourceId, at, out var source)) return;
        if (objectId is { } id && WasRecorded(id, at)) return;
        if (recent.FirstOrDefault(x => x.Event.SourceActorId == recipient.Id && x.Event.ItemId == itemId
            && x.Event.Quantity == quantity && Math.Abs((at - x.At).TotalSeconds) <= CorrelationWindow.TotalSeconds) is { } existing)
        {
            // A broadcast or exact transfer already recorded this pickup; the chest view can add its quality.
            existing.Matched = true;
            existing.ObjectId ??= objectId;
            if (item is not null && Enrich(existing.Event, item) is var enriched && enriched != existing.Event)
                existing.Event = tracker.AmendLoot(enriched with { Revision = existing.Event.Revision + 1 });
            MarkRecorded(objectId, at);
            return;
        }
        var observation = Describe(ActivityTrackerService.WithSource(tracker.Stamp(ActivityEventKind.Loot, at, origin), recipient) with
        {
            Quantity = quantity,
            LootSourceName = source.Name,
            LootSourceKind = source.Kind,
            DetailsJson = JsonSerializer.Serialize(new { Proof = proof, SourceObjectId = sourceId, ItemObjectId = objectId })
        }, itemId);
        if (item is not null) observation = Enrich(observation, item);
        observation = tracker.RecordLoot(observation);
        recent.Add(new RecentPickup(observation, objectId, at, false) { IsAttributed = true, SourceId = sourceId });
        MarkRecorded(objectId, at);
    }

    private void GuessRemaining(long sourceId, DateTime at, BaseEvent packet)
    {
        if (!detachedChests.TryGetValue(sourceId, out var closed)) return;
        if (at - closed.At > EmptyChestWindow) { detachedChests.Remove(sourceId); return; }
        detachedChests.Remove(sourceId);
        emptyChests.Remove(sourceId);
        GuessTaken(sourceId, closed.Items, at, packet);
    }

    private void GuessTaken(long sourceId, IEnumerable<long> objectIds, DateTime at, BaseEvent packet)
    {
        if (!TrySource(sourceId, at, out var source) || source.Kind != "Chest") return;
        var index = 0;
        foreach (var objectId in objectIds.Distinct().ToArray())
        {
            var origin = ActivityTrackerService.Origin(packet, "ChestGuess", index++);
            if (WasRecorded(objectId, at) || candidates.Any(x => x.ObjectId == objectId && !x.Confirmed)
                || GetItem(objectId, at) is not { Id: > 0, Quantity: > 0 } item) continue;
            AmbiguousRemoval(sourceId, item.Id, item.Quantity, at, origin, objectId, item, proof: "PublicChestDisappearance");
        }
    }

    private void ForgetMembership(long objectId)
    {
        if (!membership.Remove(objectId, out var containerId) || !containers.TryGetValue(containerId, out var container)) return;
        for (var index = 0; index < container.Slots.Length; index++)
            if (container.Slots[index] == objectId) container.Slots[index] = 0;
    }

    private void ConfirmUniqueGains(DateTime at)
    {
        foreach (var gain in gains.Where(x => !x.Consumed && at - x.At <= CorrelationWindow).ToArray())
        {
            var matches = candidates.Where(x => x.HasMoveIntent && !x.Confirmed && x.Decrement is > 0
                && x.Baseline.Id == gain.Item.Id && x.Baseline.Quality == gain.Item.Quality
                && x.Decrement == gain.Quantity && Math.Abs((gain.At - (x.RemovedAt ?? x.CreatedAt)).TotalSeconds) <= 3).ToArray();
            if (matches.Length != 1) continue;
            var competingGains = gains.Count(x => !x.Consumed && x.Item.Id == gain.Item.Id
                && x.Item.Quality == gain.Item.Quality && x.Quantity == gain.Quantity && Math.Abs((x.At - gain.At).TotalSeconds) <= 3);
            if (competingGains != 1) continue;
            Confirm(matches[0], gain.Item, gain.Quantity, gain.At, gain.Context.OriginKey ?? matches[0].RemovalOrigin,
                "UniqueDecrementAndGain", gain.Context);
            gain.Consumed = true;
        }
    }

    private void Confirm(Candidate candidate, Item item, long quantity, DateTime at, string? origin, string evidence,
        ActivityEvent? original = null)
    {
        if (quantity <= 0 || candidate.Confirmed) return;
        candidate.Confirmed = true;
        foreach (var gain in gains.Where(x => x.ObjectId == item.ObjectId && x.Item.Id == item.Id
            && Math.Abs((x.At - at).TotalSeconds) <= 3)) gain.Consumed = true;
        var matching = MatchRecent(candidate.Source.Name, item.Id, quantity, candidate.Actor.Id, at)
            ?? MatchAnySource(x => x.IsBroadcast || x.IsAttributed, item.Id, quantity, candidate.Actor.Id, at);
        if (matching is not null && (matching.IsBroadcast || matching.IsAttributed))
        {
            var amended = Enrich(matching.Event, item) with
            {
                Revision = matching.Event.Revision + 1,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    Proof = evidence,
                    SourceObjectId = candidate.Source.Id,
                    ItemObjectId = item.ObjectId,
                    RequestedQuantity = candidate.IntentQuantity
                })
            };
            matching.Event = tracker.AmendLoot(amended);
            matching.ObjectId = item.ObjectId;
            matching.Matched = true;
            SupersedeInferred(item.ObjectId);
            MarkRecorded(item.ObjectId, at);
            if (candidate.Transfer is { } provisional) tracker.RetractLoot(provisional.Id, provisional.Revision + 1);
            return;
        }
        var observation = ActivityTrackerService.WithSource(candidate.Transfer ?? original ?? tracker.Stamp(ActivityEventKind.Loot, at, origin), candidate.Actor) with
        {
            Evidence = ActivityEvidence.Confirmed,
            Quantity = quantity,
            LootSourceName = candidate.Source.Name,
            LootSourceKind = candidate.Source.Kind,
            DetailsJson = JsonSerializer.Serialize(new
            {
                Proof = evidence,
                SourceObjectId = candidate.Source.Id,
                ItemObjectId = item.ObjectId,
                RequestedQuantity = candidate.IntentQuantity
            })
        };
        observation = Enrich(observation, item);
        if (candidate.Transfer is not null)
            observation = tracker.AmendLoot(observation with { Revision = observation.Revision + 1 });
        else observation = tracker.RecordLoot(observation, preserveEncounterAssociation: original is not null);
        SupersedeInferred(item.ObjectId);
        recent.Add(new RecentPickup(observation, item.ObjectId, at, false) { SourceId = candidate.Source.Id });
        MarkRecorded(item.ObjectId, at);
    }

    // The exact recipient supersedes an earlier unknown-recipient record of the same stack.
    private void SupersedeInferred(long objectId)
    {
        if (recent.FirstOrDefault(x => x.ObjectId == objectId && x.Event.Evidence == ActivityEvidence.Inferred) is not { } inferred) return;
        tracker.RetractLoot(inferred.Event.Id, inferred.Event.Revision + 1);
        recent.Remove(inferred);
    }

    private void Broadcast(OtherGrabbedLootEvent packet, DateTime at)
    {
        if (packet.IsSilver)
        {
            var actor = tracker.ResolveActor(null, packet.PlayerName);
            if (ActivityTrackerService.Tracked(actor.Relationship))
                tracker.RecordLoot(ActivityTrackerService.WithSource(tracker.Stamp(ActivityEventKind.OutcomeEvidence, at,
                    ActivityTrackerService.Origin(packet, "SilverBroadcast")), actor) with
                {
                    Evidence = ActivityEvidence.Supplemental,
                    DetailsJson = JsonSerializer.Serialize(new
                    { Signal = "SilverBroadcast", packet.RawAmount, packet.AmountState, packet.SourceName })
                });
            return;
        }
        if (packet.ItemId <= 0 || packet.RawAmount is not > 0 || string.IsNullOrWhiteSpace(packet.PlayerName)) return;
        var amount = packet.RawAmount.Value;
        var picker = tracker.ResolveActor(null, packet.PlayerName);
        if (picker.Relationship == ActorRelationship.Self)
            foreach (var gain in gains.Where(x => x.Item.Id == packet.ItemId && x.Quantity == amount
                && Math.Abs((x.At - at).TotalSeconds) <= 3)) gain.Consumed = true;
        else
        {
            // Another player emptied this stack, so neither our concurrent request nor an unknown recipient took it.
            var taken = candidates.Where(x => !x.Confirmed && x.Removed && x.Transfer is null && x.Baseline.Id == packet.ItemId
                && x.Decrement == amount && Math.Abs((at - (x.RemovedAt ?? x.CreatedAt)).TotalSeconds) <= 3).ToArray();
            if (taken.Length == 1) taken[0].Confirmed = true;
        }
        var pendingTransfers = candidates.Where(x => !x.Confirmed && x.Transfer is { } transfer
            && transfer.ItemId == packet.ItemId && transfer.SourceActorId == picker.Id
            && Math.Abs((at - transfer.CapturedAtUtc).TotalSeconds) <= 3).ToArray();
        if (pendingTransfers.Length > 1)
            pendingTransfers = pendingTransfers.Where(x => x.Transfer!.LootSourceName == packet.SourceName).ToArray();
        if (pendingTransfers.Length == 1)
        {
            var pending = pendingTransfers[0];
            Confirm(pending, ItemSnapshot(pending.ObjectId, at), amount,
                pending.Transfer!.CapturedAtUtc, pending.Transfer.OriginKey, "PickupBroadcast");
            var confirmed = recent.SingleOrDefault(x => x.Event.Id == pending.Transfer.Id);
            if (confirmed is not null) confirmed.Matched = true;
            return;
        }
        var removals = recent.Where(x => x.Event.Evidence == ActivityEvidence.Inferred && x.Event.ItemId == packet.ItemId
            && x.Event.Quantity == amount && Math.Abs((at - x.At).TotalSeconds) <= 3).ToArray();
        if (removals.Length > 1) removals = removals.Where(x => x.Event.LootSourceName == packet.SourceName).ToArray();
        if (removals.Length == 1)
        { tracker.RetractLoot(removals[0].Event.Id, removals[0].Event.Revision + 1); recent.Remove(removals[0]); }
        var previous = MatchRecent(packet.SourceName, packet.ItemId, amount, picker.Id, at)
            ?? MatchAnySource(x => !x.IsBroadcast, packet.ItemId, amount, picker.Id, at);
        if (previous is not null && !previous.IsBroadcast)
        {
            previous.Matched = true;
            return; // Supplementary view of an already recorded transfer or party allocation.
        }
        var descriptor = itemNames.GetItemById(packet.ItemId);
        var observation = ActivityTrackerService.WithSource(tracker.Stamp(ActivityEventKind.Loot, at,
            ActivityTrackerService.Origin(packet, "PickupBroadcast")), picker) with
        {
            ItemId = packet.ItemId,
            ItemName = descriptor.UsName,
            ItemUniqueName = descriptor.UniqueName == "Unknown Item" ? null : descriptor.UniqueName,
            Quantity = amount,
            LootSourceName = packet.SourceName,
            LootSourceKind = SourceKind(packet.SourceName),
            DetailsJson = JsonSerializer.Serialize(new { Proof = "PickupBroadcast", packet.ObjectId })
        };
        observation = tracker.RecordLoot(observation);
        recent.Add(new RecentPickup(observation, null, at, true));
    }

    private RecentPickup? MatchRecent(string source, int item, long quantity, string actor, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var matches = recent.Where(x => !x.Matched && x.Event.SourceActorId == actor && x.Event.ItemId == item
            && x.Event.Quantity == quantity && x.Event.LootSourceName == source && Math.Abs((at - x.At).TotalSeconds) <= 3).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    // Broadcasts and chest views can name the same container differently; one unique
    // pickup by the same player, item and amount within the window is the same pickup.
    private RecentPickup? MatchAnySource(Func<RecentPickup, bool> kind, int item, long quantity, string actor, DateTime at)
    {
        var matches = recent.Where(x => !x.Matched && kind(x) && x.Event.SourceActorId == actor && x.Event.ItemId == item
            && x.Event.Quantity == quantity && Math.Abs((at - x.At).TotalSeconds) <= 3).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private void AmbiguousRemoval(long sourceId, int itemId, long quantity, DateTime at, string? origin,
        long? objectId = null, Item? item = null, ActivityEvent? original = null, string proof = "SourceRemovalOnly")
    {
        if (quantity <= 0 || itemId <= 0 || !TrySource(sourceId, at, out var source)) return;
        if (objectId is { } id && WasRecorded(id, at)) return;
        // A broadcast or recorded pickup of the same stack already names who took it.
        if (RemovalExplanation(sourceId, source.Name, itemId, quantity, objectId, at) is { } explained)
        {
            explained.RemovalMatched = true;
            explained.ObjectId ??= objectId;
            MarkRecorded(objectId, at);
            return;
        }
        var observation = Describe((original ?? tracker.Stamp(ActivityEventKind.Loot, at, origin)) with
        {
            Evidence = ActivityEvidence.Inferred,
            Quantity = quantity,
            LootSourceName = source.Name,
            LootSourceKind = source.Kind,
            DetailsJson = JsonSerializer.Serialize(new
            {
                Proof = proof,
                SourceObjectId = sourceId,
                ItemObjectId = objectId,
                Recipient = "Unknown"
            })
        }, itemId);
        if (item is not null) observation = Enrich(observation, item);
        observation = tracker.RecordLoot(observation);
        recent.Add(new RecentPickup(observation, objectId, at, false) { SourceId = sourceId });
        MarkRecorded(objectId, at, inferred: true);
    }

    // One unmatched pickup of the same item and amount within the window explains a source
    // removal: an exact or attributed pickup from that source, or a broadcast naming its looter.
    private RecentPickup? RemovalExplanation(long sourceId, string sourceName, int itemId, long quantity, long? objectId, DateTime at)
    {
        var matches = recent.Where(x => !x.RemovalMatched && x.Event.Evidence != ActivityEvidence.Inferred
            && x.Event.ItemId == itemId && x.Event.Quantity == quantity && Math.Abs((at - x.At).TotalSeconds) <= 3
            && (x.SourceId == sourceId || x.IsBroadcast && x.SourceId is null)
            && (objectId is null || x.ObjectId is null || x.ObjectId == objectId)).ToArray();
        if (matches.Length > 1)
        {
            var sameSource = matches.Where(x => x.SourceId == sourceId || x.Event.LootSourceName == sourceName).ToArray();
            if (sameSource.Length > 0) matches = sameSource;
        }
        return matches.OrderBy(x => x.At).FirstOrDefault();
    }

    // Reference data supplies names a pickup omits, so every pickup stays readable and priceable.
    private ActivityEvent Describe(ActivityEvent observation, int itemId)
    {
        if (!string.IsNullOrWhiteSpace(observation.ItemUniqueName) && !string.IsNullOrWhiteSpace(observation.ItemName))
            return observation with { ItemId = itemId };
        var descriptor = itemNames.GetItemById(itemId);
        var known = !descriptor.UniqueName.StartsWith("Unknown Item", StringComparison.OrdinalIgnoreCase);
        return observation with
        {
            ItemId = itemId,
            ItemUniqueName = string.IsNullOrWhiteSpace(observation.ItemUniqueName) ? known ? descriptor.UniqueName : null : observation.ItemUniqueName,
            ItemName = !string.IsNullOrWhiteSpace(observation.ItemName) ? observation.ItemName
                : known && !string.IsNullOrWhiteSpace(descriptor.UsName) ? descriptor.UsName : $"Item {itemId}"
        };
    }

    private ActivityEvent Enrich(ActivityEvent observation, Item item)
    {
        if (item.Id <= 0) return observation;
        var quality = observation.Quality ?? item.Quality;
        // An estimate made without the actual quality is replaced once it is known.
        var changedBasis = observation.Quality is null && quality is > 0;
        var price = changedBasis ? null : observation.EstimatedUnitValue;
        var priceObservedAt = changedBasis ? null : observation.PriceObservedAtUtc;
        if (price is null && int.TryParse(observation.Server, out var server))
            price = ActivityItemValuationService.LootEstimate(values, server, item.Id, quality, item.BlackMarketEmv, item.Emv);
        // The game's own estimate for a reserved party stack is used only without a market price.
        if (price is null && item.GameValue is > 0) price = item.GameValue;
        var enriched = observation with
        {
            ItemId = item.Id,
            ItemUniqueName = observation.ItemUniqueName ?? item.UniqueName,
            ItemName = item.Name ?? observation.ItemName,
            Quality = quality,
            EstimatedUnitValue = price,
            PriceObservedAtUtc = priceObservedAt ?? (price is > 0 ? DateTime.UtcNow : null)
        };
        enriched = Describe(enriched, item.Id);
        return price is > 0 ? ActivityItemValuationService.WithPrice(enriched, price.Value,
            enriched.PriceObservedAtUtc!.Value, quality ?? 0) : enriched;
    }

    private void PricesChanged(IReadOnlyCollection<ItemEstimatedMarketValueKey> keys)
    {
        var now = DateTime.UtcNow;
        var updates = new List<ActivityLootPrice>();
        foreach (var key in keys)
        {
            var server = key.ServerId.ToString(CultureInfo.InvariantCulture);
            if (values.Get(key.ServerId, key.ItemId, key.Quality) is { NormalEmv: > 0 } price)
                updates.Add(new ActivityLootPrice(server, key.ItemId, key.Quality, price.NormalEmv, now));
            if (ActivityItemValuationService.LootEstimate(values, key.ServerId, key.ItemId, key.Quality) is long loot and > 0)
                updates.Add(new ActivityLootPrice(server, key.ItemId, key.Quality, loot, now, LootBasis: true));
        }
        foreach (var (serverId, itemId) in keys.Select(key => (key.ServerId, key.ItemId)).Distinct())
            if (ActivityItemValuationService.LootEstimate(values, serverId, itemId, null) is long average and > 0)
                updates.Add(new ActivityLootPrice(serverId.ToString(CultureInfo.InvariantCulture), itemId, 0, average, now, LootBasis: true));
        if (updates.Count > 0) _ = FillPricesAsync(updates.ToArray());
    }

    private async Task FillPricesAsync(ActivityLootPrice[] updates)
    {
        try { await tracker.FillLootPricesAsync(updates).ConfigureAwait(false); }
        catch (Exception error) { Log.Warning(error, "Could not fill missing activity loot estimates."); }
    }

    // Guild and alliance can arrive after a pickup; complete pickups that are still correlated.
    internal void RefreshAffiliations(string playerName)
    {
        foreach (var pickup in recent.Where(x => string.Equals(x.Event.SourceName, playerName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Event.LootSourceName, playerName, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var filled = tracker.FillAffiliations(pickup.Event);
            if (filled != pickup.Event) pickup.Event = tracker.AmendLoot(filled with { Revision = pickup.Event.Revision + 1 });
        }
    }

    private void MarkRecorded(long? objectId, DateTime at, bool inferred = false)
    {
        if (objectId is not > 0) return;
        if (recordedObjects.Count >= 4096 && !recordedObjects.ContainsKey(objectId.Value)) return;
        recordedObjects[objectId.Value] = new RecordedObject(at, inferred);
    }

    // An item object is one stack instance, so any pickup recorded for it within the
    // metadata lifetime is the same pickup seen through another packet.
    private bool WasRecorded(long objectId, DateTime at)
        => recordedObjects.TryGetValue(objectId, out var recorded) && at - recorded.At <= MetadataLifetime;

    private bool WasRecordedAsPickup(long objectId, DateTime at)
        => recordedObjects.TryGetValue(objectId, out var recorded) && !recorded.Inferred && at - recorded.At <= MetadataLifetime;

    public void Expire(DateTime at)
    {
        foreach (var key in localIntroductions.Where(x => at - x.Value.At > TimeSpan.FromSeconds(2)).Select(x => x.Key).ToArray())
            localIntroductions.Remove(key);
        resourceClaims.RemoveAll(x => at - x.At > TimeSpan.FromSeconds(5));
        ResolveCandidates(at, force: false);
        candidates.RemoveAll(x => at - x.CreatedAt > TimeSpan.FromSeconds(10));
        gains.RemoveAll(x => x.Consumed || at - x.At > TimeSpan.FromSeconds(5));
        if (recent.Count > 4096) recent.RemoveRange(0, recent.Count - 4096);
        // Short-lived acquisition proofs above expire before every observation;
        // metadata lookups enforce their TTL without repeatedly sweeping caches.
        if (at - lastCacheSweep < TimeSpan.FromSeconds(1)) return;
        lastCacheSweep = at;
        foreach (var key in pendingInventoryMetadata.Where(x => at - x.Value.LastSeen > TimeSpan.FromSeconds(10))
            .Select(x => x.Key).ToArray()) pendingInventoryMetadata.Remove(key);
        recent.RemoveAll(x => at - x.At > TimeSpan.FromMinutes(2));
        foreach (var key in items.Where(x => at - x.Value.LastSeen > MetadataLifetime && !InMainInventory(x.Key))
            .Select(x => x.Key).ToArray()) items.Remove(key);
        foreach (var key in containers.Where(x => at - x.Value.LastSeen > MetadataLifetime).Select(x => x.Key).ToArray()) containers.Remove(key);
        foreach (var key in sources.Where(x => at - x.Value.LastSeen > MetadataLifetime).Select(x => x.Key).ToArray()) sources.Remove(key);
        foreach (var key in membership.Where(x => x.Value != inventory && !containers.ContainsKey(x.Value))
            .Select(x => x.Key).ToArray()) membership.Remove(key);
        foreach (var key in allocations.Where(x => at - x.Value.At > MetadataLifetime).Select(x => x.Key).ToArray()) allocations.Remove(key);
        foreach (var key in chestRecipients.Where(x => at - x.Value.At > MetadataLifetime).Select(x => x.Key).ToArray()) chestRecipients.Remove(key);
        foreach (var key in detachedChests.Where(x => at - x.Value.At > MetadataLifetime).Select(x => x.Key).ToArray()) detachedChests.Remove(key);
        foreach (var key in emptyChests.Where(x => at - x.Value > EmptyChestWindow).Select(x => x.Key).ToArray()) emptyChests.Remove(key);
        foreach (var key in recordedObjects.Where(x => at - x.Value.At > MetadataLifetime).Select(x => x.Key).ToArray()) recordedObjects.Remove(key);
        partyChests.Expire(at, MetadataLifetime);
    }

    // Leaving a map, finishing or stopping cannot wait for the correlation window.
    internal void ResolvePending(DateTime at) => ResolveCandidates(at, force: true);

    private void ResolveCandidates(DateTime at, bool force)
    {
        foreach (var candidate in candidates.Where(x => !x.Confirmed && (x.Removed || x.HasMoveIntent && x.Decrement is > 0)
            && (force || at - (x.RemovedAt ?? x.CreatedAt) > CorrelationWindow)).ToArray())
        {
            var quantity = candidate.Decrement ?? candidate.Baseline.Quantity;
            if ((candidate.HasMoveIntent || candidate.RecipientProven) && candidate.Baseline.Id > 0 && quantity > 0)
                // Our own move request (or the party allocation) names the recipient, and the source lost the item.
                Confirm(candidate, candidate.Baseline, quantity, candidate.RemovedAt ?? at, candidate.RemovalOrigin,
                    candidate.RecipientProven ? "PartyLootAllocation" : "MoveRequestAndSourceRemoval", candidate.Removal);
            else
                AmbiguousRemoval(candidate.Source.Id, candidate.Baseline.Id, quantity,
                    candidate.RemovedAt ?? at, candidate.RemovalOrigin, candidate.ObjectId, candidate.Baseline, candidate.Removal);
            candidate.Confirmed = true;
        }
    }

    // Only the local inventory proofs used by loot are exposed. Requests, source
    // removals, and ambiguous membership changes cannot confirm fishing rewards.
    internal ActivityAcquisitionEvidence? ClaimResourceAcquisition(int itemId, long quantity, long excludedObjectId,
        DateTime from, DateTime until, long epoch)
    {
        var matches = gains.Where(gain => !gain.Consumed && !gain.Disrupted && gain.ValidatedSimple && gain.ObjectId != excludedObjectId
            && gain.Item.Id == itemId && gain.Quantity == quantity && gain.At >= from && gain.At <= until
            && gain.Context.MapEpoch == epoch
            && !candidates.Any(candidate => candidate.ObjectId == gain.ObjectId && !candidate.Confirmed)
            && !recent.Any(pickup => pickup.Event.Evidence == ActivityEvidence.Confirmed && pickup.Event.ItemId == itemId
                && pickup.Event.Quantity == quantity && pickup.Event.SourceRelationship == ActorRelationship.Self
                && Math.Abs((pickup.At - gain.At).TotalSeconds) <= 3)
            && !resourceClaims.Any(claim => claim.ItemId == itemId && claim.Quantity == quantity
                && Math.Abs((claim.At - gain.At).TotalSeconds) <= 2)).ToArray();
        if (matches.Length != 1) return null;
        var match = matches[0];
        match.Consumed = true;
        // The exact gain is consumed once. A distinct next catch may have the
        // same item/count and must not be suppressed by a temporal signature.
        return new ActivityAcquisitionEvidence(match.ObjectId, match.Item.Quality, match.Item.UniqueName, match.Item.Name,
            match.At, match.Context.OriginKey);
    }

    internal void NoteHarvestAcquisition(int itemId, long quantity, DateTime at)
    {
        if (resourceClaims.Count >= 256) resourceClaims.RemoveAt(0);
        resourceClaims.Add(new ResourceClaim(itemId, quantity, at));
        foreach (var gain in gains.Where(x => x.Item.Id == itemId && x.Quantity == quantity
            && Math.Abs((x.At - at).TotalSeconds) <= 2)) gain.Consumed = true;
    }

    private void AddGain(Gain value)
    {
        if (value.Quantity <= 0 || gains.Any(x => x.ObjectId == value.ObjectId && x.At == value.At && x.Quantity == value.Quantity)) return;
        if (gains.Count >= 256) gains.RemoveAt(0);
        gains.Add(value);
    }

    public void ResetCandidates() { candidates.Clear(); gains.Clear(); localIntroductions.Clear(); resourceClaims.Clear(); }
    public void ResetRecordingCorrelations()
    { ResetCandidates(); recent.Clear(); recordedObjects.Clear(); detachedChests.Clear(); emptyChests.Clear(); }
    internal void DisruptLocalAcquisition(DateTime at)
    {
        // Rearrangement makes earlier gains unusable as fishing proof, never as loot proof.
        if (nonLootMoveUntil < at.AddSeconds(10)) nonLootMoveUntil = at.AddSeconds(10);
        foreach (var gain in gains.Where(x => x.At <= at)) gain.Disrupted = true;
        foreach (var key in localIntroductions.Where(x => x.Value.At <= at).Select(x => x.Key).ToArray())
            localIntroductions[key] = localIntroductions[key] with { Disrupted = true };
    }
    public void Reset()
    {
        ResetRecordingCorrelations(); items.Clear(); pendingInventoryMetadata.Clear(); sources.Clear(); containers.Clear(); membership.Clear();
        allocations.Clear(); chestRecipients.Clear(); partyChests.Clear(); inventory = null; nonLootMoveUntil = default; lastCacheSweep = default;
    }
    public void ForgetSession(Guid id) { recent.RemoveAll(x => x.Event.SessionId == id); ResetCandidates(); }
    public void ForgetUnassigned()
    { recent.RemoveAll(x => x.Event.SessionId is null); ResetCandidates(); }
    public void Dispose() => values.EstimatedMarketValuesChanged -= PricesChanged;

    private sealed record Item(long ObjectId, int Id, int? Quality, long Quantity, string? UniqueName, string? Name,
        DateTime LastSeen, DateTime QuantitySeenAt, bool ValidAcquisitionQuantity = false, long? Emv = null, long? BlackMarketEmv = null,
        long? GameValue = null);
    private readonly record struct PendingInventoryItemKey(string Connection, Guid? ReceiverGeneration, short? PeerId,
        int? Challenge, long ObjectId);
    private sealed record Source(long Id, string Name, string Kind, DateTime LastSeen);
    private sealed class Container(Guid id, long sourceId, long[] slots, DateTime lastSeen, bool isPublic)
    {
        public Guid Id { get; } = id; public long SourceId { get; } = sourceId; public long[] Slots { get; } = slots;
        public DateTime LastSeen { get; set; } = lastSeen; public bool IsPublic { get; } = isPublic;
    }
    private sealed class Candidate(long objectId, Guid containerId, Source source, Item baseline, long? intentQuantity,
        DateTime createdAt, ActivityActor actor, bool hasMoveIntent = false)
    {
        public long ObjectId { get; } = objectId;
        public Guid ContainerId { get; } = containerId;
        public Source Source { get; } = source;
        public Item Baseline { get; } = baseline;
        public long? IntentQuantity { get; } = intentQuantity;
        public DateTime CreatedAt { get; } = createdAt;
        public ActivityActor Actor { get; } = actor;
        public bool HasMoveIntent { get; } = hasMoveIntent;
        public Guid? Destination { get; init; }
        public bool RecipientProven { get; set; }
        public bool TypeRemovalClaimed { get; set; }
        public bool Removed { get; set; }
        public bool Confirmed { get; set; }
        public long? Decrement { get; set; }
        public DateTime? RemovedAt { get; set; }
        public string? RemovalOrigin { get; set; }
        public ActivityEvent? Transfer { get; set; }
        public ActivityEvent? Removal { get; set; }
    }
    private sealed class Gain(long objectId, long quantity, Item item, DateTime at, ActivityEvent context, bool validatedSimple = false)
    {
        public long ObjectId { get; } = objectId; public long Quantity { get; } = quantity; public Item Item { get; } = item;
        public DateTime At { get; } = at; public ActivityEvent Context { get; } = context; public bool ValidatedSimple { get; } = validatedSimple;
        public bool Consumed { get; set; }
        public bool Disrupted { get; set; }
    }
    private sealed record LocalIntroduction(DateTime At, ActivityEvent Context, bool Disrupted = false);
    private sealed record ResourceClaim(int ItemId, long Quantity, DateTime At);
    private sealed record Allocation(long SourceId, string PlayerName, DateTime At);
    private sealed record ChestRecipients(IReadOnlyList<Guid> PlayerGuids, DateTime At);
    private sealed record DetachedChest(IReadOnlyList<long> Items, DateTime At);
    private readonly record struct RecordedObject(DateTime At, bool Inferred);
    private sealed class RecentPickup(ActivityEvent observation, long? objectId, DateTime at, bool isBroadcast)
    {
        public ActivityEvent Event { get; set; } = observation; public long? ObjectId { get; set; } = objectId; public DateTime At { get; } = at;
        public bool IsBroadcast { get; } = isBroadcast; public bool IsAttributed { get; init; }
        public bool Matched { get; set; }
        public long? SourceId { get; init; }
        public bool RemovalMatched { get; set; }
    }
}

internal sealed record ActivityAcquisitionEvidence(long ObjectId, int? Quality, string? UniqueName, string? Name,
    DateTime CapturedAtUtc, string? OriginKey);

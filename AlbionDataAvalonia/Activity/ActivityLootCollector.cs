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
internal sealed class ActivityLootCollector : IDisposable
{
    private static readonly TimeSpan MetadataLifetime = TimeSpan.FromMinutes(10);
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
            value.Quantity > 0 ? at : sameItem?.QuantitySeenAt ?? DateTime.MinValue, validatedSimple);
    }

    private static PendingInventoryItemKey PendingKey(string connection, PhotonMessageIdentity? identity, long objectId)
        => new(connection, identity?.ReceiverGeneration, identity?.PeerId, identity?.Challenge, objectId);

    public void Observe(BaseOperation packet, DateTime at, bool record = true)
    {
        if (inventory is not { } destination) return;
        switch (packet)
        {
            case InventoryMoveItemRequest move when move.DestinationContainerId == destination:
                if (TryContainer(move.SourceContainerId, at, out var container)
                    && move.SourceSlot >= 0 && move.SourceSlot < container.Slots.Length)
                {
                    if (!TrySource(container.SourceId, at, out _)) nonLootMoveUntil = at.AddSeconds(10);
                    if (record) Intent(container.Slots[move.SourceSlot], container, null, at);
                }
                else nonLootMoveUntil = at.AddSeconds(10);
                break;
            case InventoryMoveGivenItemsRequest move when move.DestinationContainerId == destination:
                if (!TryContainer(move.SourceContainerId, at, out var source)
                    || !TrySource(source.SourceId, at, out _)) { nonLootMoveUntil = at.AddSeconds(10); return; }
                for (var index = 0; record && index < move.ItemObjectIds.Count && candidates.Count < 256; index++)
                    Intent(move.ItemObjectIds[index], source,
                        move.Quantities is { } amounts && index < amounts.Count ? amounts[index] : null, at);
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
                break;
            case AttachItemContainerEvent attach:
                Attach(attach, at, record);
                break;
            case DetachItemContainerEvent detach:
                // Detachment is visibility evidence, never a pickup or an emptied chest.
                if (TryContainer(detach.ContainerId, at, out var detached)) detached.LastSeen = at;
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
                for (var index = 0; index < party.ItemObjectIds.Count; index++)
                {
                    if (index >= party.ItemIds.Count || index >= party.Amounts.Count || party.Amounts[index] <= 0) continue;
                    var objectId = party.ItemObjectIds[index];
                    var quality = index < party.Qualities.Count && party.Qualities[index] > 0 ? party.Qualities[index] : (int?)null;
                    Metadata(objectId, party.ItemIds[index], quality, party.Amounts[index], null, null, at, record: record);
                    if (TrySource(party.SourceObjectId, at, out _))
                        foreach (var container in containers.Values.Where(x => x.SourceId == party.SourceObjectId && at - x.LastSeen <= MetadataLifetime))
                            membership[objectId] = container.Id;
                }
                // Allocation names do not establish who actually picked up an item.
                break;
            case PartyLootItemsRemovedEvent removed:
                IdentifySource(removed.SourceObjectId, null, "Chest", at);
                for (var index = 0; index < removed.ItemObjectIds.Count; index++)
                    Removed(removed.ItemObjectIds[index], at, ActivityTrackerService.Origin(packet, "PartyRemoval", index), removed.SourceObjectId, record);
                break;
            case PartyLootItemTypesRemovedEvent removed:
                IdentifySource(removed.SourceObjectId, null, "Chest", at);
                for (var index = 0; record && index < removed.ItemIds.Count && index < removed.Amounts.Count; index++)
                    AmbiguousRemoval(removed.SourceObjectId, removed.ItemIds[index], removed.Amounts[index], at,
                        ActivityTrackerService.Origin(packet, "TypeRemoval", index));
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
        foreach (var id in new[] { packet.ContainerId, packet.PrivateContainerId }.Where(x => x != Guid.Empty).Distinct())
        {
            var observedItems = packet.SlotItems.Where(x => x > 0).ToHashSet();
            if (!record)
                foreach (var missing in membership.Where(x => x.Value == id && !observedItems.Contains(x.Key)).Select(x => x.Key).ToArray())
                    ForgetMembership(missing);
            containers[id] = new Container(id, packet.ObjectId, packet.SlotItems.ToArray(), at);
            foreach (var objectId in packet.SlotItems.Where(x => x > 0)) membership[objectId] = id;
        }
    }

    private void Intent(long objectId, Container container, long? quantity, DateTime at)
    {
        if (objectId <= 0 || inventory == container.Id || !TrySource(container.SourceId, at, out var source)
            || candidates.Count >= 256) return;
        if (!container.Slots.Contains(objectId) && !(TryContainer(Membership(objectId, at), at, out var known)
            && known.SourceId == container.SourceId)) return;
        candidates.RemoveAll(x => x.ObjectId == objectId);
        candidates.Add(new Candidate(objectId, container.Id, source, ItemSnapshot(objectId, at), quantity, at, tracker.LocalActor,
            hasMoveIntent: true));
    }

    private Item ItemSnapshot(long objectId, DateTime at) => GetItem(objectId, at)
        ?? new Item(objectId, 0, null, 0, null, null, at, DateTime.MinValue);

    // Lookups enforce the original TTL even between broad maintenance sweeps.
    private Item? GetItem(long objectId, DateTime at) => items.TryGetValue(objectId, out var item)
        && at - item.LastSeen <= MetadataLifetime ? item : null;
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
            item.ItemUsName == "Unset" ? null : item.ItemUsName, at, validatedSimple == true, record);
    }

    private void Metadata(long objectId, int itemId, int? quality, long? quantity, string? uniqueName, string? name, DateTime at,
        bool validatedSimple = false, bool record = true)
    {
        var old = GetItem(objectId, at);
        var next = new Item(objectId, itemId, quality ?? old?.Quality, quantity ?? old?.Quantity ?? 0,
            uniqueName ?? old?.UniqueName, name ?? old?.Name, at,
            quantity is > 0 ? at : old?.QuantitySeenAt ?? DateTime.MinValue,
            quantity is > 0 ? validatedSimple : old?.ValidAcquisitionQuantity ?? false);
        items[objectId] = next;
        if (!record) return;
        if (Membership(objectId, at) == inventory && quantity is > 0 && validatedSimple && at > nonLootMoveUntil
            && localIntroductions.Remove(objectId, out var introduction) && at - introduction.At <= TimeSpan.FromSeconds(2))
            AddGain(new Gain(objectId, quantity.Value, next, at, introduction.Context, true));
        else if (Membership(objectId, at) == inventory && old is not null
            && old.Id == itemId && next.Quantity > old.Quantity && at > nonLootMoveUntil)
            AddGain(new Gain(objectId, next.Quantity - old.Quantity, next, at, tracker.Stamp(ActivityEventKind.Loot, at, null),
                next.ValidAcquisitionQuantity && old.ValidAcquisitionQuantity));
        if (quantity is { } observedQuantity)
            foreach (var candidate in candidates.Where(x => x.ObjectId == objectId && x.Baseline.Quantity > observedQuantity))
                candidate.Decrement = candidate.Baseline.Quantity - observedQuantity;
        foreach (var candidate in candidates.Where(x => x.ObjectId == objectId && x.Transfer is not null && !x.Confirmed).ToArray())
        {
            if (quantity is > 0 && at >= candidate.CreatedAt)
                Confirm(candidate, next, quantity.Value, candidate.Transfer!.CapturedAtUtc, candidate.Transfer.OriginKey, "ExactObjectTransfer");
            else
            {
                var amended = Enrich(candidate.Transfer!, next);
                if (amended != candidate.Transfer)
                {
                    amended = amended with { Revision = candidate.Transfer!.Revision + 1 };
                    tracker.AmendLoot(amended);
                    candidate.Transfer = amended;
                }
            }
        }
        foreach (var pickup in recent.Where(x => x.ObjectId == objectId && x.Event.ItemId == itemId
            && at - x.At <= TimeSpan.FromMinutes(2)).ToArray())
        {
            var amended = Enrich(pickup.Event, next);
            if (amended != pickup.Event)
            {
                amended = amended with { Revision = pickup.Event.Revision + 1 };
                tracker.AmendLoot(amended);
                pickup.Event = amended;
            }
        }
    }

    private void Put(long objectId, Guid destination, DateTime at, string? origin, bool record = true)
    {
        var previous = Membership(objectId, at);
        if (previous != destination) ForgetMembership(objectId);
        if (!record) { membership[objectId] = destination; return; }
        if (destination != inventory)
            candidates.RemoveAll(x => x.ObjectId == objectId &&
                (!TryContainer(destination, at, out var next) || next.SourceId != x.Source.Id));
        if (destination == inventory)
        {
            if (previous != inventory && previous == Guid.Empty && at > nonLootMoveUntil && localIntroductions.Count < 256)
                localIntroductions[objectId] = new LocalIntroduction(at, tracker.Stamp(ActivityEventKind.Loot, at, origin));
            var matches = candidates.Where(x => x.ObjectId == objectId && !x.Confirmed && at - x.CreatedAt <= TimeSpan.FromSeconds(10)).ToArray();
            if (matches.Length == 0 && previous != inventory && candidates.Count < 256
                && TryContainer(previous, at, out var sourceContainer) && TrySource(sourceContainer.SourceId, at, out var source))
            {
                // Server membership can prove the transfer even when its request was not captured.
                var candidate = new Candidate(objectId, previous, source, ItemSnapshot(objectId, at), null, at, tracker.LocalActor);
                candidates.Add(candidate);
                matches = [candidate];
            }
            if (matches.Length == 1 && previous != inventory && (previous == matches[0].ContainerId
                    || TryContainer(previous, at, out var sameSource) && sameSource.SourceId == matches[0].Source.Id
                    || matches[0].Removed))
            {
                // Server membership proves this same item object moved into the local inventory.
                // An unchanged whole stack need not receive another quantity packet after the move.
                var candidate = matches[0];
                var item = ItemSnapshot(objectId, at);
                candidate.Transfer = tracker.RecordLoot(ActivityTrackerService.WithSource(
                    tracker.Stamp(ActivityEventKind.Loot, at, origin), tracker.LocalActor) with
                {
                    Evidence = ActivityEvidence.Supplemental,
                    ItemId = item.Id > 0 ? item.Id : null,
                    ItemName = item.Name ?? (item.Id > 0 ? $"Item {item.Id}" : "Unknown item"),
                    ItemUniqueName = item.UniqueName,
                    Quality = item.Quality,
                    Quantity = 0,
                    LootSourceName = candidate.Source.Name,
                    LootSourceKind = candidate.Source.Kind,
                    DetailsJson = JsonSerializer.Serialize(new
                    { Proof = "ExactObjectTransferAwaitingQuantity", HasConfirmedPickupEvidence = true, SourceObjectId = candidate.Source.Id, ItemObjectId = objectId })
                }, hasConfirmedPickupEvidence: true);
                var wholeStack = candidate.IntentQuantity is null || candidate.IntentQuantity >= item.Quantity;
                if (item.QuantitySeenAt >= candidate.CreatedAt
                    || wholeStack && at - item.QuantitySeenAt <= MetadataLifetime)
                    Confirm(candidate, item, item.Quantity, at, origin, "ExactObjectTransfer");
            }
            else if (previous != destination && at > nonLootMoveUntil
                && (previous == Guid.Empty || TryContainer(previous, at, out var from) && TrySource(from.SourceId, at, out _))
                && GetItem(objectId, at) is { } newItem)
            {
                AddGain(new Gain(objectId, newItem.Quantity, newItem, at, tracker.Stamp(ActivityEventKind.Loot, at, origin),
                    newItem.ValidAcquisitionQuantity && at - newItem.QuantitySeenAt <= TimeSpan.FromSeconds(2)));
                if (newItem.ValidAcquisitionQuantity && newItem.Quantity > 0) localIntroductions.Remove(objectId);
            }
        }
        membership[objectId] = destination;
    }

    private void Removed(long objectId, DateTime at, string? origin, long? sourceId = null, bool record = true)
    {
        if (!record) { ForgetMembership(objectId); return; }
        var bound = Membership(objectId, at);
        if (inventory is { } main && bound == main) membership.Remove(objectId);
        var item = ItemSnapshot(objectId, at);
        var candidate = candidates.SingleOrDefault(x => x.ObjectId == objectId && !x.Confirmed);
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
        }
        else if (sourceId is { } explicitSource && TrySource(explicitSource, at, out _))
            AmbiguousRemoval(explicitSource, item.Id, item.Quantity, at, origin, objectId, item);
        // Inventory deletion alone cannot distinguish moving, splitting, destroying or consuming.
    }

    private void ForgetMembership(long objectId)
    {
        if (!membership.Remove(objectId, out var containerId) || !containers.TryGetValue(containerId, out var container)) return;
        for (var index = 0; index < container.Slots.Length; index++)
            if (container.Slots[index] == objectId) container.Slots[index] = 0;
    }

    private void ConfirmUniqueGains(DateTime at)
    {
        if (at <= nonLootMoveUntil) return;
        foreach (var gain in gains.Where(x => !x.Consumed && at - x.At <= TimeSpan.FromSeconds(3)).ToArray())
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
        var matching = MatchRecent(candidate.Source.Name, item.Id, quantity, candidate.Actor.Id, at);
        if (matching is not null && matching.IsBroadcast)
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
            tracker.AmendLoot(amended);
            matching.Event = amended;
            matching.ObjectId = item.ObjectId;
            matching.Matched = true;
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
        { observation = observation with { Revision = observation.Revision + 1 }; tracker.AmendLoot(observation); }
        else observation = tracker.RecordLoot(observation, preserveEncounterAssociation: original is not null);
        recent.Add(new RecentPickup(observation, item.ObjectId, at, false));
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
        var picker = tracker.ResolveActor(null, packet.PlayerName);
        if (picker.Relationship == ActorRelationship.Self)
            foreach (var gain in gains.Where(x => x.Item.Id == packet.ItemId && x.Quantity == packet.RawAmount
                && Math.Abs((x.At - at).TotalSeconds) <= 3)) gain.Consumed = true;
        var descriptor = itemNames.GetItemById(packet.ItemId);
        var pendingTransfers = candidates.Where(x => !x.Confirmed && x.Transfer is { } transfer
            && transfer.ItemId == packet.ItemId && transfer.SourceActorId == picker.Id
            && transfer.LootSourceName == packet.SourceName && !string.IsNullOrWhiteSpace(packet.SourceName)
            && Math.Abs((at - transfer.CapturedAtUtc).TotalSeconds) <= 3).ToArray();
        if (pendingTransfers.Length == 1)
        {
            var pending = pendingTransfers[0];
            Confirm(pending, ItemSnapshot(pending.ObjectId, at), packet.RawAmount.Value,
                pending.Transfer!.CapturedAtUtc, pending.Transfer.OriginKey, "PickupBroadcast");
            var confirmed = recent.SingleOrDefault(x => x.Event.Id == pending.Transfer.Id);
            if (confirmed is not null) confirmed.Matched = true;
            return;
        }
        var removals = recent.Where(x => x.Event.Evidence == ActivityEvidence.Inferred && x.Event.ItemId == packet.ItemId
            && x.Event.Quantity == packet.RawAmount && x.Event.LootSourceName == packet.SourceName
            && !string.IsNullOrWhiteSpace(packet.SourceName) && Math.Abs((at - x.At).TotalSeconds) <= 3).ToArray();
        if (removals.Length == 1)
        { tracker.RetractLoot(removals[0].Event.Id, removals[0].Event.Revision + 1); recent.Remove(removals[0]); }
        var previous = MatchRecent(packet.SourceName, packet.ItemId, packet.RawAmount.Value, picker.Id, at);
        if (previous is not null && !previous.IsBroadcast)
        {
            previous.Matched = true;
            return; // Supplementary view of the already confirmed exact transfer.
        }
        var observation = ActivityTrackerService.WithSource(tracker.Stamp(ActivityEventKind.Loot, at,
            ActivityTrackerService.Origin(packet, "PickupBroadcast")), picker) with
        {
            ItemId = packet.ItemId,
            ItemName = descriptor.UsName,
            ItemUniqueName = descriptor.UniqueName == "Unknown Item" ? null : descriptor.UniqueName,
            Quantity = packet.RawAmount.Value,
            LootSourceName = packet.SourceName,
            LootSourceKind = "Unknown",
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

    private void AmbiguousRemoval(long sourceId, int itemId, long quantity, DateTime at, string? origin,
        long? objectId = null, Item? item = null, ActivityEvent? original = null)
    {
        if (quantity <= 0 || itemId <= 0 || !TrySource(sourceId, at, out var source)) return;
        if (recent.Any(x => x.ObjectId == objectId && objectId is not null && x.Event.ItemId == itemId
            && Math.Abs((at - x.At).TotalSeconds) <= 3)) return;
        var observation = (original ?? tracker.Stamp(ActivityEventKind.Loot, at, origin)) with
        {
            Evidence = ActivityEvidence.Inferred,
            ItemId = itemId,
            ItemName = $"Item {itemId}",
            Quantity = quantity,
            LootSourceName = source.Name,
            LootSourceKind = source.Kind,
            DetailsJson = JsonSerializer.Serialize(new
            {
                Proof = "SourceRemovalOnly",
                SourceObjectId = sourceId,
                ItemObjectId = objectId,
                Recipient = "Unknown"
            })
        };
        if (item is not null) observation = Enrich(observation, item);
        observation = tracker.RecordLoot(observation);
        recent.Add(new RecentPickup(observation, objectId, at, false));
    }

    private ActivityEvent Enrich(ActivityEvent observation, Item item)
    {
        var quality = observation.Quality ?? item.Quality;
        var changedBasis = observation.Quality is null && quality is > 1;
        var price = changedBasis ? null : observation.EstimatedUnitValue;
        var priceObservedAt = changedBasis ? null : observation.PriceObservedAtUtc;
        if (price is null && quality is > 0 && int.TryParse(observation.Server, out var server))
            price = values.Get(server, item.Id, quality.Value)?.NormalEmv;
        var enriched = observation with
        {
            ItemId = item.Id,
            ItemUniqueName = observation.ItemUniqueName ?? item.UniqueName,
            ItemName = item.Name ?? observation.ItemName,
            Quality = quality,
            EstimatedUnitValue = price,
            PriceObservedAtUtc = priceObservedAt ?? (price is > 0 ? DateTime.UtcNow : null)
        };
        return price is > 0 ? ActivityItemValuationService.WithPrice(enriched, price.Value,
            enriched.PriceObservedAtUtc!.Value, quality ?? 1) : enriched;
    }

    private void PricesChanged(IReadOnlyCollection<ItemEstimatedMarketValueKey> keys)
    {
        var updates = keys.Select(key => (key, price: values.Get(key.ServerId, key.ItemId, key.Quality)))
            .Where(x => x.price?.NormalEmv is > 0)
            .Select(x => new ActivityLootPrice(x.key.ServerId.ToString(CultureInfo.InvariantCulture), x.key.ItemId,
                x.key.Quality, x.price!.Value.NormalEmv, DateTime.UtcNow)).ToArray();
        if (updates.Length > 0) _ = FillPricesAsync(updates);
    }

    private async Task FillPricesAsync(ActivityLootPrice[] updates)
    {
        try { await tracker.FillLootPricesAsync(updates).ConfigureAwait(false); }
        catch (Exception error) { Log.Warning(error, "Could not fill missing activity loot estimates."); }
    }

    public void Expire(DateTime at)
    {
        foreach (var key in localIntroductions.Where(x => at - x.Value.At > TimeSpan.FromSeconds(2)).Select(x => x.Key).ToArray())
            localIntroductions.Remove(key);
        resourceClaims.RemoveAll(x => at - x.At > TimeSpan.FromSeconds(5));
        foreach (var candidate in candidates.Where(x => !x.Confirmed && x.Removed
            && at - (x.RemovedAt ?? x.CreatedAt) > TimeSpan.FromSeconds(3)).ToArray())
        {
            AmbiguousRemoval(candidate.Source.Id, candidate.Baseline.Id, candidate.Decrement ?? candidate.Baseline.Quantity,
                candidate.RemovedAt ?? at, candidate.RemovalOrigin, candidate.ObjectId, candidate.Baseline, candidate.Removal);
            candidate.Confirmed = true;
        }
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
        foreach (var key in items.Where(x => at - x.Value.LastSeen > MetadataLifetime).Select(x => x.Key).ToArray()) items.Remove(key);
        foreach (var key in containers.Where(x => at - x.Value.LastSeen > MetadataLifetime).Select(x => x.Key).ToArray()) containers.Remove(key);
        foreach (var key in sources.Where(x => at - x.Value.LastSeen > MetadataLifetime).Select(x => x.Key).ToArray()) sources.Remove(key);
        foreach (var key in membership.Where(x => x.Value != inventory && !containers.ContainsKey(x.Value))
            .Select(x => x.Key).ToArray()) membership.Remove(key);
    }

    // Only the local inventory proofs used by loot are exposed. Requests, source
    // removals, and ambiguous membership changes cannot confirm fishing rewards.
    internal ActivityAcquisitionEvidence? ClaimResourceAcquisition(int itemId, long quantity, long excludedObjectId,
        DateTime from, DateTime until, long epoch)
    {
        var matches = gains.Where(gain => !gain.Consumed && gain.ValidatedSimple && gain.ObjectId != excludedObjectId
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
    public void ResetRecordingCorrelations() { ResetCandidates(); recent.Clear(); }
    internal void DisruptLocalAcquisition(DateTime at)
    {
        if (nonLootMoveUntil < at.AddSeconds(10)) nonLootMoveUntil = at.AddSeconds(10);
        gains.RemoveAll(x => x.At <= at);
        foreach (var key in localIntroductions.Where(x => x.Value.At <= at).Select(x => x.Key).ToArray())
            localIntroductions.Remove(key);
    }
    public void Reset()
    { ResetCandidates(); items.Clear(); pendingInventoryMetadata.Clear(); sources.Clear(); containers.Clear(); membership.Clear(); recent.Clear(); inventory = null; nonLootMoveUntil = default; lastCacheSweep = default; }
    public void ForgetSession(Guid id) { recent.RemoveAll(x => x.Event.SessionId == id); ResetCandidates(); }
    public void ForgetUnassigned()
    { recent.RemoveAll(x => x.Event.SessionId is null); ResetCandidates(); }
    public void Dispose() => values.EstimatedMarketValuesChanged -= PricesChanged;

    private sealed record Item(long ObjectId, int Id, int? Quality, long Quantity, string? UniqueName, string? Name,
        DateTime LastSeen, DateTime QuantitySeenAt, bool ValidAcquisitionQuantity = false);
    private readonly record struct PendingInventoryItemKey(string Connection, Guid? ReceiverGeneration, short? PeerId,
        int? Challenge, long ObjectId);
    private sealed record Source(long Id, string Name, string Kind, DateTime LastSeen);
    private sealed class Container(Guid id, long sourceId, long[] slots, DateTime lastSeen)
    { public Guid Id { get; } = id; public long SourceId { get; } = sourceId; public long[] Slots { get; } = slots; public DateTime LastSeen { get; set; } = lastSeen; }
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
        public bool Removed { get; set; }
        public bool Confirmed { get; set; }
        public long? Decrement { get; set; }
        public DateTime? RemovedAt { get; set; }
        public string? RemovalOrigin { get; set; }
        public ActivityEvent? Transfer { get; set; }
        public ActivityEvent? Removal { get; set; }
    }
    private sealed class Gain(long objectId, long quantity, Item item, DateTime at, ActivityEvent context, bool validatedSimple = false)
    { public long ObjectId { get; } = objectId; public long Quantity { get; } = quantity; public Item Item { get; } = item; public DateTime At { get; } = at; public ActivityEvent Context { get; } = context; public bool ValidatedSimple { get; } = validatedSimple; public bool Consumed { get; set; } }
    private sealed record LocalIntroduction(DateTime At, ActivityEvent Context);
    private sealed record ResourceClaim(int ItemId, long Quantity, DateTime At);
    private sealed class RecentPickup(ActivityEvent observation, long? objectId, DateTime at, bool isBroadcast)
    { public ActivityEvent Event { get; set; } = observation; public long? ObjectId { get; set; } = objectId; public DateTime At { get; } = at; public bool IsBroadcast { get; } = isBroadcast; public bool Matched { get; set; } }
}

internal sealed record ActivityAcquisitionEvidence(long ObjectId, int? Quality, string? UniqueName, string? Name,
    DateTime CapturedAtUtc, string? OriginKey);

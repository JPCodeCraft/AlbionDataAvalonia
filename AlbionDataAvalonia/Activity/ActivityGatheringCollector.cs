using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

/// <summary>Confirmed harvests and one bounded local fishing attempt.</summary>
internal sealed class ActivityGatheringCollector
{
    private static readonly TimeSpan CompletionGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AttemptLifetime = TimeSpan.FromMinutes(2);
    private readonly ActivityTrackerService tracker;
    private readonly ActivityLootCollector loot;
    private readonly ItemEstimatedMarketValueService values;
    private readonly ItemsIdsService items;
    private FishingAttempt? fishing;

    public ActivityGatheringCollector(ActivityTrackerService tracker, ActivityLootCollector loot,
        ItemEstimatedMarketValueService values, ItemsIdsService items)
    {
        this.tracker = tracker;
        this.loot = loot;
        this.values = values;
        this.items = items;
    }

    public void Observe(HarvestFinishedEvent packet, DateTime at)
    {
        if (!packet.IsValid || !tracker.HasContext) return;
        var actor = tracker.ResolveActor(packet.UserObjectId);
        if (!ActivityTrackerService.Tracked(actor.Relationship)) return;
        var item = Describe(packet.ItemId);
        var identity = ActivityResourceIdentity.FromUniqueName(item.UniqueName);
        var value = ActivityTrackerService.WithSource(tracker.Stamp(ActivityEventKind.Gathered, at,
            ActivityTrackerService.Origin(packet, "Harvest")), actor) with
        {
            ItemId = packet.ItemId,
            ItemUniqueName = item.UniqueName,
            ItemName = item.Name,
            Quality = 1,
            Quantity = packet.TotalQuantity!.Value,
            DetailsJson = JsonSerializer.Serialize(new ActivityResourceDetails
            {
                Method = ActivityResourceMethod.Gathering,
                ResourceObjectId = packet.ResourceObjectId,
                StandardAmount = packet.StandardAmount,
                GatheringBonusAmount = packet.GatheringBonusAmount,
                PremiumBonusAmount = packet.PremiumBonusAmount,
                ResourceFamily = identity.Family,
                Tier = identity.Tier,
                Enchantment = identity.Enchantment,
                ComponentStates = new Dictionary<string, string>
                {
                    ["Actor"] = packet.UserObjectIdState.ToString(),
                    ["Resource"] = packet.ResourceObjectIdState.ToString(),
                    ["Item"] = packet.ItemIdState.ToString(),
                    ["Standard"] = packet.StandardAmountState.ToString(),
                    ["GatheringBonus"] = packet.GatheringBonusAmountState.ToString(),
                    ["PremiumBonus"] = packet.PremiumBonusAmountState.ToString(),
                    ["TotalQuantity"] = packet.TotalQuantityState.ToString()
                }
            })
        };
        if (packet.ResourceObjectId is { } source) value = ActivityTrackerService.WithTarget(value, tracker.ResolveActor(source));
        tracker.RecordResource(Estimate(value));
        if (actor.Relationship == ActorRelationship.Self) loot.NoteHarvestAcquisition(packet.ItemId, value.Quantity, at);
    }

    public void Observe(RewardGrantedEvent packet, DateTime at)
    {
        if (!packet.IsValid || fishing is not { } attempt || at < attempt.StartedAt
            || at - attempt.StartedAt > AttemptLifetime || attempt.CompletedAt is { } completed && at > completed + CompletionGrace) return;
        if (attempt.Rewards.Count >= 8) { attempt.IsAmbiguous = true; return; }
        // RewardGranted has no proven recipient/activity identity. Capture its
        // context now; local inventory and finish evidence are still required.
        var value = ActivityTrackerService.WithSource(tracker.Stamp(ActivityEventKind.FishingReward, at,
            ActivityTrackerService.Origin(packet, "FishingReward")), tracker.LocalActor) with
        { ItemId = packet.ItemId, Quantity = packet.Quantity!.Value };
        attempt.Rewards.Add(value);
    }

    public void Observe(BaseOperation packet, DateTime at)
    {
        switch (packet)
        {
            case FishingStartRequest start:
                var previous = fishing;
                Reset();
                if (start.IsValid && tracker.HasContext)
                {
                    if (previous?.CompletedAt is { } completed && at >= completed)
                        Finalize(previous, at < completed + CompletionGrace ? at : completed + CompletionGrace);
                    if (tracker.HasContext)
                        fishing = new FishingAttempt(Guid.NewGuid().ToString("N"), start.UsedRodObjectId, at, tracker.LocalActor, tracker.MapEpoch);
                }
                break;
            case FishingCancelRequest:
                ResolvePending(at);
                Reset();
                break;
            case FishingFinishRequest finish when fishing is { } attempt:
                if (!finish.IsValid || !finish.Succeeded) Reset();
                else if (attempt.FinishRequestedAt is not null) attempt.IsAmbiguous = true;
                else { attempt.SuccessIntent = true; attempt.FinishRequestedAt = at; }
                break;
            case FishingFinishResponse response when fishing is { } attempt:
                if (response.ReturnCode != 0 || !attempt.SuccessIntent || attempt.FinishRequestedAt is not { } requested
                    || at < requested || at - requested > TimeSpan.FromSeconds(10)) Reset();
                else if (attempt.CompletedAt is not null) attempt.IsAmbiguous = true;
                else attempt.CompletedAt = at;
                break;
            case InventoryMoveItemRequest or InventoryMoveGivenItemsRequest:
                ResolvePending(at);
                Reset();
                break;
        }
    }

    public void Expire(DateTime at)
    {
        if (fishing is not { } attempt) return;
        if (!tracker.HasContext || tracker.MapEpoch != attempt.MapEpoch || tracker.LocalActor.Id != attempt.Actor.Id)
        { Reset(); return; }
        // A completed catch is settled even when its attempt outlived the lifetime of an unfinished one.
        if (attempt.CompletedAt is { } completed)
        {
            if (at <= completed + CompletionGrace) return;
            Reset();
            Finalize(attempt, completed + CompletionGrace);
            return;
        }
        if (at - attempt.StartedAt > AttemptLifetime) Reset();
    }

    // Cancel, inventory work, map changes and stopping end the attempt, but a catch the
    // server already completed is kept with the rewards and gains observed so far.
    internal void ResolvePending(DateTime at)
    {
        if (fishing is not { CompletedAt: { } completed } attempt || at < completed) return;
        Reset();
        Finalize(attempt, at < completed + CompletionGrace ? at : completed + CompletionGrace);
    }

    private void Finalize(FishingAttempt attempt, DateTime cutoff)
    {
        if (attempt.IsAmbiguous || !tracker.HasContext || tracker.MapEpoch != attempt.MapEpoch
            || tracker.LocalActor.Id != attempt.Actor.Id || attempt.CompletedAt is not { } completed) return;
        var rewards = attempt.Rewards.Where(x => x.CapturedAtUtc <= cutoff).ToArray();
        foreach (var reward in rewards)
        {
            // Equal rewards or multiple eligible inventory gains cannot be
            // assigned uniquely, even if one looks likely to be the catch.
            if (reward.ItemId is not { } itemId || rewards.Count(x => x.ItemId == itemId && x.Quantity == reward.Quantity) != 1) continue;
            var from = reward.CapturedAtUtc - CompletionGrace;
            if (from < attempt.StartedAt) from = attempt.StartedAt;
            var acquisition = loot.ClaimResourceAcquisition(itemId, reward.Quantity, attempt.RodObjectId,
                from, cutoff, attempt.MapEpoch);
            if (acquisition is null) continue;
            var item = Describe(itemId);
            var uniqueName = acquisition.UniqueName ?? item.UniqueName;
            var identity = ActivityResourceIdentity.FromUniqueName(uniqueName);
            tracker.RecordResource(Estimate(reward with
            {
                ItemName = acquisition.Name ?? item.Name,
                ItemUniqueName = uniqueName,
                Quality = acquisition.Quality,
                DetailsJson = JsonSerializer.Serialize(new ActivityResourceDetails
                {
                    Method = ActivityResourceMethod.Fishing,
                    FishingAttemptId = attempt.Id,
                    InventoryItemObjectId = acquisition.ObjectId,
                    Proof = "SuccessfulFinishAndUniqueLocalInventoryGain",
                    FinishAtUtc = completed,
                    AcquisitionOriginKey = acquisition.OriginKey,
                    ResourceFamily = identity.Family,
                    Tier = identity.Tier,
                    Enchantment = identity.Enchantment
                })
            }));
        }
    }

    private (string? UniqueName, string Name) Describe(int itemId)
    {
        var item = items.GetItemById(itemId);
        return (item.UniqueName == "Unknown Item" ? null : item.UniqueName, item.UsName);
    }

    private ActivityEvent Estimate(ActivityEvent value)
    {
        if (value.Quality is not > 0 || tracker.ServerId is not { } server || value.ItemId is not { } itemId) return value;
        var price = values.Get(server, itemId, value.Quality.Value)?.NormalEmv;
        return price is > 0 ? value with { EstimatedUnitValue = price, PriceObservedAtUtc = value.CapturedAtUtc } : value;
    }

    public void Reset() => fishing = null;
    public void InventoryIntent(DateTime at)
    {
        if (fishing is not { } attempt || at < attempt.StartedAt) return;
        ResolvePending(at);
        Reset();
    }

    private sealed class FishingAttempt(string id, long rodObjectId, DateTime startedAt, ActivityActor actor, long mapEpoch)
    {
        public string Id { get; } = id;
        public long RodObjectId { get; } = rodObjectId;
        public DateTime StartedAt { get; } = startedAt;
        public ActivityActor Actor { get; } = actor;
        public long MapEpoch { get; } = mapEpoch;
        public bool SuccessIntent { get; set; }
        public bool IsAmbiguous { get; set; }
        public DateTime? FinishRequestedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<ActivityEvent> Rewards { get; } = new();
    }
}

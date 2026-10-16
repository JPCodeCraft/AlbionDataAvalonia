using AlbionDataAvalonia.Network.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Activity;

internal sealed record ActivityReservedPickup(long SourceId, int ItemId, int? Quality, long Amount,
    IReadOnlyCollection<Guid> Owners, string Proof);

// Party chests reserve each member's loot until the chest turns public. Other members'
// reserved stacks arrive only as item types and amounts, and their removals are only sent
// while our chest view is open. The ledger keeps what is still reserved for others, so a
// removal made while the view was closed is recovered when the chest is reopened or empties.
internal sealed class ActivityPartyChestLedger
{
    private const int EmptyLootChestState = 8;
    private readonly Dictionary<long, Chest> chests = new();

    public IReadOnlyList<ActivityReservedPickup> ObserveList(PartyLootItemsEvent party, DateTime at)
    {
        if (party.SourceObjectId <= 0) return [];
        var listed = new Dictionary<int, Stack>();
        for (var index = 0; index < party.ItemIds.Count; index++)
        {
            var amount = index < party.Amounts.Count ? party.Amounts[index] : 0;
            if (party.ItemIds[index] <= 0 || amount <= 0) continue;
            var quality = index < party.Qualities.Count && party.Qualities[index] > 0 ? party.Qualities[index] : (int?)null;
            listed[party.ItemIds[index]] = listed.TryGetValue(party.ItemIds[index], out var stack)
                ? stack with { Amount = stack.Amount + amount } : new Stack(amount, quality);
        }
        var chest = ChestFor(party.SourceObjectId, at);
        var taken = Array.Empty<ActivityReservedPickup>();
        // A list sent on reopening shows what other members took while our view was closed.
        if (!chest.Viewing && !chest.IsPublic && chest.Reserved.Count > 0)
            taken = chest.Reserved.Select(x => new ActivityReservedPickup(party.SourceObjectId, x.Key, x.Value.Quality,
                    x.Value.Amount - (listed.TryGetValue(x.Key, out var still) ? still.Amount : 0), chest.ClosedWith.ToArray(),
                    "PartyLootMissingOnReopen"))
                .Where(x => x.Amount > 0).ToArray();
        chest.Reserved = listed;
        chest.Viewing = true;
        chest.Finished.Clear();
        return taken;
    }

    public void ObserveTypeRemoval(long sourceId, int itemId, long amount)
    {
        if (!chests.TryGetValue(sourceId, out var chest) || !chest.Reserved.TryGetValue(itemId, out var stack)) return;
        if (stack.Amount <= amount) chest.Reserved.Remove(itemId);
        else chest.Reserved[itemId] = stack with { Amount = stack.Amount - amount };
    }

    public void ObserveClosed(long sourceId)
    {
        if (!chests.TryGetValue(sourceId, out var chest)) return;
        chest.Viewing = false;
        chest.ClosedWith = chest.WithLoot.ToHashSet();
        chest.Finished.Clear();
    }

    public IReadOnlyList<ActivityReservedPickup> ObserveUpdate(UpdateLootChestEvent updated, Guid? localGuid, DateTime at)
    {
        if (updated.ObjectId <= 0 || !updated.HasRemainingLootGuids) return [];
        var chest = ChestFor(updated.ObjectId, at);
        var withLoot = updated.RemainingLootGuids.Where(guid => guid != localGuid).ToHashSet();
        if (updated.EligibleGuids.Count > 0) chest.Eligible = true;
        else if (chest.Eligible)
        {
            // Both lists clear when reserved loot turns public; anyone may take what is left.
            chest.IsPublic = true;
            chest.Reserved.Clear();
            chest.WithLoot.Clear();
            return [];
        }
        var taken = Array.Empty<ActivityReservedPickup>();
        if (!chest.Viewing && !chest.IsPublic)
        {
            // A member leaving the list has taken all of their reserved loot.
            chest.Finished.UnionWith(chest.WithLoot.Where(guid => !withLoot.Contains(guid)));
            if (chest.Reserved.Count > 0 && (withLoot.Count == 0 || updated.State == EmptyLootChestState))
            {
                var owners = (chest.Finished.Count > 0 ? chest.Finished : chest.ClosedWith).ToArray();
                taken = chest.Reserved.Select(x => new ActivityReservedPickup(updated.ObjectId, x.Key, x.Value.Quality, x.Value.Amount,
                    owners, "PartyLootTakenWhileClosed")).ToArray();
                chest.Reserved.Clear();
            }
        }
        chest.WithLoot = withLoot;
        return taken;
    }

    public int? Quality(long sourceId, int itemId) =>
        chests.TryGetValue(sourceId, out var chest) && chest.Reserved.TryGetValue(itemId, out var stack) ? stack.Quality : null;

    public bool IsReserved(long sourceId, int itemId) =>
        chests.TryGetValue(sourceId, out var chest) && chest.Reserved.ContainsKey(itemId);

    public void Expire(DateTime at, TimeSpan lifetime)
    {
        foreach (var key in chests.Where(x => at - x.Value.LastSeen > lifetime).Select(x => x.Key).ToArray()) chests.Remove(key);
    }

    public void Clear() => chests.Clear();

    private Chest ChestFor(long sourceId, DateTime at)
    {
        if (!chests.TryGetValue(sourceId, out var chest))
        {
            if (chests.Count >= 256) chests.Remove(chests.MinBy(x => x.Value.LastSeen).Key);
            chests[sourceId] = chest = new Chest();
        }
        chest.LastSeen = at;
        return chest;
    }

    private sealed record Stack(long Amount, int? Quality);

    private sealed class Chest
    {
        // Stacks reserved for other members that are still in the chest, by item type.
        public Dictionary<int, Stack> Reserved { get; set; } = new();
        // Other members whose reserved loot is still in the chest.
        public HashSet<Guid> WithLoot { get; set; } = new();
        // Other members with loot when our view closed, and those who finished since.
        public HashSet<Guid> ClosedWith { get; set; } = new();
        public HashSet<Guid> Finished { get; } = new();
        public bool Viewing { get; set; }
        public bool Eligible { get; set; }
        public bool IsPublic { get; set; }
        public DateTime LastSeen { get; set; }
    }
}

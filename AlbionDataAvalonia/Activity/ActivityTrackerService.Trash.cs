using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using Serilog;
using System;
using System.Linq;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

// Items the captured character destroys come off the Loot and Gathering EMVs they were counted in.
public sealed partial class ActivityTrackerService
{
    private readonly ActivityTrashLedger trashLedger = new();

    // The open run, when it belongs to the running play session.
    private Guid? CurrentPlayRun => session is { } run && playSession is { } play && run.PlaySessionId == play.Id ? run.Id : null;

    private void ObserveAcquisition(ActivityEvent value) => trashLedger.Observe(value, playSession?.Id, CurrentPlayRun);

    // The game client asks to destroy an item when the player confirms the trash can. Parameter 0 is the
    // item's object id (seen in game); the removal of that object that follows is the trashed item.
    internal void ObserveDestroyRequest(RequestPacket packet)
    {
        lock (sync)
        {
            var at = Utc(packet.CapturedAt);
            long? objectId = packet.Parameters.TryGetValue(0, out var raw) ? raw switch
            {
                byte x => x, short x => x, int x => x, long x => x, _ => null
            } : null;
            Log.Debug("Activity: destroy item request for object {ObjectId}", objectId);
            if (disposed || !CanRecord || at < mapEnteredAt || !SameConnection(packet.ConnectionId, packet.MessageIdentity)) return;
            loot.ObserveDestroyRequest(objectId, at);
        }
    }

    /// <summary>
    /// The captured character destroyed quantity of an item. Only what this play session picked up, gathered or
    /// fished of it comes off, at the EMV it was counted with, as a Trashed event in its run.
    /// </summary>
    internal void RecordTrashed(int itemId, string? uniqueName, string? name, int? quality, long quantity, long objectId,
        DateTime at, string? origin, string proof)
    {
        if (!CanRecord || playSession is not { } play || itemId <= 0 || quantity <= 0)
        {
            Log.Debug("Activity: trashed item {ItemId} x{Quantity} outside a session", itemId, quantity);
            return;
        }
        var parts = trashLedger.Take(play.Id, itemId, quality, quantity);
        if (parts.Count == 0)
        {
            Log.Debug("Activity: trashed {Item} x{Quantity} was not picked up in this session", uniqueName ?? itemId.ToString(), quantity);
            return;
        }
        var index = 0;
        // A trashed item belongs to the open run, else to the run that acquired it.
        foreach (var group in parts.GroupBy(x => (x.Kind, Run: CurrentPlayRun ?? x.RunId)))
        {
            var amount = group.Sum(x => x.Quantity);
            var unit = group.All(x => x.UnitValue is not null)
                ? (long)Math.Round(group.Sum(x => (double)x.UnitValue!.Value * x.Quantity) / amount) : (long?)null;
            var blackMarket = group.Select(x => x.BlackMarket).Distinct().ToArray() is [var only] ? only : null;
            var latest = group.First();
            var value = WithSource(Stamp(ActivityEventKind.Trashed, at, index == 0 ? origin : $"{origin}:{index}"), actors.Local) with
            {
                SessionId = group.Key.Run,
                EncounterId = null,
                ItemId = itemId,
                ItemUniqueName = latest.UniqueName ?? uniqueName,
                ItemName = string.IsNullOrWhiteSpace(latest.Name) ? name ?? "" : latest.Name,
                Quality = quality ?? latest.Quality,
                Quantity = amount,
                LootSourceName = "Trashed",
                LootSourceKind = group.Key.Kind.ToString(),
                DetailsJson = JsonSerializer.Serialize(new { Proof = proof, ItemObjectId = objectId, DestroyedQuantity = quantity })
            };
            value = unit is { } price
                ? ActivityItemValuationService.WithPrice(value, price, at, ActivityItemValuationService.BasisQuality(value), blackMarket)
                : valuations.Observe(value);
            Log.Debug("Activity: trashed {Item} x{Quantity} of x{Destroyed} comes off {Kind}", value.ItemUniqueName, amount, quantity, group.Key.Kind);
            Persist(value);
            index++;
        }
    }
}

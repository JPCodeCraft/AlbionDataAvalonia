using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Activity;

/// <summary>
/// What the play session picked up, gathered and fished, by item and quality, so a trashed item only takes back
/// what the session added: trashing food or gear brought from town changes nothing. Item object ids change with
/// every map, so items match by kind and quality. Kept in memory; a session continued after the app restarts
/// starts it empty.
/// </summary>
internal sealed class ActivityTrashLedger
{
    private const int MaximumEntries = 20_000;
    private readonly Dictionary<Guid, Entry> entries = new();
    private readonly HashSet<Guid> runs = new();
    private Guid? playSessionId;

    public int Count => entries.Count;

    /// <summary>Records or replaces a saved event; currentRun is the open run of playSession, if any.</summary>
    public void Observe(ActivityEvent value, Guid? playSession, Guid? currentRun)
    {
        if (value.Kind is not (ActivityEventKind.Loot or ActivityEventKind.Gathered or ActivityEventKind.FishingReward)) return;
        if (playSession is not { } id) return;
        Begin(id);
        if (currentRun is { } run) runs.Add(run);
        if (value.IsRetracted || value.Evidence != ActivityEvidence.Confirmed || value.SourceRelationship != ActorRelationship.Self
            || value.ItemId is not > 0 || value.Quantity <= 0 || value.SessionId is not { } runId || !runs.Contains(runId))
        {
            entries.Remove(value.Id);
            return;
        }
        var trashed = entries.TryGetValue(value.Id, out var old) ? Math.Min(old.Trashed, value.Quantity) : 0;
        if (old is null && entries.Count >= MaximumEntries) return;
        entries[value.Id] = new Entry(value.Kind, value.ItemId.Value, value.Quality, value.ItemUniqueName, value.ItemName,
            value.Quantity, value.EstimatedUnitValue, ActivityItemValuationService.IsBlackMarketEmv(value), runId, value.CapturedAtUtc)
        { Trashed = trashed };
    }

    public void Retract(Guid id) => entries.Remove(id);

    /// <summary>
    /// Takes up to quantity of an item back from what this play session acquired, latest first, and returns
    /// the parts taken. An entry without a quality (a pickup that never showed it) matches any quality last.
    /// </summary>
    public IReadOnlyList<Part> Take(Guid playSession, int itemId, int? quality, long quantity)
    {
        if (playSessionId != playSession || quantity <= 0) return [];
        var parts = new List<Part>();
        var left = quantity;
        foreach (var entry in entries.Values.Where(x => x.ItemId == itemId && x.Remaining > 0 && (x.Quality == quality || x.Quality is null || quality is null))
                     .OrderByDescending(x => x.Quality == quality).ThenByDescending(x => x.At).ToArray())
        {
            var taken = Math.Min(left, entry.Remaining);
            entry.Trashed += taken;
            parts.Add(new Part(entry.Kind, entry.RunId, taken, entry.UnitValue, entry.BlackMarket, entry.Quality, entry.UniqueName, entry.Name));
            left -= taken;
            if (left == 0) break;
        }
        return parts;
    }

    private void Begin(Guid playSession)
    {
        if (playSessionId == playSession) return;
        entries.Clear();
        runs.Clear();
        playSessionId = playSession;
    }

    private sealed record Entry(ActivityEventKind Kind, int ItemId, int? Quality, string? UniqueName, string Name, long Quantity,
        long? UnitValue, bool? BlackMarket, Guid RunId, DateTime At)
    {
        public long Trashed { get; set; }
        public long Remaining => Quantity - Trashed;
    }

    internal sealed record Part(ActivityEventKind Kind, Guid RunId, long Quantity, long? UnitValue, bool? BlackMarket, int? Quality,
        string? UniqueName, string Name);
}

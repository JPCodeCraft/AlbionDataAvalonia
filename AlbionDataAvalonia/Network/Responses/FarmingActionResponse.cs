using Albion.Network;
using AlbionDataAvalonia.Farming.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Responses;

public sealed class FarmingActionResponse : BaseOperation
{
    public long? RequestId { get; private set; }
    public List<FarmingPickupItem> Items { get; private set; } = [];

    public FarmingActionResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (parameters.ContainsKey(255)) RequestId = Number(parameters, 255);
        });
        // Preserve correlation even when an unknown reward shape cannot be decoded.
        // Successful removal still needs to clear the observed slot in that case.
        TryRead(() =>
        {
            var names = Strings(parameters, 0);
            var amounts = parameters.TryGetValue(1, out var raw) ? raw.ToLongArray() : [];
            if (names.Length > 0 && names.Length <= 32 && names.Length == amounts.Length
                && names.All(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 200)
                && amounts.All(n => n > 0 && n <= 1_000_000_000))
                Items = names.Select((name, i) => new FarmingPickupItem { UniqueName = name, Quantity = (int)amounts[i] }).ToList();
        });
    }
}

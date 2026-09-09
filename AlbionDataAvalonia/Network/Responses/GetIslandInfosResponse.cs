using Albion.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Responses;

public sealed class GetIslandInfosResponse : BaseOperation
{
    public bool IsValid { get; private set; }
    public Guid[] IslandIds { get; private set; } = [];
    public string?[] OwnerNames { get; private set; } = [];
    public string?[] HomeClusters { get; private set; } = [];

    public GetIslandInfosResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (!parameters.TryGetValue(0, out var raw) || raw is not byte[] bytes || bytes.Length % 16 != 0) return;
            var ids = raw.ToGuidArray();
            var owners = Strings(parameters, 3);
            var homes = Strings(parameters, 1);
            if (owners.Length != ids.Length || homes.Length != ids.Length) return;
            IslandIds = ids;
            OwnerNames = owners.Select(name => OptionalText(name, 100)).ToArray();
            HomeClusters = homes.Select(home => OptionalText(home, 200)).ToArray();
            IsValid = true;
        });
    }
}

using Albion.Network;
using System;
using System.Collections.Generic;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Responses;

public sealed class ChangeClusterResponse : BaseOperation
{
    public string? Destination { get; }
    public string? LayoutId { get; }
    public string? OwnerName { get; }

    public ChangeClusterResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Destination = Text(parameters, 0);
        var layout = OptionalText(Text(parameters, 1), 100);
        LayoutId = layout?.StartsWith("ISLAND-", StringComparison.Ordinal) == true ? layout : null;
        OwnerName = OptionalText(Text(parameters, 2), 100);
    }
}

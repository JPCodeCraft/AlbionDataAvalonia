using Albion.Network;
using System;
using System.Collections.Generic;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Events;

public sealed class FarmBuildingInfoEvent : BaseEvent
{
    public long? ObjectId { get; private set; }
    public DateTime? RenovationStartedAt { get; private set; }
    public DateTime? RenovationEndsAt { get; private set; }

    public FarmBuildingInfoEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (!parameters.ContainsKey(0)) return;
            var id = Number(parameters, 0);
            var start = UtcTicks(Number(parameters, 4));
            var end = UtcTicks(Number(parameters, 5));
            ObjectId = id;
            RenovationStartedAt = start;
            RenovationEndsAt = end;
        });
    }
}

using Albion.Network;
using System.Collections.Generic;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Requests;

public sealed class BuildingRenovationRequest : BaseOperation
{
    public long? ObjectId { get; private set; }
    public int? State { get; private set; }

    public BuildingRenovationRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (!parameters.ContainsKey(0) || !parameters.ContainsKey(1)) return;
            var id = Number(parameters, 0);
            var state = Number(parameters, 1);
            if (state is < 0 or > 255) return;
            ObjectId = id;
            State = (int)state;
        });
    }
}

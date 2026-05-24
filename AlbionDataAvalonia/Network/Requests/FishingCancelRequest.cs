using Albion.Network;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

public class FishingCancelRequest : BaseOperation
{
    public FishingCancelRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
    }
}


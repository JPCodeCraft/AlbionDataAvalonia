using Albion.Network;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class FishingFinishResponse : BaseOperation
{
    public FishingFinishResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
    }
}


using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class AuctionSellSpecificItemRequestResponse : BaseOperation
{
    public bool success = true;

    public AuctionSellSpecificItemRequestResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

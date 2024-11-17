using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class AuctionSellSpecificItemRequestResponse : BaseOperation
{
    public bool success = false;

    public AuctionSellSpecificItemRequestResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(0, out object? _success))
            {
                if (_success is bool successValue)
                {
                    success = successValue;
                }
                else
                {
                    Log.Debug("No success value found in parameters.");
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

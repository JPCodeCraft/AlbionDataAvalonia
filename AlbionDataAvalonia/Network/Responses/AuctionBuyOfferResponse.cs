using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class AuctionBuyOfferResponse : BaseOperation
{
    public bool success = true;

    public AuctionBuyOfferResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            // apparently, there's no key 0 to pass success anymore, so we assume always success, like the sell response
            // if (parameters.TryGetValue(0, out object? _success))
            // {
            //     if (_success is bool successValue)
            //     {
            //         success = successValue;
            //     }
            //     else
            //     {
            //         Log.Debug("No success value found in parameters.");
            //     }
            // }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

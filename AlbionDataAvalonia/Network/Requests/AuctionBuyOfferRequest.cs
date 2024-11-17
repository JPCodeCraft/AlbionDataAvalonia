using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

public class AuctionBuyOfferRequest : BaseOperation
{
    public int amount;
    public ulong orderId;

    public AuctionBuyOfferRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(0, out object? id))
            {
            }

            if (parameters.TryGetValue(1, out object? amount))
            {
                this.amount = Convert.ToInt32(amount);
            }

            if (parameters.TryGetValue(2, out object? orderId))
            {
                this.orderId = Convert.ToUInt64(orderId);
            }
        }

        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }

    }
}

using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class ReadMailResponse : BaseOperation
{
    public ReadMailResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Debug("Got {PacketType} packet.", GetType());
        try
        {
            //if (parameters.TryGetValue(0, out object? orders))
            //{
            //    foreach (var auctionOfferString in (IEnumerable<string>)orders ?? new List<string>())
            //    {
            //        var marketOrder = JsonSerializer.Deserialize<MarketOrder>(auctionOfferString);
            //        if (marketOrder == null) continue;
            //        marketOrders.Add(marketOrder);
            //    }
            //}
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

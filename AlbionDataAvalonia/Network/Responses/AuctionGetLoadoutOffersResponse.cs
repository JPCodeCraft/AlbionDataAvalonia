using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace AlbionDataAvalonia.Network.Responses;

public class AuctionGetLoadoutOffersResponse : BaseOperation
{
    public List<MarketOrder> marketOrders = new();

    public AuctionGetLoadoutOffersResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(1, out object? orders))
            {
                foreach (var auctionOfferString in ((string[][])orders).SelectMany(x => x).ToList())
                {
                    var order = JsonSerializer.Deserialize<MarketOrder>(auctionOfferString);
                    if (order == null) continue;
                    marketOrders.Add(order);
                }
            }

            //if (parameters.TryGetValue(2, out object buyQuantityNumbers))
            //{
            //    //we don't use the buy quantities
            //}
        }
        catch (Exception e)
        {
            Log.Error(e, "{message}", MethodBase.GetCurrentMethod()?.DeclaringType);
        }
    }
}

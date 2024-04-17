using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AlbionDataAvalonia.Network.Responses;

public class AuctionGetRequestsResponse : BaseOperation
{
    public List<MarketOrder> marketOrders = new();

    public AuctionGetRequestsResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Debug("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? orders))
            {
                foreach (var auctionOfferString in (IEnumerable<string>)orders ?? new List<string>())
                {
                    var marketOrder = JsonSerializer.Deserialize<MarketOrder>(auctionOfferString);
                    if (marketOrder == null) continue;
                    marketOrders.Add(marketOrder);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class RewardGrantedEvent : BaseEvent
{
    public int ItemId { get; }
    public int Quantity { get; }

    public RewardGrantedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(1, out var itemId))
            {
                ItemId = itemId.ToInt();
            }

            if (parameters.TryGetValue(3, out var quantity))
            {
                Quantity = quantity.ToInt();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}


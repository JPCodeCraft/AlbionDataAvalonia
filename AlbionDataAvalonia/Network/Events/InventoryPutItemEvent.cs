using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class InventoryPutItemEvent : BaseEvent
{
    public long ItemObjectId { get; }

    public InventoryPutItemEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var itemObjectId))
            {
                ItemObjectId = itemObjectId.ToLong();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

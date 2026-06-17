using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class LootChestOpenedEvent : BaseEvent
{
    public long ObjectId { get; }

    public LootChestOpenedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var objectId))
            {
                ObjectId = objectId.ToLong();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

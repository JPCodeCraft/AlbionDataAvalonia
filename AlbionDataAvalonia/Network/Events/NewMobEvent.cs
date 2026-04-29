using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class NewMobEvent : BaseEvent
{
    public long? ObjectId { get; }
    public int? MobIndex { get; }

    public NewMobEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(1, out object? mobIndex))
            {
                MobIndex = mobIndex.ToInt();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

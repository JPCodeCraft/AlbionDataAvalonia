using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class RedZoneWorldMapEvent : BaseEvent
{
    public long EventTime { get; }
    public int Phase { get; }

    public RedZoneWorldMapEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? eventTime))
            {
                EventTime = eventTime.ToLong();
            }

            if (parameters.TryGetValue(1, out object? phase))
            {
                Phase = phase.ToInt();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

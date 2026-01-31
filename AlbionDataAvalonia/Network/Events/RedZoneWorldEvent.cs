using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class RedZoneWorldEvent : BaseEvent
{
    public long EventTime { get; }
    public bool AdvanceNotice { get; }

    public RedZoneWorldEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? eventTime))
            {
                EventTime = eventTime.ToLong();
            }

            if (parameters.TryGetValue(1, out object? advanceNotice))
            {
                AdvanceNotice = advanceNotice.ToBool();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

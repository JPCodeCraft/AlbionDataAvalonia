using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class TimeSyncEvent : BaseEvent
{
    public long? GameTimeMilliseconds { get; }
    public long? ClientTimeMilliseconds { get; }

    public TimeSyncEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? gameTimeMilliseconds))
            {
                GameTimeMilliseconds = gameTimeMilliseconds.ToLong();
            }

            if (parameters.TryGetValue(1, out object? clientTimeMilliseconds))
            {
                ClientTimeMilliseconds = clientTimeMilliseconds.ToLong();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

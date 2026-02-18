using Albion.Network;
using Serilog;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class DebugEventProbeEvent : BaseEvent
{
    public Dictionary<byte, object> Parameters { get; }

    public DebugEventProbeEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        Parameters = parameters;
    }
}

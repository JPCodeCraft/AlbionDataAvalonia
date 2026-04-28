using Albion.Network;
using Serilog;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class PartyDisbandedEvent : BaseEvent
{
    public PartyDisbandedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
    }
}

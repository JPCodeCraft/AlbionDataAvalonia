using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class PartyPlayerLeftEvent : BaseEvent
{
    public Guid? UserGuid { get; }

    public PartyPlayerLeftEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(1, out object? guid))
            {
                UserGuid = guid.ToGuid();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

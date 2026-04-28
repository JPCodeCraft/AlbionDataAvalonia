using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class PartyPlayerJoinedEvent : BaseEvent
{
    public Guid UserGuid { get; } = Guid.Empty;
    public string Username { get; } = string.Empty;

    public PartyPlayerJoinedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(1, out object? guid))
            {
                UserGuid = guid.ToGuid() ?? Guid.Empty;
            }

            if (parameters.TryGetValue(2, out object? username))
            {
                Username = username?.ToString() ?? string.Empty;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

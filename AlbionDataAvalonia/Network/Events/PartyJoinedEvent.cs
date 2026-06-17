using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Events;

public class PartyJoinedEvent : BaseEvent
{
    public Dictionary<Guid, string> PartyUsers { get; } = new();

    public PartyJoinedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (!parameters.TryGetValue(5, out object? guidData)
                || !parameters.TryGetValue(6, out object? nameData))
            {
                return;
            }

            var guids = guidData.ToGuidArray();
            var names = nameData.ToStringArray();
            var count = Math.Min(guids.Length, names.Length);

            for (var i = 0; i < count; i++)
            {
                if (guids[i] != Guid.Empty && !string.IsNullOrWhiteSpace(names[i]))
                {
                    PartyUsers[guids[i]] = names[i];
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class InCombatStateUpdateEvent : BaseEvent
{
    public long? ObjectId { get; }
    public bool InActiveCombat { get; }
    public bool InPassiveCombat { get; }

    public InCombatStateUpdateEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(1, out object? inActiveCombat))
            {
                InActiveCombat = inActiveCombat.ToBool();
            }

            if (parameters.TryGetValue(2, out object? inPassiveCombat))
            {
                InPassiveCombat = inPassiveCombat.ToBool();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

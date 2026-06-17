using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class PartySetRoleFlagEvent : BaseEvent
{
    public Guid UserGuid { get; } = Guid.Empty;

    public PartySetRoleFlagEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(1, out var userGuid))
            {
                UserGuid = userGuid.ToGuid() ?? Guid.Empty;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class PartyOnClusterPartyJoinedEvent : BaseEvent
{
    public IReadOnlyList<Guid> UserGuids { get; } = Array.Empty<Guid>();

    public PartyOnClusterPartyJoinedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var userGuids))
            {
                UserGuids = userGuids.ToGuidArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

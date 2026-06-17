using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class DetachItemContainerEvent : BaseEvent
{
    public Guid ContainerId { get; } = Guid.Empty;

    public DetachItemContainerEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var containerId))
            {
                ContainerId = containerId.ToGuid() ?? Guid.Empty;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

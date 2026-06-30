using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class InventoryPutItemEvent : BaseEvent
{
    public long ItemObjectId { get; }
    public Guid ContainerId { get; } = Guid.Empty;

    public InventoryPutItemEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var objectId)) ItemObjectId = objectId.ToLong();
            if (parameters.TryGetValue(2, out var containerId)) ContainerId = containerId.ToGuid() ?? Guid.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse InventoryPutItem event");
        }
    }
}

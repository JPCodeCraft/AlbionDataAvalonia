using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

public sealed class InventoryMoveItemRequest : BaseOperation
{
    public int SourceSlot { get; }
    public Guid SourceContainerId { get; } = Guid.Empty;
    public int DestinationSlot { get; }
    public Guid DestinationContainerId { get; } = Guid.Empty;

    public InventoryMoveItemRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var sourceSlot))
            {
                SourceSlot = sourceSlot.ToInt();
            }

            if (parameters.TryGetValue(1, out var sourceContainerId))
            {
                SourceContainerId = sourceContainerId.ToGuid() ?? Guid.Empty;
            }

            if (parameters.TryGetValue(3, out var destinationSlot))
            {
                DestinationSlot = destinationSlot.ToInt();
            }

            if (parameters.TryGetValue(4, out var destinationContainerId))
            {
                DestinationContainerId = destinationContainerId.ToGuid() ?? Guid.Empty;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

using Albion.Network;
using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

public sealed class InventoryMoveGivenItemsRequest : BaseOperation
{
    public Guid SourceContainerId { get; } = Guid.Empty;
    public Guid DestinationContainerId { get; } = Guid.Empty;
    public IReadOnlyList<long> ItemObjectIds { get; } = Array.Empty<long>();

    public InventoryMoveGivenItemsRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var sourceContainerId))
            {
                SourceContainerId = sourceContainerId.ToGuid() ?? Guid.Empty;
            }

            if (parameters.TryGetValue(2, out var destinationContainerId))
            {
                DestinationContainerId = destinationContainerId.ToGuid() ?? Guid.Empty;
            }

            if (parameters.TryGetValue(4, out var itemObjectIds))
            {
                ItemObjectIds = ConvertToLongList(itemObjectIds);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    private static IReadOnlyList<long> ConvertToLongList(object value)
    {
        if (value is string || value is not IEnumerable enumerable)
        {
            return Array.Empty<long>();
        }

        var values = new List<long>();
        foreach (var item in enumerable)
        {
            if (item is not null)
            {
                values.Add(item.ToLong());
            }
        }

        return values;
    }
}

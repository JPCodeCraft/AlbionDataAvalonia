using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class PartyLootItemsRemovedEvent : BaseEvent
{
    public long SourceObjectId { get; }
    public IReadOnlyList<long> ItemObjectIds { get; } = Array.Empty<long>();

    public PartyLootItemsRemovedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var sourceObjectId))
            {
                SourceObjectId = sourceObjectId.ToLong();
            }

            if (parameters.TryGetValue(1, out var itemObjectIds))
            {
                ItemObjectIds = itemObjectIds.ToLongArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

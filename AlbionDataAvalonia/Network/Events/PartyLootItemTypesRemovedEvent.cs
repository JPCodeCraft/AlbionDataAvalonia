using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class PartyLootItemTypesRemovedEvent : BaseEvent
{
    public long SourceObjectId { get; }
    public IReadOnlyList<int> ItemIds { get; } = Array.Empty<int>();
    public IReadOnlyList<int> Amounts { get; } = Array.Empty<int>();
    public IReadOnlyList<int> Qualities { get; } = Array.Empty<int>();

    public PartyLootItemTypesRemovedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var sourceObjectId))
            {
                SourceObjectId = sourceObjectId.ToLong();
            }

            if (parameters.TryGetValue(1, out var itemIds))
            {
                ItemIds = itemIds.ToIntArray();
            }

            if (parameters.TryGetValue(4, out var amounts))
            {
                Amounts = amounts.ToIntArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

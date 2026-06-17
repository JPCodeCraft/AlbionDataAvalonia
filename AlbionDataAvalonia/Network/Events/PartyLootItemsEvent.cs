using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class PartyLootItemsEvent : BaseEvent
{
    public long SourceObjectId { get; }
    public IReadOnlyList<long> ItemObjectIds { get; } = Array.Empty<long>();
    public IReadOnlyList<int> ItemIds { get; } = Array.Empty<int>();
    public IReadOnlyList<int> Qualities { get; } = Array.Empty<int>();
    public IReadOnlyList<int> Amounts { get; } = Array.Empty<int>();
    public IReadOnlyList<string> PlayerNames { get; } = Array.Empty<string>();

    public PartyLootItemsEvent(Dictionary<byte, object> parameters) : base(parameters)
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

            if (parameters.TryGetValue(2, out var itemIds))
            {
                ItemIds = itemIds.ToIntArray();
            }

            if (parameters.TryGetValue(4, out var qualities))
            {
                Qualities = qualities.ToIntArray();
            }

            if (parameters.TryGetValue(9, out var amounts))
            {
                Amounts = amounts.ToIntArray();
            }

            if (parameters.TryGetValue(10, out var playerNames))
            {
                PlayerNames = playerNames.ToStringArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

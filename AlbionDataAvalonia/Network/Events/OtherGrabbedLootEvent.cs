using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class OtherGrabbedLootEvent : BaseEvent
{
    public long ObjectId { get; }
    public string SourceName { get; } = string.Empty;
    public string PlayerName { get; } = string.Empty;
    public bool IsSilver { get; }
    public int ItemId { get; }
    public int Amount { get; }

    public OtherGrabbedLootEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(1, out var sourceName))
            {
                SourceName = sourceName.ToString() ?? string.Empty;
            }

            if (parameters.TryGetValue(2, out var playerName))
            {
                PlayerName = playerName.ToString() ?? string.Empty;
            }

            if (parameters.TryGetValue(3, out var isSilver))
            {
                IsSilver = isSilver.ToBool();
            }

            if (parameters.TryGetValue(4, out var itemId))
            {
                ItemId = itemId.ToInt();
            }

            if (parameters.TryGetValue(5, out var amount))
            {
                Amount = amount.ToInt();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class NewLootChestEvent : BaseEvent
{
    public long ObjectId { get; }
    public string UniqueName { get; } = string.Empty;
    public string UniqueNameWithLocation { get; } = string.Empty;

    public NewLootChestEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(3, out var uniqueName))
            {
                UniqueName = uniqueName.ToString() ?? string.Empty;
            }

            if (parameters.TryGetValue(4, out var uniqueNameWithLocation))
            {
                UniqueNameWithLocation = uniqueNameWithLocation.ToString() ?? string.Empty;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

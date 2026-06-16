using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class NewLootEvent : BaseEvent
{
    public long ObjectId { get; }
    public string SourceName { get; } = string.Empty;

    public NewLootEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(3, out var sourceName))
            {
                SourceName = sourceName.ToString() ?? string.Empty;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

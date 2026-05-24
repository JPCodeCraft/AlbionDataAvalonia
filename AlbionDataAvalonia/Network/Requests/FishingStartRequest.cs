using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

public class FishingStartRequest : BaseOperation
{
    public long EventId { get; }
    public long UsedRodObjectId { get; }

    public FishingStartRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var eventId))
            {
                EventId = eventId.ToLong();
            }

            if (parameters.TryGetValue(2, out var usedRodObjectId))
            {
                UsedRodObjectId = usedRodObjectId.ToLong();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}


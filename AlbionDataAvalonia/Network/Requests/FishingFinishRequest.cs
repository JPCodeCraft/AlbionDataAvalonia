using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

public class FishingFinishRequest : BaseOperation
{
    public bool Succeeded { get; }

    public FishingFinishRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(1, out var succeeded))
            {
                Succeeded = succeeded.ToBool();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}


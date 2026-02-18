using Albion.Network;
using Serilog;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class DebugResponseProbeResponse : BaseOperation
{
    public Dictionary<byte, object> Parameters { get; }

    public DebugResponseProbeResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        Parameters = parameters;
    }
}

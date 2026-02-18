using Albion.Network;
using Serilog;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

public class DebugRequestProbeRequest : BaseOperation
{
    public Dictionary<byte, object> Parameters { get; }

    public DebugRequestProbeRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        Parameters = parameters;
    }
}

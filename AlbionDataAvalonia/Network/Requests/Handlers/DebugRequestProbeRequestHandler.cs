using Albion.Network;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugRequestProbeRequestHandler : RequestPacketHandler<DebugRequestProbeRequest>
{
    // Change this single constant to probe a different request operation.
    public const OperationCodes ProbeOperationCode = OperationCodes.ActionOnBuildingStart;

    public DebugRequestProbeRequestHandler() : base((int)ProbeOperationCode)
    {
    }

    protected override Task OnActionAsync(DebugRequestProbeRequest value)
    {
        Log.Debug(
            "Debug probe captured request {OperationCode} ({OperationName}) with {ParameterCount} parameter(s).",
            (int)ProbeOperationCode,
            ProbeOperationCode,
            value.Parameters.Count);

        foreach (var parameter in value.Parameters.OrderBy(x => x.Key))
        {
            Log.Debug(
                "Debug probe request param key={Key} type={Type} value={@Value}",
                parameter.Key,
                parameter.Value?.GetType().FullName ?? "null",
                parameter.Value);
        }

        return Task.CompletedTask;
    }
}

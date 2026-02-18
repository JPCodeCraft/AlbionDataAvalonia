using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugResponseProbeResponseHandler : PacketHandler<ResponsePacket>
{
    // Change this single constant to probe a different response operation.
    public const OperationCodes ProbeOperationCode = OperationCodes.Join;

    protected override Task OnHandleAsync(ResponsePacket packet)
    {
        if (packet.OperationCode != (int)ProbeOperationCode)
        {
            return NextAsync(packet);
        }

        var response = new DebugResponseProbeResponse(packet.Parameters);
        Log.Information(
            "Debug probe captured response {OperationCode} ({OperationName}) with {ParameterCount} parameter(s).",
            (int)ProbeOperationCode,
            ProbeOperationCode,
            response.Parameters.Count);

        foreach (var parameter in response.Parameters)
        {
            Log.Information(
                "Debug probe response param key={Key} type={Type} value={@Value}",
                parameter.Key,
                parameter.Value?.GetType().FullName ?? "null",
                parameter.Value);
        }

        // Keep normal response handlers running (e.g., JoinResponseHandler).
        return NextAsync(packet);
    }
}

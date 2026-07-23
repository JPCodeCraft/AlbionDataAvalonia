using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugResponseProbeResponseHandler : PacketHandler<ResponsePacket>
{
    private static readonly int[] ProbeOperationCodeValues =
    [
        (int)OperationCodes.Join,
        (int)OperationCodes.GetClusterData,
        (int)OperationCodes.ChangeCluster,
        (int)OperationCodes.GetClusterMapInfo,
    ];

    protected override Task OnHandleAsync(ResponsePacket packet)
    {
        if (!ProbeOperationCodeValues.Contains(packet.OperationCode))
        {
            return NextAsync(packet);
        }

        var response = new DebugResponseProbeResponse(packet.Parameters);
        Log.Debug(
            "Debug probe captured response {OperationCode} ({OperationName}) with {ParameterCount} parameter(s): {Parameters}",
            packet.OperationCode,
            System.Enum.GetName(typeof(OperationCodes), packet.OperationCode) ?? "Unknown",
            response.Parameters.Count,
            DebugProbeFormatter.FormatParameters(response.Parameters));

        return NextAsync(packet);
    }

}

using Albion.Network;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugRequestProbeRequestHandler : PacketHandler<RequestPacket>
{
    private static readonly int[] ProbeOperationCodeValues =
    [
        (int)OperationCodes.ContainerOpen,
        (int)OperationCodes.ContainerClose,
        (int)OperationCodes.InventoryMoveItem,
        (int)OperationCodes.InventoryMoveGivenItems
    ];

    protected override Task OnHandleAsync(RequestPacket packet)
    {
        if (!ProbeOperationCodeValues.Contains(packet.OperationCode))
        {
            return NextAsync(packet);
        }

        var request = new DebugRequestProbeRequest(packet.Parameters);
        Log.Debug(
            "Debug probe captured request {OperationCode} ({OperationName}) with {ParameterCount} parameter(s): {Parameters}",
            packet.OperationCode,
            System.Enum.GetName(typeof(OperationCodes), packet.OperationCode) ?? "Unknown",
            request.Parameters.Count,
            DebugProbeFormatter.FormatParameters(request.Parameters));

        return NextAsync(packet);
    }

}

using Albion.Network;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugRequestProbeRequestHandler : PacketHandler<RequestPacket>
{
    private static readonly OperationCodes[] ProbeOperationCodes =
    [
        OperationCodes.ContainerOpen,
        OperationCodes.ContainerClose,
        OperationCodes.InventoryAddToStacks,
        OperationCodes.InventoryStack,
        OperationCodes.InventoryMoveGivenItems,
        OperationCodes.TreasureChestUsingStart,
        OperationCodes.TreasureChestUsingCancel,
        OperationCodes.UseLootChest
    ];

    private static readonly int[] ProbeOperationCodeValues = ProbeOperationCodes
        .Select(code => (int)code)
        .ToArray();

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
            GetOperationName(packet.OperationCode),
            request.Parameters.Count,
            DebugProbeFormatter.FormatParameters(request.Parameters));

        return NextAsync(packet);
    }

    private static string GetOperationName(int operationCode)
    {
        var operationName = ProbeOperationCodes
            .FirstOrDefault(code => (int)code == operationCode);

        return (int)operationName == operationCode
            ? operationName.ToString()
            : "Unknown";
    }
}

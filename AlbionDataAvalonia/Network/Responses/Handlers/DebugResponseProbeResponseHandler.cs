using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugResponseProbeResponseHandler : PacketHandler<ResponsePacket>
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
            GetOperationName(packet.OperationCode),
            response.Parameters.Count,
            DebugProbeFormatter.FormatParameters(response.Parameters));

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

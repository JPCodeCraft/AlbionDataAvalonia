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
        (int)OperationCodes.ChangeCluster,
        (int)OperationCodes.GetIslandInfos,
        (int)OperationCodes.RegisterToObject,
        (int)OperationCodes.UnRegisterFromObject,
        (int)OperationCodes.PlaceableObjectPlace,
        (int)OperationCodes.PlaceableObjectPlaceCancel,
        (int)OperationCodes.PlaceableObjectPickup,
        (int)OperationCodes.FarmableHarvest,
        (int)OperationCodes.FarmableFinishGrownItem,
        (int)OperationCodes.FarmableDestroy,
        (int)OperationCodes.FarmableGetProduct,
        (int)OperationCodes.FarmableFill,
        (int)OperationCodes.BoostFarmable,
    ];

    protected override Task OnHandleAsync(ResponsePacket packet)
    {
        if (!Log.IsEnabled(Serilog.Events.LogEventLevel.Debug) || !ProbeOperationCodeValues.Contains(packet.OperationCode))
        {
            return NextAsync(packet);
        }

        var response = new DebugResponseProbeResponse(packet.Parameters);
        Log.Debug(
            "Debug probe captured response {OperationCode} ({OperationName}). ReturnCode={ReturnCode}, MessageSizeBytes={MessageSizeBytes}, IsFragmented={IsFragmented}, FragmentCount={FragmentCount}, ParameterCount={ParameterCount}: {Parameters}",
            packet.OperationCode,
            System.Enum.GetName(typeof(OperationCodes), packet.OperationCode) ?? "Unknown",
            packet.ReturnCode,
            packet.MessageSizeBytes,
            packet.IsFragmented,
            packet.FragmentCount,
            response.Parameters.Count,
            DebugProbeFormatter.FormatParameters(response.Parameters));

        return NextAsync(packet);
    }

}

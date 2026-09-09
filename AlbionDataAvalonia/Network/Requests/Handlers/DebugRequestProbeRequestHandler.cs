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

    protected override Task OnHandleAsync(RequestPacket packet)
    {
        if (!Log.IsEnabled(Serilog.Events.LogEventLevel.Debug) || !ProbeOperationCodeValues.Contains(packet.OperationCode))
        {
            return NextAsync(packet);
        }

        var request = new DebugRequestProbeRequest(packet.Parameters);
        Log.Debug(
            "Debug probe captured request {OperationCode} ({OperationName}). MessageSizeBytes={MessageSizeBytes}, IsFragmented={IsFragmented}, FragmentCount={FragmentCount}, ParameterCount={ParameterCount}: {Parameters}",
            packet.OperationCode,
            System.Enum.GetName(typeof(OperationCodes), packet.OperationCode) ?? "Unknown",
            packet.MessageSizeBytes,
            packet.IsFragmented,
            packet.FragmentCount,
            request.Parameters.Count,
            DebugProbeFormatter.FormatParameters(request.Parameters));

        return NextAsync(packet);
    }

}

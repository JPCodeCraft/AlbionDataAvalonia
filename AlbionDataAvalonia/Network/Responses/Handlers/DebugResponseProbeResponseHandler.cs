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
        (int)OperationCodes.AuctionGetOffers,
        (int)OperationCodes.AuctionGetRequests,
    ];

    protected override Task OnHandleAsync(ResponsePacket packet)
    {
        if (!ProbeOperationCodeValues.Contains(packet.OperationCode))
        {
            return NextAsync(packet);
        }

        var response = new DebugResponseProbeResponse(packet.Parameters);
        int marketOrderCount = response.Parameters.TryGetValue(0, out object? primaryValue) &&
            primaryValue is System.Collections.Generic.IEnumerable<string> marketOrders
                ? marketOrders.Count()
                : 0;
        Log.Debug(
            "Market order probe captured response {OperationCode} ({OperationName}). MessageSizeBytes={MessageSizeBytes}, IsFragmented={IsFragmented}, FragmentCount={FragmentCount}, MarketOrderCount={MarketOrderCount}, ParameterCount={ParameterCount}: {Parameters}",
            packet.OperationCode,
            System.Enum.GetName(typeof(OperationCodes), packet.OperationCode) ?? "Unknown",
            packet.MessageSizeBytes,
            packet.IsFragmented,
            packet.FragmentCount,
            marketOrderCount,
            response.Parameters.Count,
            DebugProbeFormatter.FormatParameters(response.Parameters));

        return NextAsync(packet);
    }

}

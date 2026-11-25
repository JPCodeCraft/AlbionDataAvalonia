using Albion.Network;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

// Quick Sell in a city market is not saved as trades yet: the sell request names the buy orders
// but not their prices, and the reply layout is unknown. Subscribing here makes Settings ›
// Diagnostics recordings include these packets, and the log notes their shape.
internal static class QuickSellPacketLog
{
    private static readonly OperationCodes[] Operations =
    {
        OperationCodes.QuickSellAuctionQueryAction,
        OperationCodes.QuickSellAuctionSellAction,
    };

    public static void Register(ReceiverBuilder builder)
    {
        foreach (var operation in Operations)
        {
            builder.SubscribeRequest<DebugRequestProbeRequest>((int)operation,
                value => Write(operation, "request", value.ReturnCode, value.Parameters));
            builder.SubscribeResponse<DebugResponseProbeResponse>((int)operation,
                value => Write(operation, "reply", value.ReturnCode, value.Parameters));
        }
    }

    private static Task Write(OperationCodes operation, string kind, short returnCode, Dictionary<byte, object> parameters)
    {
        Log.Debug("{Operation} {Kind} (return {ReturnCode}): {Shape}", operation, kind, returnCode, Describe(parameters));
        return Task.CompletedTask;
    }

    private static string Describe(Dictionary<byte, object> parameters) =>
        string.Join("; ", parameters
            .Where(parameter => parameter.Key != 253)
            .OrderBy(parameter => parameter.Key)
            .Select(parameter => parameter.Value switch
            {
                null => $"{parameter.Key}:null",
                string => $"{parameter.Key}:String",
                ICollection items => $"{parameter.Key}:{parameter.Value.GetType().Name}[{items.Count}]",
                _ => $"{parameter.Key}:{parameter.Value.GetType().Name}",
            }));
}

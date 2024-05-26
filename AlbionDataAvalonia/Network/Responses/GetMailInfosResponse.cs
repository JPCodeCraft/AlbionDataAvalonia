using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class GetMailInfosResponse : BaseOperation
{
    public long[] MailIds { get; set; } = [];
    public string[] LocationIds { get; set; } = [];
    public string[] Types { get; set; } = [];
    public long[] Received { get; set; } = [];

    public GetMailInfosResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Debug("Got {PacketType} packet.", GetType());
        try
        {

            if (parameters.TryGetValue(3, out object? _ids))
            {
                MailIds = (long[])_ids;
            }
            if (parameters.TryGetValue(6, out object? _locationIds))
            {
                LocationIds = (string[])_locationIds;
            }
            if (parameters.TryGetValue(10, out object? _types))
            {
                Types = (string[])_types;
            }
            if (parameters.TryGetValue(11, out object? _received))
            {
                Received = (long[])_received;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

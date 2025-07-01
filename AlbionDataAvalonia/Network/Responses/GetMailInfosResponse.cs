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
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {

            if (parameters.TryGetValue(3, out object? _ids))
            {
                if (_ids is int[] intIds)
                {
                    MailIds = Array.ConvertAll(intIds, id => (long)id);
                }
                else if (_ids is long[] longIds)
                {
                    MailIds = longIds;
                }
            }
            if (parameters.TryGetValue(7, out object? _locationIds))
            {
                LocationIds = (string[])_locationIds;
            }
            if (parameters.TryGetValue(11, out object? _types))
            {
                Types = (string[])_types;
            }
            if (parameters.TryGetValue(12, out object? _received))
            {
                if (_received is int[] intReceived)
                {
                    Received = Array.ConvertAll(intReceived, id => (long)id);
                }
                else if (_received is long[] longReceived)
                {
                    Received = longReceived;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

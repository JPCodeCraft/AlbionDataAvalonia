using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class ReadMailResponse : BaseOperation
{
    public long MailId { get; set; }
    public string MailString { get; set; } = string.Empty;
    public ReadMailResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {

            if (parameters.TryGetValue(0, out object? id))
            {
                MailId = Convert.ToInt64(id);
            }
            if (parameters.TryGetValue(1, out object? text))
            {
                MailString = (string)text;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

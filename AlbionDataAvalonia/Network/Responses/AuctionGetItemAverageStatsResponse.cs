using Albion.Network;
using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Responses;

public class AuctionGetItemAverageStatsResponse : BaseOperation
{
    public long[] itemAmounts = Array.Empty<long>();
    public ulong[] silverAmounts = Array.Empty<ulong>();
    public ulong[] timeStamps = Array.Empty<ulong>();
    public ulong messageID = 0;

    public AuctionGetItemAverageStatsResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            //reads the packet
            if (parameters.TryGetValue(0, out object amounts))
            {
                itemAmounts = ((IEnumerable)amounts).Cast<object>().Select(x => Convert.ToInt64(x)).ToArray();
            }
            if (parameters.TryGetValue(1, out object silver))
            {
                silverAmounts = ((IEnumerable)silver).Cast<object>().Select(x => Convert.ToUInt64(x)).ToArray();
            }
            if (parameters.TryGetValue(2, out object stamps))
            {
                timeStamps = ((IEnumerable)stamps).Cast<object>().Select(x => Convert.ToUInt64(x)).ToArray();
            }
            if (parameters.TryGetValue(255, out object id))
            {
                messageID = Convert.ToUInt64(id);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

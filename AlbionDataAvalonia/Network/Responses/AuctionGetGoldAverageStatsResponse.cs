using Albion.Network;
using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Responses
{
    public class AuctionGetGoldAverageStatsResponse : BaseOperation
    {
        public uint[] prices = Array.Empty<uint>();
        public long[] timeStamps = Array.Empty<long>();

        public AuctionGetGoldAverageStatsResponse(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());

            try
            {
                //reads the packet
                if (parameters.TryGetValue(0, out object? _prices))
                {
                    prices = ((IEnumerable)_prices).Cast<object>().Select(x => Convert.ToUInt32(x)).ToArray();
                }
                if (parameters.TryGetValue(1, out object? _timeStamps))
                {
                    timeStamps = ((IEnumerable)_timeStamps).Cast<object>().Select(x => Convert.ToInt64(x)).ToArray();
                }
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }
    }
}

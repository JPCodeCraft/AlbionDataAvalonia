using Albion.Network;
using AlbionData.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Requests;

public class AuctionGetItemAverageStatsRequest : BaseOperation
{
    public uint albionId;
    public ushort quality;
    public Timescale timescale;
    public uint messageID;
    public AuctionGetItemAverageStatsRequest(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Debug("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(1, out object _itemID))
            {
                albionId = Convert.ToUInt32(_itemID);
            }
            if (parameters.TryGetValue(2, out object _quality))
            {
                quality = Convert.ToUInt16(_quality);
            }
            if (parameters.TryGetValue(3, out object _timescale))
            {
                timescale = (Timescale)Convert.ToInt32(_timescale);
            }
            if (parameters.TryGetValue(255, out object _messageID))
            {
                messageID = Convert.ToUInt32(_messageID);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }

    }
}

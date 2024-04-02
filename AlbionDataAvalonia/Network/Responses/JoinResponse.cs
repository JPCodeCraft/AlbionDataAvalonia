using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class JoinResponse : BaseOperation
{
    public readonly AlbionData.Models.Location playerLocation;
    public readonly string playerName;
    public readonly int userObjectId;
    public JoinResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Debug("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object objectId))
            {
                userObjectId = (int)objectId;
            }

            if (parameters.TryGetValue(2, out object nameData))
            {
                playerName = (string)nameData;
            }

            if (parameters.TryGetValue(8, out object locationData))
            {
                string location = (string)locationData;
                if (location.Contains("-Auction2")) location = location.Replace("-Auction2", "");
                playerLocation = (AlbionData.Models.Location)int.Parse(location);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

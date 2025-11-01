using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class AssetOverviewResponse : BaseOperation
{
    public readonly AlbionLocation playerLocation;
    public readonly string playerName;
    public readonly int userObjectId;

    public AssetOverviewResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            // if (parameters.TryGetValue(0, out object objectId))
            // {
            //     switch (objectId)
            //     {
            //         case int intValue:
            //             userObjectId = intValue;
            //             break;
            //         case short shortValue:
            //             userObjectId = shortValue;
            //             break;
            //         case byte byteValue:
            //             userObjectId = byteValue;
            //             break;
            //         case null:
            //             Log.Error("objectId is null.");
            //             break;
            //         default:
            //             Log.Error("Unexpected type for objectId: {Type}", objectId.GetType());
            //             break;
            //     }
            // }

            // if (parameters.TryGetValue(2, out object nameData))
            // {
            //     playerName = (string)nameData;
            // }

            // if (parameters.TryGetValue(8, out object locationData))
            // {
            //     string location = (string)locationData;
            //     var albionLocation = AlbionLocations.Get(location);
            //     if (albionLocation != null)
            //     {
            //         playerLocation = albionLocation;
            //     }
            //     else
            //     {
            //         playerLocation = AlbionLocations.Unknown;
            //     }
            // }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

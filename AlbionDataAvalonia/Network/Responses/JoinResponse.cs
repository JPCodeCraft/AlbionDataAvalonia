using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class JoinResponse : BaseOperation
{
    public readonly AlbionLocation playerLocation;
    public readonly string playerName;
    public readonly int userObjectId;
    public readonly Guid? userGuid;
    public readonly double? globalMultiplier;

    public JoinResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object objectId))
            {
                switch (objectId)
                {
                    case long longValue:
                        userObjectId = unchecked((int)longValue);
                        break;
                    case int intValue:
                        userObjectId = intValue;
                        break;
                    case short shortValue:
                        userObjectId = shortValue;
                        break;
                    case byte byteValue:
                        userObjectId = byteValue;
                        break;
                    case null:
                        // Handle null value. For example, you might want to log an error or throw an exception.
                        Log.Error("objectId is null.");
                        break;
                    default:
                        // Handle unexpected type. For example, you might want to log an error or throw an exception.
                        Log.Error("Unexpected type for objectId: {Type}", objectId.GetType());
                        break;
                }
            }

            if (parameters.TryGetValue(1, out object? guidData))
            {
                userGuid = guidData.ToGuid();
            }

            if (parameters.TryGetValue(2, out object nameData))
            {
                playerName = (string)nameData;
            }

            if (parameters.TryGetValue(8, out object locationData))
            {
                string location = (string)locationData;
                playerLocation = AlbionLocations.ResolveLocation(location);
            }

            if (parameters.TryGetValue(84, out object globalMultiplierData))
            {
                try
                {
                    globalMultiplier = globalMultiplierData.ToLong() / 10000d;
                }
                catch (InvalidCastException)
                {
                    Log.Warning("Join response param 83 was present but could not be parsed into a global multiplier. Type: {Type}", globalMultiplierData?.GetType());
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

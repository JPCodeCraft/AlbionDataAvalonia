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
    public readonly long userObjectId;
    public readonly Guid? userGuid;
    public readonly double? globalMultiplier;
    public readonly long? premiumExpirationTicks;

    public JoinResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object objectId))
            {
                userObjectId = objectId.ToLong();
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
                    Log.Warning("Join response param 84 was present but could not be parsed into a global multiplier. Type: {Type}", globalMultiplierData?.GetType());
                }
            }

            if (parameters.TryGetValue(89, out object? premiumExpirationData))
            {
                try
                {
                    premiumExpirationTicks = premiumExpirationData.ToLong();
                }
                catch (InvalidCastException)
                {
                    Log.Warning(
                        "Join response param 89 was present but could not be parsed into Premium expiration ticks. Type: {Type}",
                        premiumExpirationData?.GetType());
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class PremiumChangedEvent : BaseEvent
{
    public readonly int? userObjectId;
    public readonly long? premiumExpirationTicks;

    public PremiumChangedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        if (parameters.TryGetValue(0, out object? objectId))
        {
            try
            {
                userObjectId = checked((int)objectId.ToLong());
            }
            catch (Exception exception) when (exception is InvalidCastException or OverflowException)
            {
                Log.Warning(
                    "PremiumChanged param 0 could not be parsed into an object ID. Type: {Type}",
                    objectId?.GetType());
            }
        }

        if (parameters.TryGetValue(1, out object? premiumExpirationData))
        {
            try
            {
                premiumExpirationTicks = premiumExpirationData.ToLong();
            }
            catch (Exception exception) when (exception is InvalidCastException or OverflowException)
            {
                Log.Warning(
                    "PremiumChanged param 1 could not be parsed into Premium expiration ticks. Type: {Type}",
                    premiumExpirationData?.GetType());
            }
        }
    }
}

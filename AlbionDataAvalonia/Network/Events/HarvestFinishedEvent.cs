using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class HarvestFinishedEvent : BaseEvent
{
    public long UserObjectId { get; }
    public long ResourceObjectId { get; }
    public int ItemId { get; }
    public int StandardAmount { get; }
    public int GatheringBonusAmount { get; }
    public int PremiumBonusAmount { get; }

    public HarvestFinishedEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(0, out var userObjectId))
            {
                UserObjectId = userObjectId.ToLong();
            }

            if (parameters.TryGetValue(3, out var resourceObjectId))
            {
                ResourceObjectId = resourceObjectId.ToLong();
            }

            if (parameters.TryGetValue(4, out var itemId))
            {
                ItemId = itemId.ToInt();
            }

            if (parameters.TryGetValue(5, out var standardAmount))
            {
                StandardAmount = standardAmount.ToInt();
            }

            if (parameters.TryGetValue(6, out var gatheringBonusAmount))
            {
                GatheringBonusAmount = gatheringBonusAmount.ToInt();
            }

            if (parameters.TryGetValue(7, out var premiumBonusAmount))
            {
                PremiumBonusAmount = premiumBonusAmount.ToInt();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}


using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class TakeSilverEvent : BaseEvent
{
    public long? ObjectId { get; private set; }
    public long? TargetEntityId { get; private set; }
    public long TimeStamp { get; private set; }
    public double YieldPreTax { get; private set; }
    public double GuildTax { get; private set; }
    public double ClusterTax { get; private set; }
    public bool IsPremiumBonus { get; private set; }
    public double Multiplier { get; private set; }
    public double SilverGained { get; private set; }

    public TakeSilverEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(1, out object? timeStamp))
            {
                TimeStamp = timeStamp.ToLong();
            }

            if (parameters.TryGetValue(2, out object? targetEntityId))
            {
                TargetEntityId = targetEntityId.ToLong();
            }

            if (parameters.TryGetValue(3, out object? yieldPreTax))
            {
                YieldPreTax = yieldPreTax.ToFixedPointDouble();
            }

            var hasGuildTax = parameters.TryGetValue(5, out object? guildTax) && guildTax is not null;
            if (hasGuildTax)
            {
                GuildTax = guildTax.ToFixedPointDouble();
            }

            var hasClusterTax = parameters.TryGetValue(6, out object? clusterTax) && clusterTax is not null;
            if (hasClusterTax)
            {
                ClusterTax = clusterTax.ToFixedPointDouble();
            }

            if (parameters.TryGetValue(7, out object? isPremiumBonus))
            {
                IsPremiumBonus = isPremiumBonus.ToBool();
            }

            if (parameters.TryGetValue(8, out object? multiplier))
            {
                Multiplier = multiplier.ToFixedPointDouble();
            }

            SilverGained = hasGuildTax || hasClusterTax
                ? YieldPreTax - GuildTax - ClusterTax
                : YieldPreTax;
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class UpdateFameEvent : BaseEvent
{
    public double BonusFactor { get; private set; } = 1;
    public double BonusFactorInPercent { get; private set; }
    public double FameWithZoneMultiplier { get; private set; }
    public bool IsPremiumBonus { get; private set; }
    public double SatchelFame { get; private set; }
    public bool IsBonusFactorActive { get; private set; }
    public long UsedBagInsightItemIndex { get; private set; } = -1;
    public double TotalPlayerFame { get; private set; }
    public double Multiplier { get; private set; } = 1;
    public double PremiumFame { get; private set; }
    public double ZoneFame { get; private set; }
    public double TotalGainedFame { get; private set; }

    public UpdateFameEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(1, out object? totalPlayerFame))
            {
                TotalPlayerFame = totalPlayerFame.ToFixedPointDouble();
            }

            if (parameters.TryGetValue(2, out object? fameWithZoneMultiplier))
            {
                FameWithZoneMultiplier = fameWithZoneMultiplier.ToFixedPointDouble();
            }

            if (parameters.TryGetValue(3, out object? zoneFame))
            {
                ZoneFame = zoneFame.ToFixedPointDouble();
            }

            if (parameters.TryGetValue(4, out object? multiplier))
            {
                Multiplier = multiplier.ToFixedPointDouble();
            }

            if (parameters.TryGetValue(5, out object? isPremiumBonus))
            {
                IsPremiumBonus = isPremiumBonus.ToBool();
            }

            if (parameters.TryGetValue(8, out object? usedBagInsightItemIndex))
            {
                UsedBagInsightItemIndex = usedBagInsightItemIndex.ToLong();
            }

            if (parameters.TryGetValue(10, out object? satchelFame))
            {
                SatchelFame = satchelFame.ToFixedPointDouble();
            }

            if (parameters.TryGetValue(17, out object? bonusFactor))
            {
                BonusFactor = 1 + bonusFactor.ToDouble();
                BonusFactorInPercent = (BonusFactor - 1) * 100;
                IsBonusFactorActive = BonusFactorInPercent > 0;

                if (IsBonusFactorActive)
                {
                    BonusFactor = 1;
                }
            }

            var fameWithZoneAndPremium = FameWithZoneMultiplier;
            if (FameWithZoneMultiplier > 0 && IsPremiumBonus)
            {
                fameWithZoneAndPremium = FameWithZoneMultiplier * 1.5d;
            }

            if (fameWithZoneAndPremium > 0 && FameWithZoneMultiplier > 0)
            {
                PremiumFame = fameWithZoneAndPremium - FameWithZoneMultiplier;
            }

            TotalGainedFame = (FameWithZoneMultiplier + PremiumFame + SatchelFame) * BonusFactor;
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}

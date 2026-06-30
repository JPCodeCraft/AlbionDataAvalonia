using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class NewEquipmentItemLegendarySoulEvent : BaseEvent
{
    public LegendarySoul? LegendarySoul { get; }

    public NewEquipmentItemLegendarySoulEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (!parameters.TryGetValue(0, out var objectIdValue)
                || !parameters.TryGetValue(1, out var soulIdValue))
            {
                return;
            }

            var objectId = objectIdValue.ToLong();
            var soulId = soulIdValue.ToGuid() ?? Guid.Empty;
            if (soulId == Guid.Empty)
            {
                return;
            }
            var soulName = parameters.TryGetValue(2, out var soulNameValue)
                ? soulNameValue.ToString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(soulName))
            {
                soulName = null;
            }
            var era = parameters.TryGetValue(3, out var eraValue)
                ? checked((int)eraValue.ToLong())
                : 0;
            var attunedToMe = parameters.TryGetValue(4, out var attunedToMeValue)
                && attunedToMeValue.ToBool();
            var attunedToPlayerName = parameters.TryGetValue(5, out var attunedToPlayerNameValue)
                ? attunedToPlayerNameValue.ToString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(attunedToPlayerName))
            {
                attunedToPlayerName = null;
            }
            var strain = parameters.TryGetValue(6, out var strainValue)
                ? strainValue.ToFixedPointDouble()
                : 0d;
            var attunement = parameters.TryGetValue(7, out var attunementValue)
                ? (long)Math.Round(attunementValue.ToLong() / 10000d, MidpointRounding.AwayFromZero)
                : 0L;
            var traitIds = parameters.TryGetValue(8, out var traitIdsValue)
                ? traitIdsValue.ToStringArray()
                : Array.Empty<string>();
            var traitValues = parameters.TryGetValue(9, out var traitValuesValue)
                ? traitValuesValue.ToDoubleArray()
                : Array.Empty<double>();
            var attunementSpent = parameters.TryGetValue(12, out var attunementSpentValue)
                ? ToFixedPointWhole(attunementSpentValue)
                : 0L;
            var pvpFameGained = parameters.TryGetValue(13, out var pvpFameGainedValue)
                ? ToFixedPointWhole(pvpFameGainedValue)
                : 0L;

            LegendarySoul = new LegendarySoul(
                objectId,
                soulId,
                soulName,
                era,
                attunedToMe,
                attunedToPlayerName,
                attunement,
                strain,
                pvpFameGained,
                attunementSpent,
                traitIds,
                traitValues);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse NewEquipmentItemLegendarySoul event");
        }
    }

    private static long ToFixedPointWhole(object value)
    {
        return (long)Math.Round(value.ToLong() / 10000d, MidpointRounding.AwayFromZero);
    }
}

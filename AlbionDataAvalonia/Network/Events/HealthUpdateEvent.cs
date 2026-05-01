using Albion.Network;
using AlbionDataAvalonia.Combat.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class HealthUpdateEvent : BaseEvent
{
    public long AffectedObjectId { get; }
    public long CauserId { get; }
    public double HealthChange { get; }
    public double NewHealthValue { get; }
    public int CausingSpellIndex { get; }
    public long? GameTimeMilliseconds { get; }

    public HealthUpdateEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? affectedObjectId))
            {
                AffectedObjectId = affectedObjectId.ToLong();
            }

            if (parameters.TryGetValue(2, out object? healthChange))
            {
                HealthChange = healthChange.ToDouble();
            }

            if (parameters.TryGetValue(1, out object? gameTimeMilliseconds))
            {
                GameTimeMilliseconds = gameTimeMilliseconds.ToLong();
            }

            if (parameters.TryGetValue(3, out object? newHealthValue))
            {
                NewHealthValue = newHealthValue.ToDouble();
            }

            if (parameters.TryGetValue(6, out object? causerId))
            {
                CauserId = causerId.ToLong();
            }

            if (parameters.TryGetValue(7, out object? causingSpellIndex))
            {
                CausingSpellIndex = causingSpellIndex.ToInt();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    public bool TryNormalize(out CombatHealthEvent healthEvent)
    {
        return CombatHealthEvent.TryCreate(
            CauserId,
            AffectedObjectId,
            HealthChange,
            NewHealthValue,
            GameTimeMilliseconds,
            out healthEvent);
    }
}

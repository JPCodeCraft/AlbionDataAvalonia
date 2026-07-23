using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class FestivitiesUpdateEvent : BaseEvent
{
    public byte[] EventTypes { get; } = [];
    public string[] Scopes { get; } = [];
    public string[] UniqueNames { get; } = [];
    public long[] StartTimeTicks { get; } = [];
    public long[] EndTimeTicks { get; } = [];
    public bool IsValid { get; }

    public FestivitiesUpdateEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (!parameters.TryGetValue(0, out var eventTypes)
                || !parameters.TryGetValue(1, out var scopes)
                || !parameters.TryGetValue(2, out var uniqueNames)
                || !parameters.TryGetValue(3, out var startTimeTicks)
                || !parameters.TryGetValue(4, out var endTimeTicks))
            {
                Log.Warning("Festivities update is missing one or more required parameters.");
                return;
            }

            EventTypes = eventTypes.ToByteArray();
            Scopes = scopes.ToStringArray();
            UniqueNames = uniqueNames.ToStringArray();
            StartTimeTicks = startTimeTicks.ToLongArray();
            EndTimeTicks = endTimeTicks.ToLongArray();

            IsValid = EventTypes.Length > 0
                && EventTypes.Length == Scopes.Length
                && EventTypes.Length == UniqueNames.Length
                && EventTypes.Length == StartTimeTicks.Length
                && EventTypes.Length == EndTimeTicks.Length;

            if (!IsValid)
            {
                Log.Warning(
                    "Festivities update arrays have mismatched lengths. EventTypes: {EventTypesCount}. Scopes: {ScopesCount}. UniqueNames: {UniqueNamesCount}. Starts: {StartsCount}. Ends: {EndsCount}.",
                    EventTypes.Length,
                    Scopes.Length,
                    UniqueNames.Length,
                    StartTimeTicks.Length,
                    EndTimeTicks.Length);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to parse festivities update.");
        }
    }
}

using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class FestivitiesUpdateEvent : BaseEvent
{
    public byte[] Kinds { get; } = [];
    public string[] Categories { get; } = [];
    public string[] UniqueNames { get; } = [];
    public long[] StartTimeTicks { get; } = [];
    public long[] EndTimeTicks { get; } = [];
    public bool IsValid { get; }

    public FestivitiesUpdateEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (!parameters.TryGetValue(0, out var kinds)
                || !parameters.TryGetValue(1, out var categories)
                || !parameters.TryGetValue(2, out var uniqueNames)
                || !parameters.TryGetValue(3, out var startTimeTicks)
                || !parameters.TryGetValue(4, out var endTimeTicks))
            {
                Log.Warning("Festivities update is missing one or more required parameters.");
                return;
            }

            Kinds = kinds.ToByteArray();
            Categories = categories.ToStringArray();
            UniqueNames = uniqueNames.ToStringArray();
            StartTimeTicks = startTimeTicks.ToLongArray();
            EndTimeTicks = endTimeTicks.ToLongArray();

            IsValid = Kinds.Length == Categories.Length
                && Kinds.Length == UniqueNames.Length
                && Kinds.Length == StartTimeTicks.Length
                && Kinds.Length == EndTimeTicks.Length;

            if (!IsValid)
            {
                Log.Warning(
                    "Festivities update arrays have mismatched lengths. Kinds: {KindsCount}. Categories: {CategoriesCount}. UniqueNames: {UniqueNamesCount}. Starts: {StartsCount}. Ends: {EndsCount}.",
                    Kinds.Length,
                    Categories.Length,
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

using Albion.Network;
using AlbionDataAvalonia.Combat.Models;
using Serilog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Events;

public class HealthUpdatesEvent : BaseEvent
{
    public IReadOnlyList<HealthUpdateEntry> HealthUpdates { get; } = Array.Empty<HealthUpdateEntry>();

    public HealthUpdatesEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            var affectedObjectId = parameters.TryGetValue(0, out object? affectedObject)
                ? affectedObject.ToLong()
                : 0;

            var gameTimeMilliseconds = parameters.TryGetValue(1, out object? gameTimeMillisecondsData)
                ? GetIndexedValues(gameTimeMillisecondsData, value => value.ToLong())
                : new Dictionary<int, long>();
            var healthChanges = parameters.TryGetValue(2, out object? healthChangesData)
                ? GetIndexedValues(healthChangesData, value => value.ToDouble())
                : new Dictionary<int, double>();
            var newHealthValues = parameters.TryGetValue(3, out object? newHealthValuesData)
                ? GetIndexedValues(newHealthValuesData, value => value.ToDouble())
                : new Dictionary<int, double>();
            var causerIds = parameters.TryGetValue(6, out object? causerIdsData)
                ? GetIndexedValues(causerIdsData, value => value.ToLong())
                : new Dictionary<int, long>();
            var causingSpellIndices = parameters.TryGetValue(7, out object? causingSpellIndicesData)
                ? GetIndexedValues(causingSpellIndicesData, value => value.ToInt())
                : new Dictionary<int, int>();
            var count = new[]
            {
                gameTimeMilliseconds.Count,
                healthChanges.Count,
                newHealthValues.Count,
                causerIds.Count,
                causingSpellIndices.Count
            }.Max();

            var updates = new List<HealthUpdateEntry>(count);
            for (var i = 0; i < count; i++)
            {
                updates.Add(new HealthUpdateEntry(
                    affectedObjectId,
                    causerIds.GetValueOrDefault(i),
                    healthChanges.GetValueOrDefault(i),
                    newHealthValues.GetValueOrDefault(i),
                    causingSpellIndices.GetValueOrDefault(i),
                    gameTimeMilliseconds.TryGetValue(i, out var gameTime)
                        ? gameTime
                        : null));
            }

            HealthUpdates = updates;
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    private static Dictionary<int, T> GetIndexedValues<T>(object raw, Func<object, T> converter)
    {
        var values = new Dictionary<int, T>();

        if (raw is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                values[entry.Key.ToInt()] = converter(entry.Value!);
            }

            return values;
        }

        if (raw is IEnumerable enumerable && raw is not string)
        {
            var index = 0;
            foreach (var item in enumerable.Cast<object>())
            {
                values[index] = converter(item!);
                index++;
            }

            return values;
        }

        values[0] = converter(raw);
        return values;
    }

    public sealed record HealthUpdateEntry(
        long AffectedObjectId,
        long CauserId,
        double HealthChange,
        double NewHealthValue,
        int CausingSpellIndex,
        long? GameTimeMilliseconds)
    {
        public bool TryNormalize(out CombatHealthEvent healthEvent)
        {
            return CombatHealthEvent.TryCreate(
                CauserId,
                AffectedObjectId,
                HealthChange,
                NewHealthValue,
                CausingSpellIndex,
                GameTimeMilliseconds,
                out healthEvent);
        }
    }
}

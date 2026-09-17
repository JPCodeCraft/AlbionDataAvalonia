using Albion.Network;
using AlbionDataAvalonia.Farming.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Events;

public sealed class NewBuildingEvent : BaseEvent
{
    public long SessionId { get; private set; }
    public int? RenovationState { get; private set; }
    public FarmingObjectObservation? Object { get; private set; }

    public NewBuildingEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (!parameters.ContainsKey(0)) return;
            var name = Text(parameters, 3);
            if (name is null || name.Length > 200) return;
            var isPlot = name is "T1_FARMHOUSE" or "T1_HERBGARDEN" or "T1_PASTURE" or "T1_KENNEL";
            if (!isPlot && !name.Contains("_FARM_", StringComparison.Ordinal)) return;
            var stableId = GuidValue(parameters, 1);
            var position = parameters.TryGetValue(4, out var raw) ? raw.ToDoubleArray() : [];
            if (stableId is null || position.Length != 2 || !position.All(n => double.IsFinite(n) && Math.Abs(n) <= 1_000_000)) return;
            var rotation = parameters.TryGetValue(5, out var rawRotation) ? rawRotation.ToDouble() : (double?)null;
            if (rotation is not null && (!double.IsFinite(rotation.Value) || Math.Abs(rotation.Value) > 100_000)) return;
            var sessionId = Number(parameters, 0);
            SessionId = sessionId;
            if (isPlot && parameters.ContainsKey(29))
            {
                var renovation = Number(parameters, 29);
                if (renovation is >= 0 and <= 255) RenovationState = (int)renovation;
            }
            Object = new FarmingObjectObservation
            {
                ObjectId = stableId,
                UniqueName = name,
                Kind = isPlot ? "plot" : "farmable",
                PositionX = position[0],
                PositionY = position[1],
                Rotation = rotation,
                ObservedAt = DateTime.UtcNow
            };
        });
    }
}

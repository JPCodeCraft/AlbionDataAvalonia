using Albion.Network;
using AlbionDataAvalonia.Farming.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using static AlbionDataAvalonia.Network.FarmingPacketValues;

namespace AlbionDataAvalonia.Network.Events;

public sealed class FarmableObjectInfoEvent : BaseEvent
{
    public long ObjectId { get; private set; }
    public FarmingObjectState? State { get; private set; }

    public FarmableObjectInfoEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        TryRead(() =>
        {
            if (!parameters.ContainsKey(0)) return;
            var id = Number(parameters, 0);
            var amounts = parameters.TryGetValue(8, out var rawAmounts) ? rawAmounts.ToLongArray() : [];
            var anchors = parameters.TryGetValue(9, out var rawAnchors) ? rawAnchors.ToLongArray() : [];
            if (amounts.Length != anchors.Length || amounts.Length > 32 || amounts.Any(n => n < 0 || n.ToFixedPointDouble() > 1_000_000_000)) return;
            var nutrition = amounts.Select((amount, i) => new FarmingNutrition
            {
                Amount = amount.ToFixedPointDouble(),
                UpdatedAt = UtcTicks(anchors[i])
            }).ToList();
            var multiplier = parameters.TryGetValue(12, out var rawMultiplier) ? rawMultiplier.ToDouble() : 1;
            var animalGrowth = Number(parameters, 1).ToFixedPointDouble();
            var cropGrowth = Number(parameters, 4).ToFixedPointDouble();
            var boostCount = Number(parameters, 14);
            var productProgress = parameters.TryGetValue(10, out var rawProgress) ? rawProgress.ToLongArray() : [];
            var productAnchors = parameters.TryGetValue(11, out var rawProductAnchors) ? rawProductAnchors.ToLongArray() : [];
            // Only the single-product progress/anchor pair has been verified. Keep
            // other shapes unknown while continuing to record growth and food.
            var productAt = productAnchors.Length == 1 ? UtcTicks(productAnchors[0]) : null;
            var hasProductProgress = productProgress.Length == 1 && productAt.HasValue
                && productProgress[0] >= 0 && productProgress[0].ToFixedPointDouble() <= 1_000_000_000;
            if (!double.IsFinite(multiplier) || multiplier < 0.001 || multiplier > 1000
                || animalGrowth < 0 || animalGrowth > 1_000_000_000 || cropGrowth < 0 || cropGrowth > 1_000_000_000
                || boostCount < 0 || boostCount > 10_000) return;
            var state = new FarmingObjectState
            {
                AnimalGrowthSeconds = animalGrowth,
                AnimalGrowthUpdatedAt = UtcTicks(Number(parameters, 2)),
                CropGrowthSeconds = cropGrowth,
                CropGrowthUpdatedAt = UtcTicks(Number(parameters, 5)),
                ProductProgressSeconds = hasProductProgress ? productProgress[0].ToFixedPointDouble() : null,
                ProductProgressUpdatedAt = hasProductProgress ? productAt : null,
                IsMature = parameters.TryGetValue(3, out var mature) && mature.ToBool(),
                Nutrition = nutrition,
                DurationMultiplier = multiplier,
                LastBoostAt = UtcTicks(Number(parameters, 13)),
                BoostCount = (int)boostCount
            };
            ObjectId = id;
            State = state;
        });
    }
}

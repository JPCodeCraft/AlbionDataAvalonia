using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Legendary.Models;

public sealed record LegendaryTraitDefinition(
    string UsName,
    LegendaryTraitEffectDefinition? Effect,
    string Rarity,
    string VariableConfig);

public sealed record LegendaryTraitEffectDefinition(string Type, double BaseValue);

public sealed record LegendaryTraitVariableDefinition(double MinFactor, double MaxFactor);

public sealed record LegendaryItemPowerDefinition(
    int BaseItemPower,
    IReadOnlyDictionary<int, int> EnchantmentItemPowers);

public sealed record LegendaryRatingScalingDefinition(double MaxRating, double RatingScaler);

public sealed record LegendaryCalculatedValue(string FormattedText, double RollPercentage);

public sealed record LegendaryDefinitionsSnapshot(
    IReadOnlyDictionary<string, LegendaryTraitDefinition> Traits,
    IReadOnlyDictionary<string, string> UsNames,
    IReadOnlyDictionary<string, LegendaryTraitVariableDefinition> TraitVariables,
    IReadOnlyDictionary<string, LegendaryItemPowerDefinition> ItemPowers,
    IReadOnlyDictionary<int, int> QualityItemPowerBonuses,
    IReadOnlyDictionary<string, double> TraitProgressions,
    IReadOnlyDictionary<int, LegendaryRatingScalingDefinition> RatingScalings,
    IReadOnlyDictionary<string, int> ItemBaseTraits)
{
    public static LegendaryDefinitionsSnapshot Empty { get; } = new(
        new Dictionary<string, LegendaryTraitDefinition>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, LegendaryTraitVariableDefinition>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, LegendaryItemPowerDefinition>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<int, int>(),
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<int, LegendaryRatingScalingDefinition>(),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
}

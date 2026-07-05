using AlbionDataAvalonia.Legendary.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Legendary;

public sealed class LegendaryDefinitionsService : IDisposable
{
    private const string DefinitionsUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/legendaryitems.json";
    private const string LocalizationUrl = "https://cdn.albionfreemarket.com/AlbionLocalization/merged_localization.json";
    private const string ItemsUrl = "https://cdn.albionfreemarket.com/AlbionLocalization/processed_items.json";
    private const string SpellsUrl = "https://cdn.albionfreemarket.com/AlbionLocalization/processed_spells.json";
    private const string GameDataUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/gamedata.json";
    private static readonly IReadOnlyDictionary<string, string> TraitNameKeysBySpell =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PASSIVE_TRAIT_ENERGY_COST_REDUCTION"] = "ITEMDETAILS_STATS_ENERGY_COST_REDUCTION",
            ["PASSIVE_TRAIT_HITPOINTS_MAX"] = "ITEMDETAILS_STATS_MAX_HITPOINTS",
            ["PASSIVE_TRAIT_ITEM_POWER"] = "ITEMDETAILS_STATS_ITEM_POWER",
            ["PASSIVE_TRAIT_ENERGY_MAX"] = "ITEMDETAILS_STATS_MAX_ENERGY",
            ["PASSIVE_TRAIT_CC_RESIST"] = "ITEMDETAILS_STATS_CROWD_CONTROL_RESISTANCE",
            ["PASSIVE_TRAIT_MOB_FAME"] = "ITEMDETAILS_STATS_MOB_FAME_MODIFIER",
            ["PASSIVE_TRAIT_RESILIENCE_PENETRATION"] = "ITEMDETAILS_STATS_FOCUS_FIRE_PROTECTION_PENETRATION",
            ["PASSIVE_TRAIT_CAST_SPEED_INCREASE"] = "ITEMDETAILS_STATS_CAST_TIME_REDUCTION",
            ["PASSIVE_TRAIT_COOLDOWN_REDUCTION"] = "ITEMDETAILS_STATS_COOLDOWN_REDUCTION"
        };
    private static readonly HashSet<string> FlatEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "hitpointsmax",
        "energymax",
        "crowdcontrolresistance",
        "itempower"
    };
    private static readonly HashSet<string> DefenseEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bonusdefensevsplayers",
        "bonusdefensevsmobs"
    };
    private static readonly HashSet<string> ReductionEffectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "magiccasttimereduction",
        "magiccooldownreduction"
    };
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private LegendaryDefinitionsSnapshot snapshot = LegendaryDefinitionsSnapshot.Empty;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var definitionsTask = httpClient.GetStringAsync(DefinitionsUrl, cancellationToken);
            var localizationTask = LoadUsNamesAsync(cancellationToken);
            var itemPowersTask = LoadItemPowersAsync(cancellationToken);
            var spellEffectsTask = LoadSpellEffectsAsync(cancellationToken);
            var gameDataTask = LoadGameDataAsync(cancellationToken);

            var json = await definitionsTask.ConfigureAwait(false);
            var usNames = await localizationTask.ConfigureAwait(false);
            var itemPowers = await itemPowersTask.ConfigureAwait(false);
            var spellEffects = await spellEffectsTask.ConfigureAwait(false);
            var gameData = await gameDataTask.ConfigureAwait(false);

            snapshot = Parse(json, usNames, spellEffects, itemPowers, gameData);
            Log.Debug(
                "Loaded {TraitCount} legendary traits. US names: {UsNameCount}; item powers: {ItemPowerCount}; spell effects: {SpellEffectCount}; quality bonuses: {QualityBonusCount}",
                snapshot.Traits.Count,
                usNames.Count,
                itemPowers.Count,
                spellEffects.Count,
                gameData.QualityItemPowerBonuses.Count);
        }
        catch (Exception ex)
        {
            snapshot = LegendaryDefinitionsSnapshot.Empty;
            Log.Warning(ex, "Failed to load legendary item definitions from {DefinitionsUrl}", DefinitionsUrl);
        }
    }

    public LegendaryTraitDefinition? FindTrait(string traitId)
    {
        return snapshot.Traits.TryGetValue(traitId, out var definition) ? definition : null;
    }

    private string FindUsName(string localizationKey, string fallback)
    {
        var normalizedKey = NormalizeKey(localizationKey);
        return snapshot.UsNames.TryGetValue(normalizedKey, out var usName) && !string.IsNullOrWhiteSpace(usName)
            ? usName
            : fallback;
    }

    public string FindItemUsName(string itemUniqueName, string fallback)
    {
        var baseName = itemUniqueName.Split('@', 2)[0];
        return FindUsName($"ITEMS_{baseName}", fallback);
    }

    public LegendaryCalculatedValue CalculateTraitValue(
        string itemUniqueName,
        int quality,
        string traitId,
        double rawValue,
        CultureInfo culture,
        double awakenedItemPowerBonus = 0d)
    {
        var fallback = new LegendaryCalculatedValue(
            FormatRawValue(rawValue, culture),
            Math.Clamp(rawValue * 100d, 0d, 100d),
            rawValue,
            false);
        if (!snapshot.Traits.TryGetValue(traitId, out var trait)
            || trait.Effect is null
            || !snapshot.TraitVariables.TryGetValue(trait.VariableConfig, out var variable)
            || !TryGetItemPower(itemUniqueName, quality, out var itemPower))
        {
            return fallback;
        }

        var rollFactor = variable.MinFactor
            + Math.Pow(rawValue, variable.RollScaler) * (variable.MaxFactor - variable.MinFactor);
        var progression = GetProgression(trait.Effect.Type);
        var isReduction = ReductionEffectTypes.Contains(trait.Effect.Type);
        var isEnergyCostReduction = string.Equals(
            trait.Effect.Type,
            "energycostreduction",
            StringComparison.OrdinalIgnoreCase);
        var baseValue = isReduction
            ? trait.Effect.BaseValue / (1d - trait.Effect.BaseValue)
            : trait.Effect.BaseValue;
        var scalingItemPower = string.Equals(trait.Effect.Type, "itempower", StringComparison.OrdinalIgnoreCase)
            ? itemPower
            : itemPower + awakenedItemPowerBonus;
        var scaledValue = baseValue * rollFactor * Math.Pow(progression, scalingItemPower / 100d);
        var isDefense = DefenseEffectTypes.Contains(trait.Effect.Type);
        var isFlat = FlatEffectTypes.Contains(trait.Effect.Type);
        var numericValue = isReduction
            ? -scaledValue / (1d + scaledValue) * 100d
            : isDefense
                ? scaledValue / (1d + scaledValue) * 100d
                : isFlat
                    ? scaledValue
                    : scaledValue * 100d;
        return new LegendaryCalculatedValue(
            FormatCalculatedValue(
                numericValue,
                !isFlat,
                isDefense,
                isReduction,
                isEnergyCostReduction,
                culture),
            fallback.RollPercentage,
            numericValue,
            true);
    }

    public double CalculateAwakenedItemPowerBonus(IEnumerable<LegendaryItemTrait> traits)
    {
        foreach (var observedTrait in traits)
        {
            if (!string.Equals(observedTrait.TraitId, "TRAIT_ITEM_POWER", StringComparison.OrdinalIgnoreCase)
                || !snapshot.Traits.TryGetValue(observedTrait.TraitId, out var definition)
                || definition.Effect is null
                || !string.Equals(definition.Effect.Type, "itempower", StringComparison.OrdinalIgnoreCase)
                || !snapshot.TraitVariables.TryGetValue(definition.VariableConfig, out var variable))
            {
                continue;
            }

            var rollFactor = variable.MinFactor
                + Math.Pow(observedTrait.Value, variable.RollScaler) * (variable.MaxFactor - variable.MinFactor);
            return definition.Effect.BaseValue * rollFactor;
        }

        return 0d;
    }

    public long? CalculateLegendaryRating(string itemUniqueName, IEnumerable<double> traitValues)
    {
        var baseName = itemUniqueName.Split('@', 2)[0];
        if (baseName.Length < 3
            || baseName[0] != 'T'
            || !int.TryParse(baseName.AsSpan(1, 1), out var tier)
            || !snapshot.RatingScalings.TryGetValue(tier, out var scaling)
            || !snapshot.ItemBaseTraits.TryGetValue($"T?{baseName[2..]}", out var baseTraits))
        {
            return null;
        }

        var traitTotal = 0d;
        foreach (var value in traitValues)
        {
            traitTotal += value;
        }
        var rating = scaling.MaxRating
            * Math.Pow(traitTotal / (baseTraits + 1d), scaling.RatingScaler);
        return (long)Math.Floor(rating);
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadUsNamesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await httpClient.GetStringAsync(LocalizationUrl, cancellationToken).ConfigureAwait(false);
            return ParseUsNames(json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load US legendary localization from {LocalizationUrl}", LocalizationUrl);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<IReadOnlyDictionary<string, LegendaryItemPowerDefinition>> LoadItemPowersAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await httpClient.GetStringAsync(ItemsUrl, cancellationToken).ConfigureAwait(false);
            var itemPowers = ParseItemPowers(json);
            if (itemPowers.Count == 0)
            {
                throw new InvalidOperationException("Processed item data contains no item power definitions.");
            }
            return itemPowers;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load legendary item power data from {ItemsUrl}", ItemsUrl);
            return new Dictionary<string, LegendaryItemPowerDefinition>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<IReadOnlyDictionary<string, LegendaryTraitEffectDefinition>> LoadSpellEffectsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await httpClient.GetStringAsync(SpellsUrl, cancellationToken).ConfigureAwait(false);
            var spellEffects = ParseSpellEffects(json);
            if (spellEffects.Count == 0)
            {
                throw new InvalidOperationException("Processed spell data contains no legendary trait effects.");
            }
            return spellEffects;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load legendary spell effects from {SpellsUrl}", SpellsUrl);
            return new Dictionary<string, LegendaryTraitEffectDefinition>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<GameDataLookup> LoadGameDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await httpClient.GetStringAsync(GameDataUrl, cancellationToken).ConfigureAwait(false);
            var gameData = ParseGameData(json);
            if (gameData.QualityItemPowerBonuses.Count == 0 || gameData.TraitProgressions.Count == 0)
            {
                throw new InvalidOperationException("Game data contains no legendary progression or quality data.");
            }
            return gameData;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load legendary progression data from {GameDataUrl}", GameDataUrl);
            return GameDataLookup.Empty;
        }
    }

    private static LegendaryDefinitionsSnapshot Parse(
        string json,
        IReadOnlyDictionary<string, string> usNames,
        IReadOnlyDictionary<string, LegendaryTraitEffectDefinition> spellEffects,
        IReadOnlyDictionary<string, LegendaryItemPowerDefinition> itemPowers,
        GameDataLookup gameData)
    {
        using var document = JsonDocument.Parse(json);
        var traitConfiguration = document.RootElement
            .GetProperty("legendaries")
            .GetProperty("traitconfiguration");
        var legendaries = document.RootElement.GetProperty("legendaries");

        var traitVariables = new Dictionary<string, LegendaryTraitVariableDefinition>(StringComparer.OrdinalIgnoreCase);
        var variableConfiguration = traitConfiguration.GetProperty("traitvariables").GetProperty("traitvariableconfig");
        foreach (var variableElement in EnumerateArrayOrSingle(variableConfiguration))
        {
            var id = GetString(variableElement, "@id");
            if (!string.IsNullOrWhiteSpace(id)
                && TryGetDouble(variableElement, "@minfactor", out var minFactor)
                && TryGetDouble(variableElement, "@maxfactor", out var maxFactor))
            {
                var rollScaler = TryGetDouble(variableElement, "@rollscaler", out var configuredRollScaler)
                    ? configuredRollScaler
                    : 1d;
                traitVariables[id] = new LegendaryTraitVariableDefinition(minFactor, maxFactor, rollScaler);
            }
        }

        var traits = new Dictionary<string, LegendaryTraitDefinition>(StringComparer.OrdinalIgnoreCase);
        var traitPool = traitConfiguration.GetProperty("traitpool").GetProperty("trait");
        foreach (var traitElement in EnumerateArrayOrSingle(traitPool))
        {
            var id = GetString(traitElement, "@uniquename");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }
            var spell = GetString(traitElement, "@spell");
            var nameKey = GetString(traitElement, "@name");
            if (string.IsNullOrWhiteSpace(nameKey))
            {
                TraitNameKeysBySpell.TryGetValue(spell, out nameKey);
            }
            nameKey ??= string.Empty;
            var normalizedNameKey = NormalizeKey(nameKey);
            var usName = usNames.TryGetValue(normalizedNameKey, out var localizedName)
                && !string.IsNullOrWhiteSpace(localizedName)
                    ? localizedName
                    : id;
            spellEffects.TryGetValue(spell, out var effect);
            traits[id] = new LegendaryTraitDefinition(
                usName,
                effect,
                GetString(traitElement, "@rarity"),
                GetString(traitElement, "@var1config"));
        }

        var ratingScalings = new Dictionary<int, LegendaryRatingScalingDefinition>();
        var scalingElements = legendaries
            .GetProperty("globalsettings")
            .GetProperty("legendaryrating")
            .GetProperty("scaling");
        foreach (var scalingElement in EnumerateArrayOrSingle(scalingElements))
        {
            if (TryGetInt(scalingElement, "@tier", out var tier)
                && TryGetDouble(scalingElement, "@maxrating", out var maxRating)
                && TryGetDouble(scalingElement, "@ratingscaler", out var ratingScaler))
            {
                ratingScalings[tier] = new LegendaryRatingScalingDefinition(maxRating, ratingScaler);
            }
        }

        var itemBaseTraits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var itemGroups = legendaries.GetProperty("itemconfiguration").GetProperty("itemgroup");
        foreach (var itemGroup in EnumerateArrayOrSingle(itemGroups))
        {
            if (!TryGetInt(itemGroup, "@basetraits", out var baseTraits)
                || !itemGroup.TryGetProperty("item", out var itemElements))
            {
                continue;
            }
            foreach (var itemElement in EnumerateArrayOrSingle(itemElements))
            {
                var itemId = GetString(itemElement, "@id");
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    itemBaseTraits[itemId] = baseTraits;
                }
            }
        }

        return new LegendaryDefinitionsSnapshot(
            traits,
            usNames,
            traitVariables,
            itemPowers,
            gameData.QualityItemPowerBonuses,
            gameData.TraitProgressions,
            ratingScalings,
            itemBaseTraits);
    }

    private static IReadOnlyDictionary<string, string> ParseUsNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        var usNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var key = NormalizeKey(GetString(entry, "@tuid"));
            if (!IsRelevantLocalizationKey(key) || !entry.TryGetProperty("tuv", out var translations))
            {
                continue;
            }
            foreach (var translation in EnumerateArrayOrSingle(translations))
            {
                if (!string.Equals(GetString(translation, "@xml:lang"), "EN-US", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var name = GetString(translation, "seg");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    usNames[key] = name;
                }
                break;
            }
        }
        return usNames;
    }

    private static IReadOnlyDictionary<string, LegendaryItemPowerDefinition> ParseItemPowers(string json)
    {
        using var document = JsonDocument.Parse(json);
        var itemPowers = new Dictionary<string, LegendaryItemPowerDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in document.RootElement.EnumerateObject())
        {
            if (category.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var item in category.Value.EnumerateArray())
            {
                var uniqueName = GetString(item, "@uniquename");
                if (string.IsNullOrWhiteSpace(uniqueName) || !TryGetInt(item, "@itempower", out var baseItemPower))
                {
                    continue;
                }
                var enchantments = new Dictionary<int, int>();
                if (item.TryGetProperty("enchantments", out var enchantmentsElement)
                    && enchantmentsElement.TryGetProperty("enchantment", out var enchantmentElement))
                {
                    foreach (var enchantment in EnumerateArrayOrSingle(enchantmentElement))
                    {
                        if (TryGetInt(enchantment, "@enchantmentlevel", out var level)
                            && TryGetInt(enchantment, "@itempower", out var enchantmentItemPower))
                        {
                            enchantments[level] = enchantmentItemPower;
                        }
                    }
                }
                itemPowers[uniqueName] = new LegendaryItemPowerDefinition(baseItemPower, enchantments);
            }
        }
        return itemPowers;
    }

    private static IReadOnlyDictionary<string, LegendaryTraitEffectDefinition> ParseSpellEffects(string json)
    {
        using var document = JsonDocument.Parse(json);
        var spellEffects = new Dictionary<string, LegendaryTraitEffectDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var spell in document.RootElement.EnumerateArray())
        {
            var uniqueName = GetString(spell, "@uniquename");
            if (!uniqueName.StartsWith("PASSIVE_TRAIT_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            LegendaryTraitEffectDefinition? effect = null;
            if (spell.TryGetProperty("buff", out var buffs))
            {
                effect = ParseMatchingEffects(buffs, "@value");
            }
            else if (spell.TryGetProperty("healonhitpassive", out var heals))
            {
                effect = ParseMatchingEffects(heals, "@percentage", "lifesteal");
            }
            if (effect is not null)
            {
                spellEffects[uniqueName] = effect;
            }
        }
        return spellEffects;
    }

    private static LegendaryTraitEffectDefinition? ParseMatchingEffects(
        JsonElement effects,
        string valueProperty,
        string? forcedType = null)
    {
        LegendaryTraitEffectDefinition? first = null;
        foreach (var effect in EnumerateArrayOrSingle(effects))
        {
            var type = forcedType ?? GetString(effect, "@type");
            if (string.IsNullOrWhiteSpace(type) || !TryGetDouble(effect, valueProperty, out var baseValue))
            {
                return null;
            }
            if (first is null)
            {
                first = new LegendaryTraitEffectDefinition(type, baseValue);
            }
            else if (Math.Abs(first.BaseValue - baseValue) > 0.000000001d)
            {
                return null;
            }
        }
        return first;
    }

    private static GameDataLookup ParseGameData(string json)
    {
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.GetProperty("AO-GameData").GetProperty("Items");
        var progressions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var legendaryTraits = items.GetProperty("ItemPowerProgression").GetProperty("legendaryitemtraits");
        foreach (var property in legendaryTraits.EnumerateObject())
        {
            if (TryParseDouble(property.Value, out var progression))
            {
                progressions[NormalizeKey(property.Name)] = progression;
            }
        }

        var qualityBonuses = new Dictionary<int, int> { [1] = 0 };
        var qualityLevels = items.GetProperty("QualityLevels").GetProperty("qualitylevel");
        foreach (var qualityLevel in EnumerateArrayOrSingle(qualityLevels))
        {
            if (TryGetInt(qualityLevel, "@level", out var level)
                && TryGetInt(qualityLevel, "@itempowerbonus", out var bonus))
            {
                qualityBonuses[level] = bonus;
            }
        }
        return new GameDataLookup(qualityBonuses, progressions);
    }

    private bool TryGetItemPower(string itemUniqueName, int quality, out int itemPower)
    {
        var parts = itemUniqueName.Split('@', 2);
        var baseName = parts[0];
        var enchantmentLevel = parts.Length == 2
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLevel)
                ? parsedLevel
                : 0;
        if (!snapshot.ItemPowers.TryGetValue(baseName, out var itemDefinition)
            || !snapshot.QualityItemPowerBonuses.TryGetValue(quality, out var qualityBonus))
        {
            itemPower = 0;
            return false;
        }
        if (enchantmentLevel == 0)
        {
            itemPower = itemDefinition.BaseItemPower + qualityBonus;
            return true;
        }
        if (itemDefinition.EnchantmentItemPowers.TryGetValue(enchantmentLevel, out var enchantmentItemPower))
        {
            itemPower = enchantmentItemPower + qualityBonus;
            return true;
        }
        itemPower = 0;
        return false;
    }

    private double GetProgression(string effectType)
    {
        var progressionKey = effectType.ToLowerInvariant() switch
        {
            "hitpointsmax" => "hitpointsprogression",
            "energymax" => "energyprogression",
            "crowdcontrolresistance" => "crowdcontrolresistanceprogression",
            _ => effectType
        };
        return snapshot.TraitProgressions.TryGetValue(progressionKey, out var progression) ? progression : 1d;
    }

    private static string FormatCalculatedValue(
        double value,
        bool isPercentage,
        bool isDefense,
        bool isReduction,
        bool isEnergyCostReduction,
        CultureInfo culture)
    {
        string formatted;
        if (isDefense)
        {
            formatted = Truncate(value, 4).ToString("0.0000", culture);
        }
        else if (isReduction)
        {
            formatted = Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", culture);
        }
        else if (isEnergyCostReduction)
        {
            formatted = Truncate(value, 2).ToString("0.##", culture);
        }
        else
        {
            formatted = Math.Abs(value) >= 1d
                ? Truncate(value, 2).ToString("0.00", culture)
                : Truncate(value, 4).ToString("0.####", culture);
        }
        var prefix = value >= 0d ? "+" : string.Empty;
        return isPercentage ? $"{prefix}{formatted}%" : $"{prefix}{formatted}";
    }

    private static double Truncate(double value, int decimalPlaces)
    {
        var scale = Math.Pow(10d, decimalPlaces);
        return Math.Truncate(value * scale) / scale;
    }

    private static string FormatRawValue(double value, CultureInfo culture)
    {
        return value.ToString("N6", culture)
            .TrimEnd('0')
            .TrimEnd(culture.NumberFormat.NumberDecimalSeparator[0]);
    }

    private static IEnumerable<JsonElement> EnumerateArrayOrSingle(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [element];
    }

    private static string GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return string.Empty;
        }
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && int.TryParse(property.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetDouble(JsonElement element, string name, out double value)
    {
        value = 0d;
        return element.TryGetProperty(name, out var property) && TryParseDouble(property, out value);
    }

    private static bool TryParseDouble(JsonElement element, out double value)
    {
        return double.TryParse(element.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeKey(string key)
    {
        return key.Trim().TrimStart('@');
    }

    private static bool IsRelevantLocalizationKey(string key)
    {
        return key.StartsWith("ITEMS_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("ITEMDETAILS_STATS_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("TRAIT_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("TRAITS_", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record GameDataLookup(
        IReadOnlyDictionary<int, int> QualityItemPowerBonuses,
        IReadOnlyDictionary<string, double> TraitProgressions)
    {
        public static GameDataLookup Empty { get; } = new(
            new Dictionary<int, int>(),
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
    }
}

using System;

namespace AlbionDataAvalonia.Items;

public static class ItemNameFormatter
{
    public static string FormatUsName(string uniqueName, string usName)
    {
        if (string.IsNullOrWhiteSpace(usName))
        {
            return uniqueName;
        }

        if (HasTierEnchantmentSuffix(usName)
            || !TryGetTierAndEnchantment(uniqueName, out var tier, out var enchantment))
        {
            return usName;
        }

        return $"{usName} [{tier}.{enchantment}]";
    }

    private static bool HasTierEnchantmentSuffix(string usName)
    {
        return usName.EndsWith(']')
            && usName.LastIndexOf('[') >= 0;
    }

    private static bool TryGetTierAndEnchantment(string uniqueName, out int tier, out int enchantment)
    {
        tier = 0;
        enchantment = 0;

        if (string.IsNullOrWhiteSpace(uniqueName))
        {
            return false;
        }

        var normalizedUniqueName = uniqueName.Trim();
        var underscoreIndex = normalizedUniqueName.IndexOf('_', StringComparison.Ordinal);
        if (underscoreIndex <= 1
            || normalizedUniqueName[0] != 'T'
            || !int.TryParse(normalizedUniqueName.AsSpan(1, underscoreIndex - 1), out tier))
        {
            return false;
        }

        var enchantmentIndex = normalizedUniqueName.LastIndexOf('@');
        if (enchantmentIndex >= 0
            && enchantmentIndex + 1 < normalizedUniqueName.Length
            && int.TryParse(normalizedUniqueName.AsSpan(enchantmentIndex + 1), out var parsedEnchantment))
        {
            enchantment = parsedEnchantment;
        }

        return true;
    }
}

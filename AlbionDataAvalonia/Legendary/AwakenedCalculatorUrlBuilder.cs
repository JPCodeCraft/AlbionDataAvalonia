using AlbionDataAvalonia.Legendary.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlbionDataAvalonia.Legendary;

public static class AwakenedCalculatorUrlBuilder
{
    private const string CalculatorUrl = "https://albionfreemarket.com/awakened-calculator";

    public static string? Build(LegendaryItem item)
    {
        var server = item.AlbionServerId switch
        {
            1 => "west",
            2 => "east",
            3 => "europe",
            _ => null
        };
        if (server is null || string.IsNullOrWhiteSpace(item.ItemUniqueName) || item.Quality is < 1 or > 5)
        {
            return null;
        }

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("v", "1"),
            new("tab", "calculator"),
            new("server", server),
            new("item", item.ItemUniqueName),
            new("quality", item.Quality.ToString(CultureInfo.InvariantCulture)),
            new("masteryIp", "0"),
            new("acquisition", "offered"),
            new("focus", "0"),
            new("focusAmount", "30000")
        };

        var traits = item.Traits.OrderBy(trait => trait.Position).Take(3).ToArray();
        for (var index = 0; index < traits.Length; index++)
        {
            parameters.Add(new($"t{index + 1}", traits[index].TraitId));
            parameters.Add(new($"g{index + 1}", traits[index].Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        if (item.Strain is >= 1 && double.IsFinite(item.Strain.Value))
        {
            parameters.Add(new("strain", item.Strain.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        return $"{CalculatorUrl}?{string.Join('&', parameters.Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"))}";
    }
}

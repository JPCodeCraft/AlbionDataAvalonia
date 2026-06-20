using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Items;

public sealed record ItemQualityOption(int Index, string Name)
{
    public override string ToString() => Name;
}

public static class ItemQuality
{
    public static readonly ItemQualityOption UnknownOption = new(0, "Unknown");

    public static readonly IReadOnlyList<ItemQualityOption> Options =
    [
        new(1, "Normal"),
        new(2, "Good"),
        new(3, "Outstanding"),
        new(4, "Excellent"),
        new(5, "Masterpiece")
    ];

    public static readonly IReadOnlyList<ItemQualityOption> OptionsWithUnknown =
    [
        UnknownOption,
        ..Options
    ];

    public static string Format(int? quality)
    {
        return OptionsWithUnknown.FirstOrDefault(option => option.Index == quality)?.Name ?? UnknownOption.Name;
    }
}

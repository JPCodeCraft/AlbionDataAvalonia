using AlbionDataAvalonia.Network.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Ui;

/// <summary>What selectors show for an option, whatever its value type.</summary>
public interface IOption
{
    string Label { get; }
}

/// <summary>A value with the label a combo box or segmented selector shows for it.</summary>
public sealed record Option<T>(T Value, string Label) : IOption
{
    public override string ToString() => Label;
}

/// <summary>Option lists that more than one screen offers.</summary>
public static class UiOptions
{
    /// <summary>How many trades to load.</summary>
    public static IReadOnlyList<Option<int>> TradeCounts { get; } = Counts(100, 250, 500, 1000, 2000, 5000, 10000);

    /// <summary>How many log lines to keep on screen.</summary>
    public static IReadOnlyList<Option<int>> LogCounts { get; } = Counts(100, 500, 1000, 2000, 3000, 4000, 5000);

    /// <summary>Every server by ID, after "Any" for no server filter.</summary>
    public static IReadOnlyList<Option<int?>> Servers { get; } =
        [new(null, UiText.Any), .. AlbionServers.GetAll().Select(server => new Option<int?>(server.Id, server.Name))];

    /// <summary>The option for a value, or the nearest one for a number that isn't offered.</summary>
    public static Option<int> Nearest(IReadOnlyList<Option<int>> options, int value) =>
        options.MinBy(option => Math.Abs(option.Value - value))!;

    /// <summary>The option whose value matches, or the first option.</summary>
    public static Option<T> Find<T>(IReadOnlyList<Option<T>> options, T value) =>
        options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value)) ?? options[0];

    private static Option<int>[] Counts(params int[] values) => values.Select(value => new Option<int>(value, UiFormat.Whole(value))).ToArray();
}

using System.Collections.Generic;

namespace AlbionDataAvalonia.ViewModels;

public sealed class NumericOption
{
    public int Value { get; }
    public string Label { get; }

    public NumericOption(int value, string label)
    {
        Value = value;
        Label = label;
    }

    public override string ToString()
    {
        return Label;
    }
}

public static class NumericOptions
{
    public static readonly IReadOnlyList<NumericOption> MailAndTradeLoadOptions =
    [
        new NumericOption(100, "100"),
        new NumericOption(250, "250"),
        new NumericOption(500, "500"),
        new NumericOption(1000, "1,000"),
        new NumericOption(2000, "2,000"),
        new NumericOption(5000, "5,000"),
        new NumericOption(10000, "10,000")
    ];
}

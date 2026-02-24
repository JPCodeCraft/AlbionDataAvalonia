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

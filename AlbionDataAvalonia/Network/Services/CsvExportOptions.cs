using System;
using System.Globalization;

namespace AlbionDataAvalonia.Network.Services;

public sealed class CsvExportOptions
{
    public CsvExportOptions(string delimiter, string decimalSeparator)
    {
        if (string.IsNullOrEmpty(delimiter))
        {
            throw new ArgumentException("CSV delimiter cannot be empty.", nameof(delimiter));
        }

        if (string.IsNullOrEmpty(decimalSeparator))
        {
            throw new ArgumentException("Decimal separator cannot be empty.", nameof(decimalSeparator));
        }

        if (delimiter == decimalSeparator)
        {
            throw new ArgumentException("CSV delimiter and decimal separator must be different.", nameof(decimalSeparator));
        }

        Delimiter = delimiter;
        DecimalSeparator = decimalSeparator;
    }

    public string Delimiter { get; }

    public string DecimalSeparator { get; }

    public static CsvExportOptions FromCurrentCulture(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return new CsvExportOptions(
            culture.TextInfo.ListSeparator,
            culture.NumberFormat.NumberDecimalSeparator);
    }

    public CultureInfo CreateFormattingCulture(CultureInfo? baseCulture = null)
    {
        var culture = (CultureInfo)(baseCulture ?? CultureInfo.CurrentCulture).Clone();
        culture.NumberFormat.NumberDecimalSeparator = DecimalSeparator;
        culture.NumberFormat.CurrencyDecimalSeparator = DecimalSeparator;
        culture.NumberFormat.PercentDecimalSeparator = DecimalSeparator;
        return culture;
    }
}

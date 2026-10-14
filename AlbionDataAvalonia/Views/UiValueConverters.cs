using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace AlbionDataAvalonia.Views;

// Presentation only: stored values, sorting and exports retain their original precision and UTC timestamps.
public static class UiValueConverters
{
    public static readonly FuncValueConverter<object?, string> LocalDateTime = new(value => FormatLocalDate(value, "g"));
    public static readonly FuncValueConverter<object?, string> LocalTimestamp = new(value => FormatLocalDate(value, "G"));

    private static string FormatLocalDate(object? value, string format) => value switch
    {
        DateTime date => (date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : date)
            .ToLocalTime().ToString(format, CultureInfo.CurrentCulture),
        DateTimeOffset date => date.ToLocalTime().ToString(format, CultureInfo.CurrentCulture),
        _ => ""
    };

    public static readonly FuncValueConverter<object?, string> WholeNumber = new(value =>
        value is IFormattable number ? number.ToString("N0", CultureInfo.CurrentCulture) : "");
}

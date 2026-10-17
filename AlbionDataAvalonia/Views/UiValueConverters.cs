using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Ui;
using Avalonia.Data.Converters;
using Avalonia.Media;
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
        DateTime date => UiFormat.ToLocal(date).ToString(format, CultureInfo.CurrentCulture),
        DateTimeOffset date => date.ToLocalTime().ToString(format, CultureInfo.CurrentCulture),
        _ => ""
    };

    public static readonly FuncValueConverter<object?, string> WholeNumber = new(value =>
        value is IFormattable number ? number.ToString("N0", CultureInfo.CurrentCulture) : "");

    // The color of a kind of stored data; null is the rest of the database.
    public static readonly FuncValueConverter<DataCleanupType?, IBrush> StorageBrush = new(type => UiPalette.Brush(ActivityPalette.Storage(type)));

    // Shows an empty state while a collection has no items.
    public static readonly FuncValueConverter<int, bool> IsZero = new(count => count == 0);
}

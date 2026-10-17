using System.Globalization;

namespace AlbionDataAvalonia.Ui;

/// <summary>Formats counts, numbers and times the same way on every screen.</summary>
public static class UiFormat
{
    /// <summary>"1 trade", "2,330 trades". Pass the plural when it isn't the singular plus "s".</summary>
    public static string Count(long count, string singular, string? plural = null) =>
        $"{count.ToString("N0", CultureInfo.CurrentCulture)} {Noun(count, singular, plural)}";

    /// <summary>The singular for exactly one, otherwise the plural.</summary>
    public static string Noun(long count, string singular, string? plural = null) =>
        count == 1 ? singular : plural ?? singular + "s";
}

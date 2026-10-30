using System;
using System.Globalization;

namespace AlbionDataAvalonia.Ui;

/// <summary>
/// Formats counts, numbers and times the same way on every screen. Everything here is for display only: stored
/// values, sorting and exports keep their full precision and UTC timestamps.
/// </summary>
public static class UiFormat
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>"1 trade", "2,330 trades". Pass the plural when it isn't the singular plus "s".</summary>
    public static string Count(long count, string singular, string? plural = null) =>
        $"{Whole(count)} {Noun(count, singular, plural)}";

    /// <summary>The singular for exactly one, otherwise the plural.</summary>
    public static string Noun(long count, string singular, string? plural = null) =>
        count == 1 ? singular : plural ?? singular + "s";

    /// <summary>A whole number with group separators: "12,480".</summary>
    public static string Whole(double value) => value.ToString("N0", Culture);

    /// <summary>
    /// A short number as on the AFM website: 5.6, 845, 12.3k, 345k, 12.3M, 345M, 1.2B. Whole numbers under 10 stay whole.
    /// </summary>
    public static string Compact(double value)
    {
        var size = Math.Abs(value);
        var text = size < 10 ? size.ToString(size % 1 == 0 ? "0" : "0.0", Culture)
            : size < 1_000 ? size.ToString("0", Culture)
            : size < 100_000 ? (size / 1_000).ToString("0.0", Culture) + "k"
            : size < 1_000_000 ? (size / 1_000).ToString("0", Culture) + "k"
            : size < 100_000_000 ? (size / 1_000_000).ToString("0.0", Culture) + "M"
            : size < 1_000_000_000 ? (size / 1_000_000).ToString("0", Culture) + "M"
            : (size / 1_000_000_000).ToString("0.0", Culture) + "B";
        return value < 0 ? "-" + text : text;
    }

    /// <summary>A file or database size: "812 B", "540.1 KB", "12.3 MB".</summary>
    public static string Bytes(long bytes) => bytes < 1024 ? $"{Whole(bytes)} B"
        : bytes < 1024L * 1024 ? $"{(bytes / 1024d).ToString("N1", Culture)} KB"
        : bytes < 1024L * 1024 * 1024 ? $"{(bytes / (1024d * 1024)).ToString("N1", Culture)} MB"
        : $"{(bytes / (1024d * 1024 * 1024)).ToString("N1", Culture)} GB";

    /// <summary>The two largest units: "2d 5h", "5h 22m", "12m 5s", "45s".</summary>
    public static string Duration(TimeSpan duration)
    {
        duration = TimeSpan.FromSeconds(Math.Floor(Math.Max(0, duration.TotalSeconds)));
        return duration.TotalDays >= 1 ? $"{(int)duration.TotalDays}d {duration.Hours}h"
            : duration.TotalHours >= 1 ? $"{duration.Hours}h {duration.Minutes}m"
            : duration.TotalMinutes >= 1 ? $"{duration.Minutes}m {duration.Seconds}s"
            : $"{duration.Seconds}s";
    }

    public static string Duration(double seconds) => Duration(TimeSpan.FromSeconds(Math.Max(0, seconds)));

    /// <summary>A running clock: "4:05" under an hour, then "1:04:05".</summary>
    public static string Clock(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";
    }

    /// <summary>"just now", "12 min ago", then the local time once it is an hour old.</summary>
    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var age = nowUtc - utc;
        return age < TimeSpan.FromMinutes(1) ? "just now"
            : age < TimeSpan.FromHours(1) ? $"{(int)age.TotalMinutes} min ago"
            : LocalShortTime(utc);
    }

    /// <summary>"just now", "25 min ago", "3h 12m ago", then "1d 4h ago", for things that can be days old.</summary>
    public static string TimeAgo(DateTime utc, DateTime nowUtc)
    {
        var age = nowUtc - utc;
        return age < TimeSpan.FromMinutes(1) ? "just now"
            : age < TimeSpan.FromHours(1) ? $"{(int)age.TotalMinutes} min ago"
            : age < TimeSpan.FromDays(1) ? $"{(int)age.TotalHours}h {age.Minutes}m ago"
            : $"{(int)age.TotalDays}d {age.Hours}h ago";
    }

    /// <summary>The local time with seconds in the user's format, such as "14:05:09" or "2:05:09 PM".</summary>
    public static string LocalTime(DateTime utc) => ToLocal(utc).ToString("T", Culture);

    /// <summary>The local time without seconds: "14:05" or "2:05 PM".</summary>
    public static string LocalShortTime(DateTime utc) => ToLocal(utc).ToString("t", Culture);

    /// <summary>The local date and time without seconds.</summary>
    public static string LocalDateTime(DateTime utc) => ToLocal(utc).ToString("g", Culture);

    /// <summary>The local date and time with seconds, for logs.</summary>
    public static string LocalTimestamp(DateTime utc) => ToLocal(utc).ToString("G", Culture);

    /// <summary>The clock a time picker shows: "24HourClock" when the user's times run 0–23, else "12HourClock".</summary>
    public static string ClockIdentifier => Culture.DateTimeFormat.ShortTimePattern.Contains('H') ? "24HourClock" : "12HourClock";

    /// <summary>Treats an unspecified kind as UTC, which is how the database stores times.</summary>
    public static DateTime ToLocal(DateTime value) =>
        (value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value).ToLocalTime();
}

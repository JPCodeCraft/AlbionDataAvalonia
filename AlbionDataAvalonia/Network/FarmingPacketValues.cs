using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network;

internal static class FarmingPacketValues
{
    public static bool TryRead(Action read)
    {
        try { read(); return true; }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            Log.Debug(ex, "Skipped unsupported farming packet fields");
            return false;
        }
    }

    public static long Number(Dictionary<byte, object> values, byte key) => values.TryGetValue(key, out var value) ? value.ToLong() : 0;
    public static string? Text(Dictionary<byte, object> values, byte key) => values.TryGetValue(key, out var value) && value is string text
        && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    public static string? OptionalText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum ? value.Trim() : null;
    public static string[] Strings(Dictionary<byte, object> values, byte key)
    {
        if (!values.TryGetValue(key, out var value)) return [];
        if (value is not Array array || array.Cast<object?>().Any(item => item is not string))
            throw new InvalidCastException("Expected farming string array");
        return array.Cast<string>().ToArray();
    }
    public static string? GuidValue(Dictionary<byte, object> values, byte key) => values.TryGetValue(key, out var value) ? value.ToGuid()?.ToString() : null;
    public static DateTime? UtcTicks(long value) => value >= DateTime.UnixEpoch.Ticks && value <= DateTime.MaxValue.Ticks ? new DateTime(value, DateTimeKind.Utc) : null;

}

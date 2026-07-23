using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Handlers;

internal static class DebugProbeFormatter
{
    public static string FormatParameters(Dictionary<byte, object> parameters)
    {
        return string.Join(
            "; ",
            parameters
                .OrderBy(parameter => parameter.Key)
                .Select(parameter =>
                    $"{parameter.Key}:{GetTypeName(parameter.Value)}={FormatValue(parameter.Value)}"));
    }

    private static string GetTypeName(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        var type = value.GetType();
        if (!type.IsArray)
        {
            return type.Name;
        }

        return $"{type.GetElementType()?.Name ?? "object"}[]";
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is string text)
        {
            return $"\"{text}\"";
        }

        if (value is byte[] bytes)
        {
            return $"[{string.Join(",", bytes)}]";
        }

        if (value is IDictionary dictionary)
        {
            return $"{{{string.Join(
                ",",
                dictionary.Keys
                    .Cast<object>()
                    .Select(key => $"{FormatValue(key)}:{FormatValue(dictionary[key])}"))}}}";
        }

        if (value is IEnumerable enumerable)
        {
            return $"[{string.Join(",", enumerable.Cast<object>().Select(FormatValue))}]";
        }

        return value.ToString() ?? string.Empty;
    }
}

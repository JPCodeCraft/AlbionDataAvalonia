using System;

namespace AlbionDataAvalonia.Settings;

public static class AppSettingsExtensions
{
    public static Uri GetAfmBackendApiBaseUri(this AppSettings settings)
    {
        var value = settings.AfmBackendApiBase;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = settings.AfmAuthApiUrl;
            if (value.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^"/api".Length];
            }
        }

        value = string.IsNullOrWhiteSpace(value)
            ? "https://api.albionfreemarket.com/be"
            : value;
        return new Uri(value.TrimEnd('/') + "/", UriKind.Absolute);
    }
}

using System;
using Serilog.Events;

namespace AlbionDataAvalonia.Logging;

public class LogEventWrapper
{
    private static readonly string[] PublicUploadMessagePrefixes =
    {
        "Public market upload complete",
        "Gold price upload complete",
        "Market history upload complete",
        "Bandit event upload complete"
    };

    public LogEvent LogEvent { get; }

    public string RenderedMessage
    {
        get
        {
            var message = LogEvent.RenderMessage();
            if (LogEvent.Exception != null)
            {
                message += $"\nException: {LogEvent.Exception}";
            }
            return message;
        }
    }

    public string? PublicUploadUrl { get; }

    public bool HasPublicUploadLink => !string.IsNullOrWhiteSpace(PublicUploadUrl);

    public LogEventWrapper(LogEvent logEvent)
    {
        LogEvent = logEvent;
        PublicUploadUrl = TryBuildPublicUploadUrl(logEvent);
    }

    private static string? TryBuildPublicUploadUrl(LogEvent logEvent)
    {
        var messageTemplate = logEvent.MessageTemplate.Text;
        if (messageTemplate is null)
        {
            return null;
        }

        var matchesPrefix = false;
        foreach (var prefix in PublicUploadMessagePrefixes)
        {
            if (messageTemplate.StartsWith(prefix, StringComparison.Ordinal))
            {
                matchesPrefix = true;
                break;
            }
        }

        if (!matchesPrefix)
        {
            return null;
        }

        if (!logEvent.Properties.TryGetValue("identifier", out var identifierValue))
        {
            return null;
        }

        var identifier = ExtractScalarValue(identifierValue);

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var identifierPath = Uri.EscapeDataString(identifier);

        return $"https://albionfreemarket.com/identifiers/{identifierPath}";
    }

    private static string? ExtractScalarValue(LogEventPropertyValue propertyValue)
    {
        if (propertyValue is ScalarValue scalar)
        {
            return scalar.Value?.ToString();
        }

        return propertyValue.ToString();
    }
}

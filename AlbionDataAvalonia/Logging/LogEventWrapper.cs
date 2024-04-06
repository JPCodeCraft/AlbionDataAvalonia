using Serilog.Events;

namespace AlbionDataAvalonia.Logging;

public class LogEventWrapper
{
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

    public LogEventWrapper(LogEvent logEvent)
    {
        LogEvent = logEvent;
    }
}


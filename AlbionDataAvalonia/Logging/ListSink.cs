using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;

namespace AlbionDataAvalonia.Logging;

public class ListSink : ILogEventSink
{
    private int _maxEvents = 50;

    public ConcurrentQueue<LogEventWrapper> Events { get; } = new ConcurrentQueue<LogEventWrapper>();

    public event Action? CollectionChanged;

    public void Emit(LogEvent logEvent)
    {
        Events.Enqueue(new LogEventWrapper(logEvent));
        while (Events.Count > _maxEvents)
        {
            Events.TryDequeue(out _);
        }
        CollectionChanged?.Invoke();
    }
}


public class LogEventWrapper
{
    public LogEvent LogEvent { get; }

    public string RenderedMessage => LogEvent.RenderMessage();

    public LogEventWrapper(LogEvent logEvent)
    {
        LogEvent = logEvent;
    }
}


using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;

namespace AlbionDataAvalonia.Logging;

public class ListSink : ILogEventSink
{
    public const int MemoryRetentionLimit = 100_000;

    public ConcurrentQueue<LogEventWrapper> Events { get; } = new ConcurrentQueue<LogEventWrapper>();

    public event Action<LogEventWrapper>? CollectionChanged;

    public void Emit(LogEvent logEvent)
    {
        var logEventWrapper = new LogEventWrapper(logEvent);
        Events.Enqueue(logEventWrapper);
        while (Events.Count > MemoryRetentionLimit)
        {
            Events.TryDequeue(out _);
        }
        CollectionChanged?.Invoke(logEventWrapper);
    }
}

using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;

namespace AlbionDataAvalonia.Logging;

public class ListSink : ILogEventSink
{
    // The Logs page shows at most 5,000 lines; twice that leaves room for its filters without holding hours of
    // verbose events (often over 100 MB) in memory.
    public const int MemoryRetentionLimit = 10_000;

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

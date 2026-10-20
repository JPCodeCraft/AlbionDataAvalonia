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

    // Warnings and errors kept apart, so busy verbose logging can't push them out before Copy diagnostics reads them.
    private const int ProblemRetentionLimit = 200;

    public ConcurrentQueue<LogEventWrapper> Events { get; } = new ConcurrentQueue<LogEventWrapper>();

    public ConcurrentQueue<LogEvent> RecentProblems { get; } = new ConcurrentQueue<LogEvent>();

    public event Action<LogEventWrapper>? CollectionChanged;

    public void Emit(LogEvent logEvent)
    {
        var logEventWrapper = new LogEventWrapper(logEvent);
        Events.Enqueue(logEventWrapper);
        while (Events.Count > MemoryRetentionLimit)
        {
            Events.TryDequeue(out _);
        }
        if (logEvent.Level >= LogEventLevel.Warning)
        {
            RecentProblems.Enqueue(logEvent);
            while (RecentProblems.Count > ProblemRetentionLimit)
            {
                RecentProblems.TryDequeue(out _);
            }
        }
        CollectionChanged?.Invoke(logEventWrapper);
    }
}

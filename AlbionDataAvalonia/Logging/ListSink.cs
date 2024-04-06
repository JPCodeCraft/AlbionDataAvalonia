using AlbionDataAvalonia.Settings;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;

namespace AlbionDataAvalonia.Logging;

public class ListSink : ILogEventSink
{
    private readonly SettingsManager _settingsManager;
    public ListSink(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    public ConcurrentQueue<LogEventWrapper> Events { get; } = new ConcurrentQueue<LogEventWrapper>();

    public event Action<LogEventWrapper>? CollectionChanged;

    public void Emit(LogEvent logEvent)
    {
        var logEventWrapper = new LogEventWrapper(logEvent);
        Events.Enqueue(logEventWrapper);
        while (Events.Count > _settingsManager.UserSettings.MaxLogCount)
        {
            Events.TryDequeue(out _);
        }
        CollectionChanged?.Invoke(logEventWrapper);
    }
}
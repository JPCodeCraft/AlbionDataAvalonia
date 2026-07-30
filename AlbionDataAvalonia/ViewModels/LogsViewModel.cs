using AlbionDataAvalonia.Logging;
using AlbionDataAvalonia.Settings;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace AlbionDataAvalonia.ViewModels
{
    public partial class LogsViewModel : ViewModelBase, IDisposable
    {
        private const int MaxIncrementalLogBatchCount = 100;
        private readonly TimeSpan _filterDebounceInterval = TimeSpan.FromMilliseconds(250);

        private readonly record struct PendingLogEvent(
            LogEventWrapper LogEvent,
            LogEventWrapper? RemovedBufferedEvent);

        public sealed class AmountShownOption
        {
            public int Value { get; }
            public string Label { get; }

            public AmountShownOption(int value, string label)
            {
                Value = value;
                Label = label;
            }

            public override string ToString()
            {
                return Label;
            }
        }

        private readonly ListSink _listSink;
        private readonly SettingsManager _settingsManager;
        private readonly List<LogEventWrapper> _allEventsNewestFirst = new();
        private readonly List<PendingLogEvent> _pendingLogEvents = new();
        private readonly object _sync = new();
        private IDisposable? _pendingFilterRefreshRegistration;
        private string _appliedFilterText = string.Empty;
        private bool _logDispatchScheduled;
        private bool _pendingLogEventsRequireRebuild;
        private volatile bool _isDisposed;
        private static readonly IReadOnlyList<AmountShownOption> _amountShownOptions =
        [
            new AmountShownOption(100, "100"),
            new AmountShownOption(500, "500"),
            new AmountShownOption(1000, "1,000"),
            new AmountShownOption(2000, "2,000"),
            new AmountShownOption(3000, "3,000"),
            new AmountShownOption(4000, "4,000"),
            new AmountShownOption(5000, "5,000")
        ];
        private static readonly IReadOnlyList<LogEventLevel> _logVerbosityOptions = Enum
            .GetValues<LogEventLevel>()
            .Cast<LogEventLevel>()
            .ToArray();

        [ObservableProperty]
        private string filterText = string.Empty;

        public ObservableCollection<LogEventWrapper> Events { get; } = new();

        public UserSettings UserSettings => _settingsManager.UserSettings;
        public IReadOnlyList<AmountShownOption> AmountShownOptions => _amountShownOptions;
        public IReadOnlyList<LogEventLevel> LogVerbosityOptions => _logVerbosityOptions;

        partial void OnFilterTextChanged(string? oldValue, string newValue) => ScheduleFilterEvents();

        public AmountShownOption SelectedAmountShown
        {
            get
            {
                return _amountShownOptions.First(option => option.Value == GetVisibleLimit());
            }
            set
            {
                if (value is null)
                {
                    return;
                }

                if (_settingsManager.UserSettings.MaxLogCount != value.Value)
                {
                    _settingsManager.UserSettings.MaxLogCount = value.Value;
                }
            }
        }

        public LogEventLevel SelectedLogVerbosity
        {
            get => _settingsManager.UserSettings.LogLevel;
            set
            {
                if (_settingsManager.UserSettings.LogLevel != value)
                {
                    _settingsManager.UserSettings.LogLevel = value;
                }
            }
        }

        public LogsViewModel(ListSink listSink, SettingsManager settingsManager)
        {
            _listSink = listSink;
            _settingsManager = settingsManager;
            EnsureVisibleCountMatchesOptions();

            var snapshot = _listSink.Events.ToArray();
            for (var i = snapshot.Length - 1; i >= 0; i--)
            {
                _allEventsNewestFirst.Add(snapshot[i]);
            }
            TrimBufferedEvents();
            RebuildVisibleEvents();

            _listSink.CollectionChanged += OnLogEventReceived;
            _settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        }

        private void OnLogEventReceived(LogEventWrapper logEventWrapper)
        {
            LogEventWrapper? removedBufferedEvent = null;
            var shouldScheduleDispatch = false;
            lock (_sync)
            {
                if (_isDisposed)
                {
                    return;
                }

                _allEventsNewestFirst.Insert(0, logEventWrapper);
                if (_allEventsNewestFirst.Count > ListSink.MemoryRetentionLimit)
                {
                    removedBufferedEvent = _allEventsNewestFirst[^1];
                    _allEventsNewestFirst.RemoveAt(_allEventsNewestFirst.Count - 1);
                }

                if (!_pendingLogEventsRequireRebuild)
                {
                    if (_pendingLogEvents.Count < MaxIncrementalLogBatchCount)
                    {
                        _pendingLogEvents.Add(new PendingLogEvent(logEventWrapper, removedBufferedEvent));
                    }
                    else
                    {
                        _pendingLogEvents.Clear();
                        _pendingLogEventsRequireRebuild = true;
                    }
                }

                if (!_logDispatchScheduled)
                {
                    _logDispatchScheduled = true;
                    shouldScheduleDispatch = true;
                }
            }

            if (shouldScheduleDispatch)
            {
                Dispatcher.UIThread.Post(DrainPendingLogEvents);
            }
        }

        private void DrainPendingLogEvents()
        {
            List<PendingLogEvent>? pendingEvents;
            bool rebuildVisibleEvents;
            lock (_sync)
            {
                if (_isDisposed)
                {
                    _pendingLogEvents.Clear();
                    _pendingLogEventsRequireRebuild = false;
                    _logDispatchScheduled = false;
                    return;
                }

                rebuildVisibleEvents = _pendingLogEventsRequireRebuild;
                pendingEvents = rebuildVisibleEvents
                    ? null
                    : new List<PendingLogEvent>(_pendingLogEvents);
                _pendingLogEvents.Clear();
                _pendingLogEventsRequireRebuild = false;
                _logDispatchScheduled = false;
            }

            if (_isDisposed)
            {
                return;
            }

            if (rebuildVisibleEvents)
            {
                RebuildVisibleEvents();
                return;
            }

            if (pendingEvents is null)
            {
                return;
            }

            foreach (var pendingEvent in pendingEvents)
            {
                ApplyIncomingEvent(pendingEvent.LogEvent, pendingEvent.RemovedBufferedEvent);
            }
        }

        private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UserSettings.LogLevel))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_isDisposed)
                    {
                        return;
                    }

                    OnPropertyChanged(nameof(SelectedLogVerbosity));
                    RebuildVisibleEvents();
                });
                return;
            }

            if (e.PropertyName == nameof(UserSettings.MaxLogCount))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_isDisposed)
                    {
                        return;
                    }

                    OnPropertyChanged(nameof(SelectedAmountShown));
                    RebuildVisibleEvents();
                });
            }
        }

        private void RebuildVisibleEvents()
        {
            List<LogEventWrapper> visibleEvents;
            lock (_sync)
            {
                visibleEvents = _allEventsNewestFirst
                    .Where(PassesActiveFilters)
                    .Take(GetVisibleLimit())
                    .ToList();
                _pendingLogEvents.Clear();
                _pendingLogEventsRequireRebuild = false;
            }

            ReplaceVisibleEvents(visibleEvents);
        }

        private void ReplaceVisibleEvents(IEnumerable<LogEventWrapper> visibleEvents)
        {
            Events.Clear();
            foreach (var logEvent in visibleEvents)
            {
                Events.Add(logEvent);
            }
        }

        private void ApplyIncomingEvent(LogEventWrapper logEventWrapper, LogEventWrapper? removedBufferedEvent)
        {
            if (removedBufferedEvent is not null)
            {
                Events.Remove(removedBufferedEvent);
            }

            if (!PassesActiveFilters(logEventWrapper))
            {
                return;
            }

            Events.Insert(0, logEventWrapper);
            TrimVisibleEvents();
        }

        private bool PassesActiveFilters(LogEventWrapper logEventWrapper)
        {
            return PassesCurrentFilter(logEventWrapper) && PassesTextFilter(logEventWrapper);
        }

        private bool PassesTextFilter(LogEventWrapper logEventWrapper)
        {
            var normalizedFilterText = NormalizeFilterText(_appliedFilterText);
            if (string.IsNullOrEmpty(normalizedFilterText))
            {
                return true;
            }

            return NormalizeFilterText(logEventWrapper.RenderedMessage)
                .Contains(normalizedFilterText, StringComparison.OrdinalIgnoreCase);
        }

        private bool PassesCurrentFilter(LogEventWrapper logEventWrapper)
        {
            return logEventWrapper.LogEvent.Level >= _settingsManager.UserSettings.LogLevel;
        }

        private int GetVisibleLimit()
        {
            return NormalizeVisibleLimit(_settingsManager.UserSettings.MaxLogCount);
        }

        private void TrimVisibleEvents()
        {
            var limit = GetVisibleLimit();
            while (Events.Count > limit)
            {
                Events.RemoveAt(Events.Count - 1);
            }
        }

        private void TrimBufferedEvents()
        {
            while (_allEventsNewestFirst.Count > ListSink.MemoryRetentionLimit)
            {
                _allEventsNewestFirst.RemoveAt(_allEventsNewestFirst.Count - 1);
            }
        }

        private void EnsureVisibleCountMatchesOptions()
        {
            var normalized = NormalizeVisibleLimit(_settingsManager.UserSettings.MaxLogCount);
            if (_settingsManager.UserSettings.MaxLogCount != normalized)
            {
                _settingsManager.UserSettings.MaxLogCount = normalized;
            }
        }

        private static int NormalizeVisibleLimit(int count)
        {
            if (_amountShownOptions.Count == 0)
            {
                return 100;
            }

            var nearest = _amountShownOptions
                .OrderBy(option => Math.Abs(option.Value - count))
                .First();

            return nearest.Value;
        }

        private void ScheduleFilterEvents()
        {
            if (_isDisposed)
            {
                return;
            }

            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ScheduleFilterEvents);
                return;
            }

            CancelPendingFilterRefresh();
            _pendingFilterRefreshRegistration = DispatcherTimer.RunOnce(() =>
            {
                _pendingFilterRefreshRegistration = null;
                if (_isDisposed)
                {
                    return;
                }

                _appliedFilterText = FilterText;
                RebuildVisibleEvents();
            }, _filterDebounceInterval);
        }

        private void CancelPendingFilterRefresh()
        {
            _pendingFilterRefreshRegistration?.Dispose();
            _pendingFilterRefreshRegistration = null;
        }

        private static string NormalizeFilterText(string? value)
        {
            // Keep logs search aligned with the existing mails/trades space-insensitive matching.
            return (value ?? string.Empty).Replace(" ", string.Empty);
        }

        public void Dispose()
        {
            _listSink.CollectionChanged -= OnLogEventReceived;
            _settingsManager.UserSettings.PropertyChanged -= OnUserSettingsPropertyChanged;

            lock (_sync)
            {
                _isDisposed = true;
                _pendingLogEvents.Clear();
                _pendingLogEventsRequireRebuild = false;
                _logDispatchScheduled = false;
            }

            CancelPendingFilterRefresh();
        }
    }
}

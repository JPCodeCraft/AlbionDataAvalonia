using AlbionDataAvalonia.Logging;
using AlbionDataAvalonia.Settings;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Linq;

namespace AlbionDataAvalonia.ViewModels
{
    public partial class LogsViewModel : ViewModelBase
    {
        private readonly ListSink _listSink;
        private readonly SettingsManager _settingsManager;

        public ObservableCollection<LogEventWrapper> Events { get; set; } = new();

        public LogsViewModel()
        {

        }
        public LogsViewModel(ListSink listSink, SettingsManager settingsManager)
        {
            _listSink = listSink;
            _settingsManager = settingsManager;

            foreach (var logEvent in _listSink.Events)
            {
                Events.Add(logEvent);
            }
            Events.Reverse();

            _listSink.CollectionChanged += (e) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Events.Insert(0, e);
                    while (Events.Count > _settingsManager.UserSettings.MaxLogCount)
                    {
                        Events.RemoveAt(Events.Count - 1);
                    }
                });
            };
        }
    }
}

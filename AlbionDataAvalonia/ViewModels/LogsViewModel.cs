using AlbionDataAvalonia.Logging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.ViewModels
{
    public partial class LogsViewModel : ViewModelBase
    {
        private readonly ListSink _listSink;

        [ObservableProperty]
        private List<LogEventWrapper> events;
        public LogsViewModel()
        {

        }
        public LogsViewModel(ListSink listSink)
        {
            _listSink = listSink;
            events = _listSink.Events.ToList();
            events.Reverse();

            _listSink.CollectionChanged += () =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Events = _listSink.Events.ToList();
                    Events.Reverse();
                });
            };
        }
    }
}

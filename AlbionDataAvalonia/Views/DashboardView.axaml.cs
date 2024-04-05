using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views
{
    public partial class DashboardView : UserControl
    {
        private Ellipse? _sendIndicator;
        private Ellipse? _receiveIndicator;
        private int oldCount = 0;
        public DashboardView()
        {
            InitializeComponent();
            _sendIndicator = this.FindControl<Ellipse>("SendIndicator");
            _receiveIndicator = this.FindControl<Ellipse>("ReceiveIndicator");

        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is MainViewModel vm && _sendIndicator is not null)
            {
                oldCount = vm.UploadQueueSize;
                vm.PropertyChanged += async (sender, args) =>
                {
                    if (args.PropertyName == nameof(vm.UploadQueueSize))
                    {
                        string className = vm.UploadQueueSize > oldCount ? "Red" : "Green";
                        Ellipse? control = vm.UploadQueueSize > oldCount ? _receiveIndicator : _sendIndicator;
                        oldCount = vm.UploadQueueSize;

                        await Dispatcher.UIThread.InvokeAsync(() => control?.Classes.Add(className));

                        await Task.Delay(TimeSpan.FromSeconds(0.2));
                        await Dispatcher.UIThread.InvokeAsync(() => control?.Classes.Remove(className));
                    }
                };
            }
        }
    }
}

using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView(LogsViewModel logsViewModel)
        {
            InitializeComponent();
            this.DataContext = logsViewModel;
        }
    }
}

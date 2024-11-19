using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views
{
    public partial class TradesView : UserControl
    {
        public TradesView(TradesViewModel tradesViewModel)
        {
            InitializeComponent();
            this.DataContext = tradesViewModel;
        }
    }
}

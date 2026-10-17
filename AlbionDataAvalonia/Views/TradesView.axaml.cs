using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using System.Linq;

namespace AlbionDataAvalonia.Views
{
    public partial class TradesView : UserControl
    {
        public TradesView()
        {
            InitializeComponent();
        }

        // The grid's selection isn't bindable, so the view model hears about it here.
        private void TradesGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not TradesViewModel vm) return;
            if (sender is not DataGrid grid) return;

            vm.UpdateSelectedTrades(grid.SelectedItems.OfType<TradeRowViewModel>());
        }
    }
}

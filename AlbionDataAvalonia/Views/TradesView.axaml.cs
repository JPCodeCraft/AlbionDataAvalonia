using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;
using System.Collections.Specialized;
using System.Linq;

namespace AlbionDataAvalonia.Views
{
    public partial class TradesView : UserControl
    {
        private TradesViewModel? watchedViewModel;

        public TradesView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Watch(DataContext as TradesViewModel);
            // The main window drops a page it leaves; the trades list would otherwise keep this view alive.
            DetachedFromLogicalTree += (_, _) => Watch(null);
            AttachedToLogicalTree += (_, _) => Watch(DataContext as TradesViewModel);
        }

        private void Watch(TradesViewModel? viewModel)
        {
            if (watchedViewModel is not null) watchedViewModel.Trades.CollectionChanged -= Trades_CollectionChanged;
            watchedViewModel = viewModel;
            if (watchedViewModel is null) return;
            watchedViewModel.Trades.CollectionChanged += Trades_CollectionChanged;
            // A page built again starts with nothing selected; the view model's selection follows the grid's.
            watchedViewModel.UpdateSelectedTrades(TradesGrid.SelectedItems.OfType<TradeRowViewModel>());
        }

        // A load refills the same list, and the grid would keep its current row selected and its scroll position.
        // Once the grid has taken the new rows, start them unselected at the top, as a new list did.
        private void Trades_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Reset) return;
            Dispatcher.UIThread.Post(() =>
            {
                TradesGrid.SelectedItems.Clear();
                if (TradesGrid.CollectionView?.Cast<object>().FirstOrDefault() is { } first) TradesGrid.ScrollIntoView(first, null);
            });
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

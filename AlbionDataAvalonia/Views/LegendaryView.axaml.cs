using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using System.Linq;

namespace AlbionDataAvalonia.Views;

public partial class LegendaryView : UserControl
{
    public LegendaryView()
    {
        InitializeComponent();
    }

    private void ItemsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is LegendaryViewModel viewModel)
        {
            viewModel.UpdateSelectedItems(LegendaryItemsGrid.SelectedItems.OfType<LegendaryItemRowViewModel>());
        }
    }
}

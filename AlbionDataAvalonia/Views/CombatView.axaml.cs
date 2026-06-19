using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AlbionDataAvalonia.Views;

public partial class CombatView : UserControl
{
    public CombatView()
    {
        InitializeComponent();
    }

    public CombatView(CombatViewModel combatViewModel)
    {
        InitializeComponent();
        DataContext = combatViewModel;
    }

    private void EncountersGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not CombatViewModel viewModel || sender is not DataGrid grid)
        {
            return;
        }

        if (grid.SelectedItem is CombatEncounterListItemViewModel encounter)
        {
            viewModel.SelectedEncounter = encounter;
        }
    }

    private void PlayersGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not CombatViewModel viewModel || sender is not DataGrid grid)
        {
            return;
        }

        if (grid.SelectedItem is CombatPlayerRowViewModel player)
        {
            viewModel.SelectedPlayer = player;
        }
    }

    private void ClearSelectedEncounterButton_Click(object? sender, RoutedEventArgs e)
    {
        EncountersGrid.SelectedItem = null;
    }

    private void ClearSelectedPlayerButton_Click(object? sender, RoutedEventArgs e)
    {
        PlayersGrid.SelectedItem = null;
    }
}

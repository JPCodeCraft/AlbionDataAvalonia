using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;

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
}

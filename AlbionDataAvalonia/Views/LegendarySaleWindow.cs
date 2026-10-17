using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views;

public partial class LegendarySaleWindow : Window
{
    public LegendarySaleWindow()
    {
        InitializeComponent();
    }

    public LegendarySaleWindow(LegendarySaleViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
        // A listing being posted cannot be abandoned half way.
        Closing += (_, e) => e.Cancel = viewModel.IsPosting;
    }
}

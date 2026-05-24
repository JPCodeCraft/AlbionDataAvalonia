using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views;

public partial class GatheringView : UserControl
{
    public GatheringView()
    {
        InitializeComponent();
    }

    public GatheringView(GatheringViewModel gatheringViewModel)
    {
        InitializeComponent();
        DataContext = gatheringViewModel;
    }
}


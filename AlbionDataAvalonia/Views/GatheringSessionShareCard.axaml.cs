using AlbionDataAvalonia.Gathering;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views;

public partial class GatheringSessionShareCard : UserControl
{
    public GatheringSessionShareCard()
    {
        InitializeComponent();
        Width = GatheringSessionShareImageService.CardLogicalWidth;
        Height = GatheringSessionShareImageService.CardLogicalHeight;
    }
}

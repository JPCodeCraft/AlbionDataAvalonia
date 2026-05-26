using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public partial class ConfirmDiscardGatheringSessionWindow : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDiscardGatheringSessionWindow()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner)
    {
        var window = new ConfirmDiscardGatheringSessionWindow();
        await window.ShowDialog(owner);
        return window.Confirmed;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DiscardButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}

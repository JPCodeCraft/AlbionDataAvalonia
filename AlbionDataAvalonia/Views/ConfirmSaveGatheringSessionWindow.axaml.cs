using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public partial class ConfirmSaveGatheringSessionWindow : Window
{
    private bool confirmed;

    public ConfirmSaveGatheringSessionWindow()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner)
    {
        var window = new ConfirmSaveGatheringSessionWindow();
        await window.ShowDialog(owner);
        return window.confirmed;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        confirmed = true;
        Close();
    }
}

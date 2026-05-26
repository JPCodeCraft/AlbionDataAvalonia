using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public partial class ConfirmDeleteGatheringSessionWindow : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDeleteGatheringSessionWindow()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner)
    {
        var window = new ConfirmDeleteGatheringSessionWindow();
        await window.ShowDialog(owner);
        return window.Confirmed;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}

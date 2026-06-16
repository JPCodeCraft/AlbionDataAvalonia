using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public partial class ConfirmClearLootWindow : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmClearLootWindow()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner)
    {
        var window = new ConfirmClearLootWindow();
        await window.ShowDialog(owner);
        return window.Confirmed;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}

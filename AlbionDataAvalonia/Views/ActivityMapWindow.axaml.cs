using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AlbionDataAvalonia.Views;

public partial class ActivityMapWindow : Window
{
    public ActivityMapWindow()
    {
        InitializeComponent();
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => MapCanvas.ZoomBy(1.4);

    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => MapCanvas.ZoomBy(1 / 1.4);

    private void Fit_Click(object? sender, RoutedEventArgs e) => MapCanvas.Fit();
}

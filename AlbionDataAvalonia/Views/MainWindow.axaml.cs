using Avalonia.Controls;
using System.ComponentModel;

namespace AlbionDataAvalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        IsVisible = false;
    }
}

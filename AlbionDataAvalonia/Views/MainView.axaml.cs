using Avalonia.Controls;
using AlbionDataAvalonia.ViewModels;
using Avalonia.Interactivity;
using System;

namespace AlbionDataAvalonia.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private async void CopyLoginLink_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || string.IsNullOrWhiteSpace(viewModel.LoginAuthorizationUrl))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            viewModel.UpdateLoginStatus("Clipboard is not available. Use Open Browser Again instead.");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(viewModel.LoginAuthorizationUrl);
            viewModel.UpdateLoginStatus("Sign-in link copied. Open it in a browser on this computer.");
        }
        catch (Exception)
        {
            viewModel.UpdateLoginStatus("Could not copy the sign-in link. Use Open Browser Again instead.");
        }
    }
}

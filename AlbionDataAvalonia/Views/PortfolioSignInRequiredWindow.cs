using AlbionDataAvalonia.Ui;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed class PortfolioSignInRequiredWindow : Window
{
    private PortfolioSignInRequiredWindow(string targetDescription)
    {
        Classes.Add("AppPage");
        Title = "Sign in required";
        Icon = UiAssets.WindowIcon;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Sign in required",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Sign in to AFM before adding {targetDescription} to Portfolio.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.85
        });

        var okButton = new Button
        {
            Content = "OK",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Classes = { "UiAction", "UiPrimary" }
        };
        okButton.Click += OkButton_Click;

        panel.Children.Add(okButton);
        Content = panel;
    }

    public static async Task ShowAsync(Window owner, string targetDescription = "trades")
    {
        var window = new PortfolioSignInRequiredWindow(targetDescription);
        await window.ShowDialog(owner);
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

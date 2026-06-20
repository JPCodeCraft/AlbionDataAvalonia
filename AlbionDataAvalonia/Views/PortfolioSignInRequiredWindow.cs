using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed class PortfolioSignInRequiredWindow : Window
{
    private PortfolioSignInRequiredWindow()
    {
        Title = "Sign in required";
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
            FontWeight = Avalonia.Media.FontWeight.DemiBold
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Sign in to AFM before adding trades to Portfolio.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.85
        });

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        okButton.Click += OkButton_Click;

        panel.Children.Add(okButton);
        Content = panel;
    }

    public static async Task ShowAsync(Window owner)
    {
        var window = new PortfolioSignInRequiredWindow();
        await window.ShowDialog(owner);
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

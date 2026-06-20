using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed class ConfirmPortfolioReuploadWindow : Window
{
    public bool Confirmed { get; private set; }

    private ConfirmPortfolioReuploadWindow(int uploadedCount, int selectedCount)
    {
        Title = "Upload trades again?";
        Width = 440;
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
            Text = "Upload trades again?",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.DemiBold
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"{uploadedCount:N0} of {selectedCount:N0} selected trades are already marked as uploaded to Portfolio. If you continue, they will be added again as new portfolio transactions.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.85
        });

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += CancelButton_Click;
        buttons.Children.Add(cancelButton);

        var uploadButton = new Button { Content = "Upload Again" };
        uploadButton.Click += UploadButton_Click;
        buttons.Children.Add(uploadButton);

        panel.Children.Add(buttons);
        Content = panel;
    }

    public static async Task<bool> ShowAsync(Window owner, int uploadedCount, int selectedCount)
    {
        var window = new ConfirmPortfolioReuploadWindow(uploadedCount, selectedCount);
        await window.ShowDialog(owner);
        return window.Confirmed;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UploadButton_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}

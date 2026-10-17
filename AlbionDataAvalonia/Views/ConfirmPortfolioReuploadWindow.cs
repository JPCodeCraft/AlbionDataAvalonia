using AlbionDataAvalonia.Ui;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed class ConfirmPortfolioReuploadWindow : Window
{
    public bool Confirmed { get; private set; }

    private ConfirmPortfolioReuploadWindow(int uploadedCount, int selectedCount, string recordName)
    {
        var prompt = $"Upload {UiFormat.Noun(uploadedCount, recordName)} again?";
        var one = uploadedCount == 1;

        Classes.Add("AppPage");
        Title = prompt;
        Icon = UiAssets.WindowIcon;
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
            Text = prompt,
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"{uploadedCount:N0} of {UiFormat.Count(selectedCount, $"selected {recordName}")} {(one ? "is" : "are")} already marked as uploaded to Portfolio. If you continue, {(one ? "it" : "they")} will be added again as new portfolio {UiFormat.Noun(uploadedCount, "transaction")}.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.85
        });

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelButton = new Button { Content = "Cancel", IsCancel = true, Classes = { "UiAction" } };
        cancelButton.Click += CancelButton_Click;
        buttons.Children.Add(cancelButton);

        var uploadButton = new Button { Content = "Upload again", Classes = { "UiAction", "UiPrimary" } };
        uploadButton.Click += UploadButton_Click;
        buttons.Children.Add(uploadButton);

        panel.Children.Add(buttons);
        Content = panel;
    }

    public static async Task<bool> ShowAsync(
        Window owner,
        int uploadedCount,
        int selectedCount,
        string recordName = "trade")
    {
        var window = new ConfirmPortfolioReuploadWindow(uploadedCount, selectedCount, recordName);
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

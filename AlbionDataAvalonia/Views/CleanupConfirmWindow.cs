using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed class CleanupConfirmWindow : Window
{
    private bool _confirmed;

    private CleanupConfirmWindow(string itemNamePlural, int count)
    {
        Title = $"Delete {itemNamePlural}?";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14
        };

        root.Children.Add(new TextBlock
        {
            Text = $"Delete {count:N0} {itemNamePlural}?",
            FontSize = 18,
            FontWeight = FontWeight.DemiBold
        });

        root.Children.Add(new TextBlock
        {
            Text = $"This will delete {count:N0} {itemNamePlural} and cannot be recovered from the app.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += CancelButton_Click;
        buttons.Children.Add(cancelButton);

        var deleteButton = new Button { Content = "Delete" };
        deleteButton.Click += DeleteButton_Click;
        buttons.Children.Add(deleteButton);

        root.Children.Add(buttons);
        Content = root;
    }

    public static async Task<bool> ShowAsync(Window owner, string itemNamePlural, int count)
    {
        var window = new CleanupConfirmWindow(itemNamePlural, count);
        await window.ShowDialog(owner);
        return window._confirmed;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }
}

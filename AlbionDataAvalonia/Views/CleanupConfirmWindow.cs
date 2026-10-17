using AlbionDataAvalonia.Ui;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

/// <summary>Asks before a destructive action; used by deletes and cleanups across the app.</summary>
public sealed class CleanupConfirmWindow : Window
{
    private bool _confirmed;

    private CleanupConfirmWindow(string title, string heading, string message, string action)
    {
        Classes.Add("AppPage");
        Title = title;
        Icon = UiAssets.WindowIcon;
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
            Text = heading,
            FontSize = 18,
            FontWeight = FontWeight.DemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelButton = new Button { Content = "Cancel", IsCancel = true, Classes = { "UiAction", "UiSecondary" } };
        cancelButton.Click += CancelButton_Click;
        buttons.Children.Add(cancelButton);

        var deleteButton = new Button { Content = action, Classes = { "UiAction" } };
        deleteButton.Click += DeleteButton_Click;
        buttons.Children.Add(deleteButton);

        root.Children.Add(buttons);
        Content = root;
    }

    public static Task<bool> ShowAsync(Window owner, string singular, string plural, int count) => ShowAsync(
        owner,
        $"Delete {UiFormat.Noun(count, singular, plural)}?",
        $"Delete {UiFormat.Count(count, singular, plural)}?",
        $"This will delete {UiFormat.Count(count, singular, plural)} and cannot be recovered from the app.",
        "Delete");

    public static Task<bool> ShowAsync(Window owner, string title, string message, string action) =>
        ShowAsync(owner, title, title, message, action);

    private static async Task<bool> ShowAsync(Window owner, string title, string heading, string message, string action)
    {
        var window = new CleanupConfirmWindow(title, heading, message, action);
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

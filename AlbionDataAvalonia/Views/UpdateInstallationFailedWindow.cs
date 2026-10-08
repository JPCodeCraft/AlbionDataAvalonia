using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed class UpdateInstallationFailedWindow : Window
{
    private UpdateInstallationFailedWindow(string version)
    {
        Title = "Restart required";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Update to version {version} failed",
            FontSize = 18,
            FontWeight = FontWeight.DemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Recording has stopped while preparing the update. The app will close. Reopen AFM to resume recording and try the update again.",
            TextWrapping = TextWrapping.Wrap
        });
        var closeButton = new Button { Content = "Close app", HorizontalAlignment = HorizontalAlignment.Right };
        closeButton.Click += (_, _) => Close();
        panel.Children.Add(closeButton);
        Content = panel;
    }

    public static async Task ShowAsync(Window? owner, string version)
    {
        var window = new UpdateInstallationFailedWindow(version);
        if (owner is { IsVisible: true })
        {
            window.ShowInTaskbar = false;
            await window.ShowDialog(owner);
            return;
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Closed += (_, _) => completion.TrySetResult(null);
        window.Show();
        await completion.Task;
    }
}

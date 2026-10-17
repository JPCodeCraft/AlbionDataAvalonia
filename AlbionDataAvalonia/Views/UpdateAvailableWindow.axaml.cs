using Avalonia.Controls;
using Avalonia.Interactivity;
using Serilog;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public partial class UpdateAvailableWindow : Window
{
    private readonly string? releasePageUrl;

    public UpdateAvailableWindow()
    {
        InitializeComponent();
    }

    public UpdateAvailableWindow(ClientUpdateInfo update) : this()
    {
        releasePageUrl = update.ReleasePageUrl;
        TitleText.Text = $"{update.ChannelLabel} update available";
        MessageText.Text = $"Version {update.Version} is available. Automatic updates are not supported on this platform, so download and install the new version manually.";
        OpenReleaseButton.IsEnabled = !string.IsNullOrWhiteSpace(releasePageUrl);
    }

    public static async Task ShowAsync(Window? owner, ClientUpdateInfo update)
    {
        var window = new UpdateAvailableWindow(update);

        if (owner is { IsVisible: true })
        {
            await window.ShowDialog(owner);
            return;
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Closed += (_, _) => completion.TrySetResult(null);
        window.Show();
        await completion.Task;
    }

    private void OpenReleaseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(releasePageUrl)
            && Uri.TryCreate(releasePageUrl, UriKind.Absolute, out var uri))
        {
            OpenUrl(uri);
        }

        Close();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void OpenUrl(Uri uri) => SystemLauncher.TryOpenUrl(uri);
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Serilog;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

        var completion = new TaskCompletionSource<object?>();
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

    private static void OpenUrl(Uri uri)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.ToString(),
                    UseShellExecute = true
                });
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("x-www-browser", uri.ToString());
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", uri.ToString());
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open update URL {Url}", uri);
        }
    }
}

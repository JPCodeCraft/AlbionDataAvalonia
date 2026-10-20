using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

// The upload line under the setup checklist and Copy diagnostics, which reports the checklist with the uploads.
public partial class MainViewModel
{
    public SetupStatusViewModel Setup { get; } = new();

    [ObservableProperty] private string uploadStatusText = "Uploads start once you’re detected";
    [ObservableProperty] private bool isUploading;
    [ObservableProperty] private string copyDiagnosticsLabel = "Copy diagnostics";
    [ObservableProperty] private bool isDiagnosticsEnabled;

    [RelayCommand]
    private void OpenDiscord() => SystemLauncher.TryOpenUrl(SystemLauncher.AfmDiscordUrl);

    private void UpdateUploadStatus()
    {
        IsUploading = UploadQueueSize > 0 || RunningTasksCount > 0;
        UploadStatusText = IsUploading
            ? $"Uploading {UiFormat.Whole(RunningTasksCount)} · {UiFormat.Whole(UploadQueueSize)} waiting"
            : Setup.IsReady ? "Uploads idle" : "Uploads start once you’re detected";
    }

    [RelayCommand]
    private async Task CopyDiagnostics()
    {
        var copied = false;
        if (_clipboard is not null && _diagnosticsReport is not null)
        {
            CopyDiagnosticsLabel = "Copying…";
            try
            {
                copied = await _clipboard.SetTextAsync(await _diagnosticsReport.BuildAsync(Setup));
            }
            catch (Exception error)
            {
                Log.Error(error, "Could not build the diagnostics report.");
            }
        }

        CopyDiagnosticsLabel = copied ? "Copied" : "Couldn’t copy";
        await Task.Delay(TimeSpan.FromSeconds(2));
        CopyDiagnosticsLabel = "Copy diagnostics";
    }
}

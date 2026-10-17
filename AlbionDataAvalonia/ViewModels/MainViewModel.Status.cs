using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Runtime.InteropServices;
using System.Text;
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

    public string BuildDiagnosticsText()
    {
        var text = new StringBuilder();
        text.AppendLine($"AFM Data Client {AppVersion} ({RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture})");
        Setup.AppendDiagnostics(text);
        text.AppendLine($"Signed in: {(UserLoggedIn ? "yes" : "no")} · Private mode: {(UploadToAfmOnly ? "on" : "off")}");
        text.AppendLine($"Uploads since start: public {PublicUploadSuccessCount:N0} ok, {PublicUploadFailedCount:N0} failed · private {PrivateUploadSuccessCount:N0} ok, {PrivateUploadFailedCount:N0} failed");
        text.AppendLine($"Upload queue: {UploadQueueSize:N0} waiting, {RunningTasksCount:N0} running");
        text.Append($"Diagnostics mode: {(IsDiagnosticsEnabled ? "on" : "off")}");
        return text.ToString();
    }

    [RelayCommand]
    private async Task CopyDiagnostics()
    {
        var copied = _clipboard is not null && await _clipboard.SetTextAsync(BuildDiagnosticsText());
        CopyDiagnosticsLabel = copied ? "Copied" : "Couldn’t copy";
        await Task.Delay(TimeSpan.FromSeconds(2));
        CopyDiagnosticsLabel = "Copy diagnostics";
    }
}

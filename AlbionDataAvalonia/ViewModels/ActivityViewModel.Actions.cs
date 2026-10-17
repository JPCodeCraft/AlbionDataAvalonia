using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Ui.Services;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

// Actions that ask before they run: starting or deleting sessions and choosing where loot exports go.
public partial class ActivityViewModel
{
    private readonly IDialogService? dialogs;
    private readonly IFileDialogService? files;
    private bool choosingLootExport;

    [RelayCommand]
    private async Task NewSession()
    {
        if (dialogs is null || !HasCurrentPlaySession) return;
        var id = CurrentPlaySessionId;
        if (await dialogs.ConfirmAsync("Start a new session?", "This session ends at your last activity and stays in History. Your next activity starts the new session.", "New session", destructive: false)
            && CurrentPlaySessionId == id)
            StartNewSession(id);
    }

    [RelayCommand]
    private async Task DeleteSession()
    {
        if (dialogs is null || !CanDeleteSelectedSession) return;
        var id = (SelectedPlaySession?.Id, SelectedSession?.Id);
        var label = SelectedSession is null ? "session" : "run";
        if (await dialogs.ConfirmAsync($"Delete {label}?", $"Delete the {DeleteTargetText} and all activity in it? This cannot be undone.", $"Delete {label}")
            && (SelectedPlaySession?.Id, SelectedSession?.Id) == id)
            await DeleteSelectedSessionAsync();
    }

    [RelayCommand]
    private void OpenRunRow(ActivitySessionRow? run)
    {
        if (run is not null) OpenRun(run.Id);
    }

    [RelayCommand] private Task ExportCurrentLoot(LootExportFormat format) => ExportLootToFileAsync(CaptureCurrentLootExportQuery(), format);
    [RelayCommand] private Task ExportSessionLoot(LootExportFormat format) => ExportLootToFileAsync(CaptureSessionLootExportQuery(), format);
    [RelayCommand] private Task ExportGlobalLoot(LootExportFormat format) => ExportLootToFileAsync(CaptureGlobalLootExportQuery(), format);
    [RelayCommand] private void OpenLootLogger() => SystemLauncher.TryOpenOptionalUrl(SystemLauncher.AfmLootLoggerUrl);

    // The query is captured when the menu item is chosen, so the file holds what the list showed then.
    private async Task ExportLootToFileAsync(ActivityQuery? query, LootExportFormat format)
    {
        if (choosingLootExport || files is null || query is null) return;
        choosingLootExport = true;
        try
        {
            IStorageFile? file;
            CsvExportOptions? options = null;
            if (format == LootExportFormat.LootLoggerViewer)
            {
                file = await files.SaveAsync("Export loot for Loot Logger Viewer", $"loot-events-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt", "Loot Logger files", "*.txt");
            }
            else
            {
                var target = await files.PickCsvExportAsync("Export loot for AFM Loot Logger", $"afm-loot-log-{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                (file, options) = (target?.File, target?.Options);
            }
            if (file is null) return;
            if (!CanStartLootExport)
            {
                LootExportStatusText = "Export canceled.";
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            await ExportLootAsync(stream, query, format, options);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not open the loot export file.");
            LootExportStatusText = "Export failed. The file may be incomplete.";
        }
        finally
        {
            choosingLootExport = false;
        }
    }
}

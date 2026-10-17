using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Ui.Services;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public enum LootExportFormat { AfmCsv, LootLoggerViewer }

/// <summary>
/// Loot exports for the Activity tabs. One export runs at a time, so the Current, History and Items loot
/// lists share its progress and status; each list passes the query it shows.
/// </summary>
public partial class ActivityLootExport : ObservableObject, IDisposable
{
    private readonly ActivityPersistence? persistence;
    private readonly CsvExportService? csvExportService;
    private readonly IFileDialogService? files;
    private CancellationTokenSource? cancellation;
    private long version;
    private bool choosingFile;
    private bool isDisposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool isExporting;
    [ObservableProperty] private bool isPreparing;
    [ObservableProperty] private int percent;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string statusText = "";

    public ActivityLootExport(ActivityPersistence? persistence, CsvExportService? csvExportService, IFileDialogService? files)
    {
        this.persistence = persistence;
        this.csvExportService = csvExportService;
        this.files = files;
    }

    public bool CanStart => !isDisposed && !IsExporting && persistence is not null && csvExportService is not null;
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    // The query is captured when the menu item is chosen, so the file holds what the list showed then.
    public async Task ExportToFileAsync(ActivityQuery? query, LootExportFormat format)
    {
        if (choosingFile || files is null || query is null) return;
        choosingFile = true;
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
            if (!CanStart)
            {
                StatusText = "Export canceled.";
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            await ExportAsync(stream, query, format, options);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not open the loot export file.");
            StatusText = "Export failed. The file may be incomplete.";
        }
        finally
        {
            choosingFile = false;
        }
    }

    // Each export uses the exact query of the loot list it is opened from, across all pages.
    public async Task ExportAsync(Stream stream, ActivityQuery query, LootExportFormat format,
        CsvExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (IsExporting || persistence is null || csvExportService is null)
            throw new InvalidOperationException("Loot export is unavailable or already in progress.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation = linked;
        var exportVersion = ++version;
        IsExporting = true;
        IsPreparing = true;
        Percent = 0;
        StatusText = "Preparing export…";
        try
        {
            var records = await persistence.GetLootExportSnapshotAsync(query, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            var omitted = format == LootExportFormat.LootLoggerViewer ? records.Count(x => x.Quantity <= 0) : 0;
            IsPreparing = false;
            StatusText = "Exporting loot…";
            var progress = new Progress<int>(value =>
            {
                if (!isDisposed && IsExporting && exportVersion == version) Percent = value;
            });
            await Task.Run(() => format switch
            {
                LootExportFormat.AfmCsv => csvExportService.ExportLootToCsvAsync(stream, records, options, progress, linked.Token),
                LootExportFormat.LootLoggerViewer => csvExportService.ExportLootToViewerAsync(stream, records, progress, linked.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            }, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            Percent = 100;
            StatusText = $"Exported {records.Count - omitted:N0} pickups."
                + (omitted > 0 ? $" {omitted:N0} pickups with unknown quantities were skipped." : "");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Export canceled. The file may be incomplete.";
        }
        catch (Exception error)
        {
            Log.Error(error, "Failed to export loot.");
            StatusText = "Export failed. The file may be incomplete.";
        }
        finally
        {
            cancellation = null;
            IsPreparing = false;
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void Cancel() => cancellation?.Cancel();

    [RelayCommand] private void OpenLootLogger() => SystemLauncher.TryOpenOptionalUrl(SystemLauncher.AfmLootLoggerUrl);

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        cancellation?.Cancel();
        OnPropertyChanged(nameof(CanStart));
    }
}

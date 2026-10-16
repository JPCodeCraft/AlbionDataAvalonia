using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Network.Services;
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

public partial class ActivityViewModel
{
    private readonly CsvExportService? csvExportService;
    private CancellationTokenSource? lootExportCancellation;
    private long lootExportVersion;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportSessionLoot))]
    [NotifyPropertyChangedFor(nameof(CanStartLootExport))]
    private bool isLootExporting;
    [ObservableProperty] private bool isPreparingLootExport;
    [ObservableProperty] private int lootExportProgress;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLootExportStatus))]
    private string lootExportStatusText = "";

    public bool CanStartLootExport => !isDisposed && !IsLootExporting && persistence is not null && csvExportService is not null;
    public bool CanExportSessionLoot => CanStartLootExport
        && SelectedPlaySession is not null && !IsImportedDetail && (SelectedSessionItemSource.Value & ActivityItemSources.Loot) != 0;
    public bool HasLootExportStatus => !string.IsNullOrEmpty(LootExportStatusText);

    // Each export uses the exact query of the loot list it is opened from, across all pages.
    public ActivityQuery? CaptureSessionLootExportQuery() => CanExportSessionLoot ? DetailQuery(true) with { LootOnly = true } : null;
    public ActivityQuery? CaptureCurrentLootExportQuery() => CanStartLootExport && HasCurrentLoot ? currentLootQuery : null;
    public ActivityQuery? CaptureGlobalLootExportQuery() => CanStartLootExport && ShowGlobalLootPanel ? displayedGlobalLootQuery : null;

    public async Task ExportLootAsync(Stream stream, ActivityQuery query, LootExportFormat format,
        CsvExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (IsLootExporting || persistence is null || csvExportService is null)
            throw new InvalidOperationException("Loot export is unavailable or already in progress.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lootExportCancellation = cancellation;
        var version = ++lootExportVersion;
        IsLootExporting = true;
        IsPreparingLootExport = true;
        LootExportProgress = 0;
        LootExportStatusText = "Preparing export…";
        try
        {
            var records = await persistence.GetLootExportSnapshotAsync(query, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var omitted = format == LootExportFormat.LootLoggerViewer ? records.Count(x => x.Quantity <= 0) : 0;
            IsPreparingLootExport = false;
            LootExportStatusText = "Exporting loot…";
            var progress = new Progress<int>(value =>
            {
                if (!isDisposed && IsLootExporting && version == lootExportVersion) LootExportProgress = value;
            });
            await Task.Run(() => format switch
            {
                LootExportFormat.AfmCsv => csvExportService.ExportLootToCsvAsync(stream, records, options, progress, cancellation.Token),
                LootExportFormat.LootLoggerViewer => csvExportService.ExportLootToViewerAsync(stream, records, progress, cancellation.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            LootExportProgress = 100;
            LootExportStatusText = $"Exported {records.Count - omitted:N0} pickups."
                + (omitted > 0 ? $" {omitted:N0} pickups with unknown quantities were skipped." : "");
        }
        catch (OperationCanceledException)
        {
            LootExportStatusText = "Export canceled. The file may be incomplete.";
        }
        catch (Exception error)
        {
            Log.Error(error, "Failed to export loot.");
            LootExportStatusText = "Export failed. The file may be incomplete.";
        }
        finally
        {
            lootExportCancellation = null;
            IsPreparingLootExport = false;
            IsLootExporting = false;
        }
    }

    [RelayCommand]
    private void CancelLootExport() => lootExportCancellation?.Cancel();
}

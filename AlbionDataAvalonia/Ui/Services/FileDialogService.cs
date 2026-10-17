using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Views.Dialogs;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Ui.Services;

public sealed record CsvExportTarget(CsvExportOptions Options, IStorageFile File);

public interface IFileDialogService
{
    /// <summary>Asks where to save a file of one type; null when the user cancels.</summary>
    Task<IStorageFile?> SaveAsync(string title, string suggestedName, string fileTypeName, string pattern);

    /// <summary>Asks for the CSV separator and decimal symbol, then where to save the file.</summary>
    Task<CsvExportTarget?> PickCsvExportAsync(string title, string suggestedName);
}

public sealed class FileDialogService(IDialogService dialogs) : IFileDialogService
{
    public async Task<IStorageFile?> SaveAsync(string title, string suggestedName, string fileTypeName, string pattern)
    {
        if (UiWindows.Active is not { } window) return null;
        return await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = [pattern] }]
        });
    }

    public async Task<CsvExportTarget?> PickCsvExportAsync(string title, string suggestedName)
    {
        if (await dialogs.ShowAsync(new CsvExportDialog()) is not { } options) return null;
        return await SaveAsync(title, suggestedName, "CSV files", "*.csv") is { } file ? new(options, file) : null;
    }
}

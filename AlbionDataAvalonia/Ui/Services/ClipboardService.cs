using Serilog;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Ui.Services;

public interface IClipboardService
{
    /// <summary>Copies text and reports whether it worked; failures are logged, not thrown.</summary>
    Task<bool> SetTextAsync(string text);
}

public sealed class ClipboardService : IClipboardService
{
    public async Task<bool> SetTextAsync(string text)
    {
        if (UiWindows.Active?.Clipboard is not { } clipboard) return false;
        try
        {
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception error)
        {
            Log.Warning(error, "Could not copy to the clipboard.");
            return false;
        }
    }
}

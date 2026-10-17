using AlbionDataAvalonia.Views.Dialogs;
using Avalonia.Controls;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Ui.Services;

/// <summary>Opens dialogs over the active window, so view models can ask questions without knowing about windows.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string action, bool destructive = true, string? heading = null, string cancel = "Cancel");

    /// <summary>"Delete 3 trades?" for a count of items, with a danger button.</summary>
    Task<bool> ConfirmDeleteAsync(string singular, string plural, int count);

    Task AlertAsync(string title, string message, string button = "OK", string? heading = null);

    Task<TResult?> ShowAsync<TResult>(AppDialog<TResult> dialog);

    /// <summary>Shows a window that is not an <see cref="AppDialog{TResult}"/> as a dialog over the active window.</summary>
    Task ShowAsync(Window window);
}

public sealed class DialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string action, bool destructive = true, string? heading = null, string cancel = "Cancel") =>
        ShowAsync(new ConfirmDialog(title, heading ?? title, message, action, destructive, cancel));

    public Task<bool> ConfirmDeleteAsync(string singular, string plural, int count) => ConfirmAsync(
        $"Delete {UiFormat.Noun(count, singular, plural)}?",
        $"This will delete {UiFormat.Count(count, singular, plural)} and cannot be recovered from the app.",
        "Delete",
        heading: $"Delete {UiFormat.Count(count, singular, plural)}?");

    public Task AlertAsync(string title, string message, string button = "OK", string? heading = null) =>
        ShowAsync(new AlertDialog(title, heading ?? title, message, button));

    public Task<TResult?> ShowAsync<TResult>(AppDialog<TResult> dialog) => dialog.ShowAsync(UiWindows.Active);

    public async Task ShowAsync(Window window)
    {
        if (UiWindows.Active is { IsVisible: true } owner)
        {
            await window.ShowDialog(owner);
            return;
        }

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        await closed.Task;
    }
}

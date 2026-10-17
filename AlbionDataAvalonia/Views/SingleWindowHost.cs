using AlbionDataAvalonia.Settings;
using Avalonia.Controls;
using Avalonia.Threading;
using System;

namespace AlbionDataAvalonia.Views;

/// <summary>Where a secondary window keeps its open state and last placement.</summary>
public sealed record WindowMemory(Func<bool> IsOpen, Action<bool> SetOpen, Func<WindowPlacement?> Placement, Action<WindowPlacement> SetPlacement);

/// <summary>
/// Owns one secondary window. It reopens the window where it was, remembers whether it was open so it can come back
/// when the client starts, and disposes the window's view model when it closes. The view model reads data only
/// while the window is open.
/// </summary>
public sealed class SingleWindowHost<TWindow, TViewModel>(
    Func<TViewModel> createViewModel,
    Action<TViewModel> start,
    WindowMemory memory,
    bool restoreSize)
    where TWindow : Window, new()
    where TViewModel : class, IDisposable
{
    private TWindow? window;

    public bool IsOpen => window is not null;
    public TViewModel? ViewModel { get; private set; }
    public event EventHandler? IsOpenChanged;

    public void RestoreIfOpen()
    {
        if (memory.IsOpen()) Show();
    }

    public void Toggle()
    {
        if (window is null) Show();
        else window.Close();
    }

    public void Show()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (window is not null)
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            return;
        }
        if (App.IsShuttingDown) return;

        var model = createViewModel();
        var opened = new TWindow { DataContext = model };
        WindowPlacementTracker.Attach(opened, memory.Placement(), restoreSize, memory.SetPlacement);
        opened.Closed += (_, _) =>
        {
            model.Dispose();
            if (!App.IsShuttingDown) memory.SetOpen(false);
            if (window == opened)
            {
                window = null;
                ViewModel = null;
            }
            IsOpenChanged?.Invoke(this, EventArgs.Empty);
        };
        window = opened;
        ViewModel = model;
        memory.SetOpen(true);
        opened.Show();
        start(model);
        IsOpenChanged?.Invoke(this, EventArgs.Empty);
    }
}

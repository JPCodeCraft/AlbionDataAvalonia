using Avalonia.Threading;
using System;
using System.Threading;

namespace AlbionDataAvalonia.Ui;

/// <summary>Moves work onto the UI thread. Services raise their events on capture and timer threads.</summary>
public static class UiDispatch
{
    /// <summary>Runs the action now on the UI thread, or posts it there from any other thread.</summary>
    public static void Run(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>
    /// For the first line of a handler: off the UI thread it posts <paramref name="again"/> there and returns true,
    /// so the caller returns and runs again on the UI thread.
    /// </summary>
    public static bool Repost(Action again)
    {
        if (Dispatcher.UIThread.CheckAccess()) return false;
        Dispatcher.UIThread.Post(again);
        return true;
    }
}

/// <summary>Runs an action on the UI thread once calls have stopped for the interval; each call restarts the wait.</summary>
public sealed class UiDebouncer(TimeSpan interval, Action action) : IDisposable
{
    private IDisposable? pending;
    private bool disposed;

    public void Trigger() => UiDispatch.Run(() =>
    {
        if (disposed) return;
        pending?.Dispose();
        pending = DispatcherTimer.RunOnce(() =>
        {
            pending = null;
            if (!disposed) action();
        }, interval);
    });

    /// <summary>Drops a waiting run, for when the caller has just done the work itself.</summary>
    public void Cancel() => UiDispatch.Run(() =>
    {
        pending?.Dispose();
        pending = null;
    });

    public void Dispose()
    {
        disposed = true;
        Cancel();
    }
}

/// <summary>
/// Numbers overlapping loads so only the newest applies its result: take a ticket with <see cref="Begin"/> and
/// check <see cref="IsCurrent"/> before using what the load returned.
/// </summary>
public sealed class LatestLoad
{
    private int version;

    public int Begin() => Interlocked.Increment(ref version);

    public bool IsCurrent(int ticket) => ticket == Volatile.Read(ref version);

    /// <summary>Makes every load in progress stale, such as when the data it would show has changed.</summary>
    public void Invalidate() => Interlocked.Increment(ref version);
}

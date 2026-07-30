using Avalonia.Threading;
using System;

namespace AlbionDataAvalonia.ViewModels;

internal sealed class LatestUiValueDispatcher<T> : IDisposable
    where T : class
{
    private readonly object sync = new();
    private readonly Action<T> applyValue;
    private readonly TimeSpan minimumInterval;
    private IDisposable? pendingDrainRegistration;
    private T? pendingValue;
    private bool isScheduled;
    private bool isDisposed;

    public LatestUiValueDispatcher(Action<T> applyValue, TimeSpan minimumInterval = default)
    {
        this.applyValue = applyValue;
        this.minimumInterval = minimumInterval;
    }

    public void Post(T value)
    {
        lock (sync)
        {
            if (isDisposed)
            {
                return;
            }

            pendingValue = value;
            if (isScheduled)
            {
                return;
            }

            isScheduled = true;
        }

        Dispatcher.UIThread.Post(ScheduleDrain);
    }

    private void ScheduleDrain()
    {
        lock (sync)
        {
            if (isDisposed)
            {
                pendingValue = null;
                isScheduled = false;
                return;
            }

            if (minimumInterval > TimeSpan.Zero)
            {
                pendingDrainRegistration = DispatcherTimer.RunOnce(Drain, minimumInterval);
                return;
            }
        }

        Drain();
    }

    private void Drain()
    {
        T? value;
        lock (sync)
        {
            pendingDrainRegistration = null;
            if (isDisposed)
            {
                pendingValue = null;
                isScheduled = false;
                return;
            }

            value = pendingValue;
            pendingValue = null;
            isScheduled = false;
        }

        if (value is not null)
        {
            applyValue(value);
        }
    }

    public void Dispose()
    {
        IDisposable? drainRegistration;
        lock (sync)
        {
            isDisposed = true;
            pendingValue = null;
            isScheduled = false;
            drainRegistration = pendingDrainRegistration;
            pendingDrainRegistration = null;
        }

        drainRegistration?.Dispose();
    }
}

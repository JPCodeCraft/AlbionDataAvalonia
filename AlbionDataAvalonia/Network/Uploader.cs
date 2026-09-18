using System;

namespace AlbionDataAvalonia.Network.Services;

/// <summary>Desktop queue indicators backed by the shared upload worker.</summary>
public sealed class Uploader : IDisposable
{
    private readonly DesktopClientCore client;

    public Uploader(DesktopClientCore client)
    {
        this.client = client;
        client.Core.QueueChanged += OnQueueChanged;
    }

    public int uploadQueueCount => client.Core.QueueCount;
    public int runningTasksCount => client.Core.RunningCount;
    public event Action? OnChange;

    private void OnQueueChanged() => OnChange?.Invoke();
    public void Dispose() => client.Core.QueueChanged -= OnQueueChanged;
}

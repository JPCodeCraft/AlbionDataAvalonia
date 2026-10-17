namespace AlbionDataAvalonia.Network.Models;

/// <summary>What the capture session is doing right now.</summary>
/// <param name="IsStarted">A capture session exists. False while drivers are waited on or capture is blocked.</param>
/// <param name="ListeningDeviceCount">Adapters currently open for capture.</param>
/// <param name="AvailableDeviceCount">Adapters the capture library reported.</param>
/// <param name="SelectedDeviceName">The adapter that carries game traffic, once found.</param>
public sealed record CaptureStatus(bool IsStarted, int ListeningDeviceCount, int AvailableDeviceCount, string? SelectedDeviceName)
{
    public static CaptureStatus NotStarted { get; } = new(false, 0, 0, null);
}

using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

/// <summary>How game packets reach this computer, read from the server's end of the connection.</summary>
public enum GameTrafficTransport
{
    None,
    IPv4,
    /// <summary>An IPv6-only network, such as phone tethering, carrying the server's IPv4 address.</summary>
    IPv6Nat64,
    IPv4MappedIPv6,
    IPv6
}

public sealed record CaptureAdapterState(string Name, string State);

public sealed record CaptureRestart(string Reason, DateTime Utc);

/// <summary>The capture details Copy diagnostics adds to the setup checklist. Adapter names come from the driver;
/// addresses, MACs and adapter ids are left out because the report gets pasted in public channels.</summary>
public sealed record CaptureDiagnostics(
    IReadOnlyList<CaptureAdapterState> Adapters,
    IReadOnlyList<string> UnknownServerIps,
    GameTrafficTransport Transport,
    int RestartCount,
    CaptureRestart? LastRestart);

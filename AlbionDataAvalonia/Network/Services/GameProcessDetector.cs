using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AlbionDataAvalonia.Network.Services;

/// <summary>Whether the game is running and which known VPNs or ping boosters run beside it.</summary>
public sealed record GameProcessScan(bool IsGameRunning, IReadOnlyList<string> TrafficRouters);

/// <summary>Tells whether the Albion Online game client is running, to explain a capture that sees no traffic.</summary>
public static class GameProcessDetector
{
    /// <summary>One pass over the running processes. Only the game and names on the VPN and booster list are kept.</summary>
    // Albion-Online on Windows and Linux, "Albion Online" on macOS. The launcher alone sends no game traffic.
    public static GameProcessScan Scan()
    {
        var ownId = Environment.ProcessId;
        var gameRunning = false;
        var routers = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == ownId) continue;
                    var name = process.ProcessName.Replace("-", "").Replace(" ", "").Replace("_", "");
                    if (name.StartsWith("AlbionOnline", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
                    {
                        gameRunning = true;
                    }
                    else if (TrafficRouterDetector.MatchProcess(name) is { } router && !routers.Contains(router))
                    {
                        routers.Add(router);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the list was being read.
                }
            }
        }

        routers.Sort(StringComparer.Ordinal);
        return new GameProcessScan(gameRunning, routers);
    }
}

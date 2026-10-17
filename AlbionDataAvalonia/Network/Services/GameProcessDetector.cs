using System;
using System.Diagnostics;

namespace AlbionDataAvalonia.Network.Services;

/// <summary>Tells whether the Albion Online game client is running, to explain a capture that sees no traffic.</summary>
public static class GameProcessDetector
{
    // Albion-Online on Windows and Linux, "Albion Online" on macOS. The launcher alone sends no game traffic.
    public static bool IsGameRunning()
    {
        var ownId = Environment.ProcessId;
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
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the list was being read.
                }
            }
        }

        return false;
    }
}

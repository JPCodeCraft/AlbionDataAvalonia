using Microsoft.Win32;
using Serilog;
using System;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace AlbionDataAvalonia.Settings;

public class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Albion Free Market Data Client";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Sync(bool startWithWindows)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        SyncWindows(startWithWindows);
    }

    [SupportedOSPlatform("windows")]
    private static void SyncWindows(bool startWithWindows)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key == null)
            {
                Log.Warning("Unable to open Windows startup registry key.");
                return;
            }

            if (startWithWindows)
            {
                key.SetValue(RunValueName, GetExecutableRunValue(), RegistryValueKind.String);
                return;
            }

            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to sync Windows startup setting.");
        }
    }

    private static string GetExecutableRunValue()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Process.GetCurrentProcess().MainModule?.FileName;
        }

        return $"\"{path ?? AppContext.BaseDirectory}\"";
    }
}

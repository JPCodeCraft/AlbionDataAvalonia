using Microsoft.Win32;
using Serilog;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AlbionDataAvalonia;

public static class WinPCapInstallationChecker
{
    private const string WinPCapInstallerPath = @"WinPCap\WinPCap_4_1_3.exe"; // Adjust the path according to your project structure

    public static bool IsWinPCapInstalled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check if WinPCap is in the registry
            using (RegistryKey? WinPCapKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinPCap"))
            {
                if (WinPCapKey != null)
                {
                    return true;
                }
            }

            // Check if WinPCap is in the registry
            using (RegistryKey? winPCapKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinPCap"))
            {
                if (winPCapKey != null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool InstallWinPCap()
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = WinPCapInstallerPath,
                Arguments = "",
                UseShellExecute = true,
                Verb = "runas" // Run the installer with administrative privileges
            };

            Process process = new Process { StartInfo = startInfo };
            process.Start();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Log.Information("WinPCap installation completed successfully.", "WinPCap Installation");
                return true;
            }
            else
            {
                Log.Error($"WinPCap installation failed with exit code: {process.ExitCode}", "WinPCap Installation");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred during WinPCap installation: {ex.Message}", "WinPCap Installation");
            return false;
        }
    }
}
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AlbionDataAvalonia;

public static class NpCapInstallationChecker
{
    public static bool IsNpCapInstalled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using (RegistryKey? WinPcapKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinPcap"))
            {
                if (WinPcapKey != null)
                {
                    return true;
                }
            }

            using (RegistryKey? nPCapKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Npcap"))
            {
                if (nPCapKey != null)
                {
                    return true;
                }
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-c \"dpkg -s libpcap-dev\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var process = new Process { StartInfo = startInfo };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (output.Contains("install ok installed"))
            {
                return true;
            }
        }

        return false;
    }
}
using Microsoft.Win32;
using System;
using System.Collections.Generic;
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

            using (RegistryKey? win10PcapKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Win10Pcap"))
            {
                if (win10PcapKey != null)
                {
                    return true;
                }
            }

        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Package names across different distributions
            var packageNames = new Dictionary<string, string>
            {
                { "dpkg", "libpcap-dev" },      // Debian, Ubuntu
                { "pacman", "libpcap" },        // Arch, Manjaro
                { "rpm", "libpcap-devel" },     // Red Hat, Fedora
                { "zypper", "libpcap-devel" }   // openSUSE
            };

            foreach (var packageManager in packageNames.Keys)
            {
                if (IsCommandAvailable(packageManager))
                {
                    var (isInstalled, _) = CheckPackageWithManager(packageManager, packageNames[packageManager]);
                    if (isInstalled)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    static private bool IsCommandAvailable(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"command -v {command}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    static private (bool isInstalled, string output) CheckPackageWithManager(string packageManager, string packageName)
    {
        var command = packageManager switch
        {
            "dpkg" => $"dpkg -s {packageName}",
            "pacman" => $"pacman -Qi {packageName}",
            "rpm" => $"rpm -q {packageName}",
            "zypper" => $"zypper search -i {packageName}",
            _ => throw new ArgumentException($"Unsupported package manager: {packageManager}")
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var isInstalled = packageManager switch
        {
            "dpkg" => output.Contains("install ok installed"),
            "pacman" => process.ExitCode == 0,
            "rpm" => process.ExitCode == 0,
            "zypper" => output.Contains(packageName) && output.Contains("i |"),
            _ => false
        };

        return (isInstalled, output);
    }
}
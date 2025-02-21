using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AlbionDataAvalonia;

public static class NpCapInstallationChecker
{
    public static bool IsNpCapInstalled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] registryPaths = new[]
            {
                @"SOFTWARE\WOW6432Node\WinPcap",
                @"SOFTWARE\WinPcap",
                @"SOFTWARE\WOW6432Node\Npcap",
                @"SOFTWARE\Npcap",
                @"SYSTEM\CurrentControlSet\Services\Win10Pcap"
            };

            foreach (var path in registryPaths)
            {
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(path))
                {
                    if (key != null)
                    {
                        return true;
                    }
                }
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Check if libpcap shared object files exist directly
            if (File.Exists("/usr/lib/libpcap.so") || File.Exists("/usr/local/lib/libpcap.so"))
            {
                return true;
            }

            // Define package names for each package manager
            var packageNames = new Dictionary<string, string>
            {
                { "dpkg", "libpcap-dev" },       // Debian, Ubuntu
                { "pacman", "libpcap" },         // Arch, Manjaro
                { "rpm", "libpcap-devel" },      // Red Hat, Fedora (older)
                { "zypper", "libpcap-devel" },   // openSUSE
                { "emerge", "net-libs/libpcap" },// Gentoo
                { "slackpkg", "libpcap" },       // Slackware
                { "dnf", "libpcap-devel" },      // Fedora (newer)
                { "yum", "libpcap-devel" },      // Older Red Hat-based
                { "apk", "libpcap-dev" }         // Alpine Linux
            };

            // Check each package manager
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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return true; // Always true for macOS as per your decision
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
        // Define the command to check if the package is installed
        var command = packageManager switch
        {
            "dpkg" => $"dpkg -s {packageName}",
            "pacman" => $"pacman -Qi {packageName}",
            "rpm" => $"rpm -q {packageName}",
            "zypper" => $"zypper search -i {packageName}",
            "emerge" => $"emerge -pv {packageName}",      // Gentoo: Check if installed
            "slackpkg" => $"slackpkg info {packageName}", // Slackware: Package info
            "dnf" => $"dnf list installed {packageName}", // Fedora: List installed
            "yum" => $"yum list installed {packageName}", // Older Red Hat: List installed
            "apk" => $"apk info -e {packageName}",        // Alpine: Check if installed
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

        // Determine if the package is installed based on the output or exit code
        var isInstalled = packageManager switch
        {
            "dpkg" => output.Contains("install ok installed"),
            "pacman" => process.ExitCode == 0,
            "rpm" => process.ExitCode == 0,
            "zypper" => output.Contains(packageName) && output.Contains("i |"),
            "emerge" => output.Contains("[I]"),          // Gentoo: [I] indicates installed
            "slackpkg" => output.Contains("INSTALLED"),  // Slackware: Check for "INSTALLED"
            "dnf" => process.ExitCode == 0,
            "yum" => process.ExitCode == 0,
            "apk" => process.ExitCode == 0,              // Alpine: Exit code 0 if installed
            _ => false
        };

        return (isInstalled, output);
    }
}
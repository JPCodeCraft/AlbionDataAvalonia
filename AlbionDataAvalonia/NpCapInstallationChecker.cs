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
            if (TryLoadLibpcap())
            {
                return true;
            }

            if (IsLibpcapInCommonPaths())
            {
                return true;
            }

            if (IsLibpcapReportedByLdconfig())
            {
                return true;
            }

            // Fallback: probe common package managers for a libpcap installation
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
                { "apk", "libpcap-dev" },        // Alpine Linux
                { "nix-env", "libpcap" }         // NixOS / nix package manager
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
            try
            {
                // Try opening a pcap device to verify /dev/bpf* permissions
                var devices = SharpPcap.CaptureDeviceList.New();
                if (devices == null || devices.Count == 0)
                {
                    return false;
                }
                using var dev = devices[0];
                dev.Open(new SharpPcap.DeviceConfiguration
                {
                    Mode = SharpPcap.DeviceModes.None,
                    ReadTimeout = 1000
                });
                dev.Close();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false; // Missing BPF permissions
            }
            catch
            {
                // Conservative: if anything else fails, report not installed so UI can guide user
                return false;
            }
        }

        return false;
    }

    static private bool TryLoadLibpcap()
    {
        var candidateNames = new[]
        {
            "libpcap.so",
            "libpcap.so.1",
            "libpcap.so.0.8"
        };

        foreach (var libraryName in candidateNames)
        {
            if (NativeLibrary.TryLoad(libraryName, out var handle))
            {
                NativeLibrary.Free(handle);
                return true;
            }
        }

        return false;
    }

    static private bool IsLibpcapInCommonPaths()
    {
        var searchDirectories = new[]
        {
            "/usr/lib",
            "/usr/local/lib",
            "/lib",
            "/usr/lib/x86_64-linux-gnu",
            "/lib/x86_64-linux-gnu",
            "/usr/lib/aarch64-linux-gnu",
            "/lib/aarch64-linux-gnu"
        };

        foreach (var directory in searchDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var _ in Directory.EnumerateFiles(directory, "libpcap.so*"))
            {
                return true;
            }
        }

        return false;
    }

    static private bool IsLibpcapReportedByLdconfig()
    {
        if (!IsCommandAvailable("ldconfig"))
        {
            return false;
        }

        var (_, output) = RunCommand("ldconfig -p | grep libpcap");
        return !string.IsNullOrWhiteSpace(output);
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
            "emerge" => $"emerge -pv {packageName}",      // Gentoo: Check if installed
            "slackpkg" => $"slackpkg info {packageName}", // Slackware: Package info
            "dnf" => $"dnf list installed {packageName}", // Fedora: List installed
            "yum" => $"yum list installed {packageName}", // Older Red Hat: List installed
            "apk" => $"apk info -e {packageName}",        // Alpine: Check if installed
            "nix-env" => $"nix-env -q {packageName}",
            _ => throw new ArgumentException($"Unsupported package manager: {packageManager}")
        };

        var (exitCode, output) = RunCommand(command);

        var isInstalled = packageManager switch
        {
            "dpkg" => output.Contains("install ok installed", StringComparison.OrdinalIgnoreCase),
            "pacman" => exitCode == 0,
            "rpm" => exitCode == 0,
            "zypper" => output.Contains(packageName, StringComparison.OrdinalIgnoreCase) && output.Contains("i |"),
            "emerge" => output.Contains("[I]"),          // Gentoo: [I] indicates installed
            "slackpkg" => output.Contains("INSTALLED", StringComparison.OrdinalIgnoreCase),
            "dnf" => exitCode == 0,
            "yum" => exitCode == 0,
            "apk" => exitCode == 0,
            "nix-env" => !string.IsNullOrWhiteSpace(output),
            _ => false
        };

        return (isInstalled, output);
    }

    static private (int exitCode, string output) RunCommand(string command)
    {
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

        return (process.ExitCode, output);
    }
}

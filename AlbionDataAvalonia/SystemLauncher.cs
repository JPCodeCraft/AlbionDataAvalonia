using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AlbionDataAvalonia;

/// <summary>Opens links and folders with the operating system's default handler.</summary>
public static class SystemLauncher
{
    public const string AfmWebsiteUrl = "https://www.albionfreemarket.com";
    public const string AodpWebsiteUrl = "https://www.albion-online-data.com/";
    public const string AfmDiscordUrl = "https://discord.com/invite/BPmDE3zznH";

    public static bool TryOpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Log.Error("Invalid URL: {Url}", url);
            return false;
        }

        if (TryOpenUrl(uri))
        {
            return true;
        }

        Log.Warning("Unable to open URL: {Url}", url);
        return false;
    }

    public static bool TryOpenUrl(Uri uri)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TryShellExecute(uri.AbsoluteUri);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return TryStartLauncher("xdg-open", uri.AbsoluteUri)
                || TryStartLauncher("gio", uri.AbsoluteUri, "open")
                || TryStartLauncher("x-www-browser", uri.AbsoluteUri);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return TryStartLauncher("open", uri.AbsoluteUri);
        }

        return false;
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            var opened = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? TryShellExecute(path)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? TryStartLauncher("xdg-open", path)
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && TryStartLauncher("open", path);

            if (opened)
            {
                Log.Debug("Opened folder. Path={FolderPath}", path);
            }
            else
            {
                Log.Warning("Unable to open folder. Path={FolderPath}", path);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open folder. Path={FolderPath}", path);
        }
    }

    private static bool TryShellExecute(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });

            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Windows could not open {Target} with the system shell", target);
            return false;
        }
    }

    private static bool TryStartLauncher(string executable, string target, string? command = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false
            };

            if (!string.IsNullOrEmpty(command))
            {
                startInfo.ArgumentList.Add(command);
            }

            startInfo.ArgumentList.Add(target);
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Launcher {Launcher} was unavailable", executable);
            return false;
        }
    }
}

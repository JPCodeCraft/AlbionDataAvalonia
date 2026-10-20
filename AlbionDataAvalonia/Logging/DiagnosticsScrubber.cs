using AlbionDataAvalonia.Network.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace AlbionDataAvalonia.Logging;

/// <summary>
/// Takes personal details out of log text before Copy diagnostics includes it, since the report gets pasted in public
/// channels: the user folder in paths, the computer's name, the character, guild and alliance names, email addresses,
/// URL query strings and any IP address that isn't an Albion server's.
/// </summary>
public sealed class DiagnosticsScrubber
{
    private static readonly Regex UserFolder = new(@"[A-Za-z]:\\{1,2}Users\\{1,2}[^\\/\s""']+|/(?:Users|home)/[^/\s""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Email = new(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+", RegexOptions.Compiled);
    private static readonly Regex UrlQuery = new(@"(https?://[^\s?""']+)\?[^\s""']*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Ipv4 = new(@"(?<![\w.])\d{1,3}(?:\.\d{1,3}){3}(?![\w.])", RegexOptions.Compiled);
    private static readonly Regex Ipv6 = new(@"(?<![\w:])[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7}(?:\.\d{1,3}){0,3}(?![\w:])", RegexOptions.Compiled);

    private readonly (Regex Pattern, string Replacement)[] names;
    private readonly HashSet<string> serverIps;

    /// <param name="names">Names to hide with what replaces them, such as the character's name and "&lt;character&gt;".</param>
    /// <param name="serverIps">Unknown Albion server addresses to keep; known Albion servers are always kept.</param>
    public DiagnosticsScrubber(IEnumerable<(string Name, string Replacement)> names, IEnumerable<string> serverIps)
    {
        this.names = names
            .Where(entry => entry.Name.Trim().Length >= 3)
            .OrderByDescending(entry => entry.Name.Length)
            .Select(entry => (new Regex($@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(entry.Name.Trim())}(?![\p{{L}}\p{{N}}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), entry.Replacement))
            .ToArray();
        this.serverIps = new HashSet<string>(serverIps, StringComparer.OrdinalIgnoreCase);
    }

    public string Scrub(string text)
    {
        text = UserFolder.Replace(text, "~");
        text = Email.Replace(text, "<email>");
        text = UrlQuery.Replace(text, "$1?…");
        foreach (var (pattern, replacement) in names)
        {
            text = pattern.Replace(text, replacement);
        }

        text = Ipv6.Replace(text, match => KeepAddress(match.Value, AddressFamily.InterNetworkV6) ? match.Value : "<ip>");
        // Version numbers look like IPv4 addresses; "v1.2.3.4" and "v.1.2.3.4" are already skipped by the pattern.
        var input = text;
        return Ipv4.Replace(input, match => IsVersion(input, match.Index) || KeepAddress(match.Value, AddressFamily.InterNetwork)
            ? match.Value
            : "<ip>");
    }

    private bool KeepAddress(string value, AddressFamily family)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != family)
        {
            // Not an address after all, such as a clock time.
            return true;
        }

        return serverIps.Contains(address.ToString()) || AlbionServers.FindByAddress(address) is not null;
    }

    private static bool IsVersion(string text, int index) =>
        text.AsSpan(0, index).TrimEnd().EndsWith("version", StringComparison.OrdinalIgnoreCase);
}

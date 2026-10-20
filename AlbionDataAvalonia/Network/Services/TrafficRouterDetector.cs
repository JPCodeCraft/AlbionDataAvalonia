using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Services;

/// <summary>
/// Spots VPNs and ping boosters by process and adapter name. They can route the game's traffic through a tunnel the
/// capture can't read, which is the most common reason no traffic arrives. Only names on this list are ever reported.
/// </summary>
public static class TrafficRouterDetector
{
    private sealed record KnownRouter(string Name, string[] Processes, string[] Adapters);

    // Process fragments are compared with the start of a lowercase process name without '-', '_' or spaces;
    // adapter fragments with any part of the lowercase adapter name.
    private static readonly KnownRouter[] Known =
    [
        new("ExitLag", ["exitlag"], ["exitlag"]),
        new("WTFast", ["wtfast"], ["wtfast"]),
        new("NoPing", ["noping"], ["noping"]),
        new("Mudfish", ["mudfish"], ["mudfish"]),
        new("GearUP", ["gearup"], ["gearup"]),
        new("LagoFast", ["lagofast"], ["lagofast"]),
        new("NordVPN", ["nordvpn"], ["nordlynx", "nordvpn"]),
        new("ExpressVPN", ["expressvpn"], ["expressvpn"]),
        new("Proton VPN", ["protonvpn"], ["protonvpn", "proton vpn"]),
        new("Surfshark", ["surfshark"], ["surfshark"]),
        new("Private Internet Access", ["piaclient", "piaservice"], ["private internet access"]),
        new("Cloudflare WARP", ["warpsvc", "cloudflarewarp"], ["cloudflare warp"]),
        new("Hamachi", ["hamachi"], ["hamachi"]),
        new("Radmin VPN", ["radminvpn", "rvrvpngui", "rvcontrolsvc"], ["radmin vpn"]),
        new("ZeroTier", ["zerotier"], ["zerotier"]),
        new("Tailscale", ["tailscale"], ["tailscale"]),
        new("OpenVPN", ["openvpn"], ["openvpn"]),
        new("WireGuard", ["wireguard"], ["wireguard"]),
        // Shared tunnel drivers that many VPNs install.
        new("VPN", [], ["tap-windows", "wintun", "tap adapter"])
    ];

    /// <summary>The known VPN or booster this process name belongs to, if any.</summary>
    public static string? MatchProcess(string processName)
    {
        var name = processName.Replace("-", "").Replace(" ", "").Replace("_", "").ToLowerInvariant();
        return Known.FirstOrDefault(router => router.Processes.Any(fragment => name.StartsWith(fragment, StringComparison.Ordinal)))?.Name;
    }

    /// <summary>The known VPN or booster this network adapter belongs to, if any.</summary>
    public static string? MatchAdapter(string adapterName)
    {
        var name = adapterName.ToLowerInvariant();
        return Known.FirstOrDefault(router => router.Adapters.Any(fragment => name.Contains(fragment, StringComparison.Ordinal)))?.Name;
    }

    /// <summary>"ExitLag", "ExitLag and NordVPN", "ExitLag, NordVPN and WARP".</summary>
    public static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
    };
}

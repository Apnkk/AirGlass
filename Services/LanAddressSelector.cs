using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AirGlass.Services;

/// <summary>
/// Picks the IPv4 address of the physical LAN adapter (Wi-Fi / Ethernet) so the
/// built-in mDNS responder of uxplay advertises on it, not on a VPN adapter.
/// </summary>
public static class LanAddressSelector
{
    private static readonly string[] VirtualMarkers =
    {
        "Hyper-V", "Virtual", "vEthernet", "WSL", "Tailscale", "Radmin",
        "VPN", "TAP", "Wintun", "VMware", "VirtualBox", "Loopback",
    };

    /// <summary>Returns the best LAN IPv4 (with a default gateway), or null if none.</summary>
    public static string? FindBestIPv4()
    {
        try
        {
            string? best = null;
            var bestScore = int.MinValue;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                var label = nic.Name + " " + nic.Description;
                if (VirtualMarkers.Any(m => label.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;

                var props = nic.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any));
                if (!hasGateway) continue;

                var ip = props.UnicastAddresses
                    .Select(u => u.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                         && !a.ToString().StartsWith("169.254", StringComparison.Ordinal));
                if (ip is null) continue;

                var score = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => 20,
                    NetworkInterfaceType.Ethernet => 10,
                    _ => 0,
                };

                if (score > bestScore)
                {
                    bestScore = score;
                    best = ip.ToString();
                }
            }

            return best;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

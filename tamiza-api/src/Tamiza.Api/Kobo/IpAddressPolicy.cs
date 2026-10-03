using System.Net;
using System.Net.Sockets;

namespace Tamiza.Api.Kobo;

/// <summary>
/// Decides whether an address may be dialed for a user-supplied Kobo server: loopback, private, link-local,
/// unique-local, multicast, reserved and unspecified ranges are refused.
/// </summary>
public static class IpAddressPolicy
{
    private static readonly IPNetwork[] BlockedIPv4 = Parse(
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
        "192.0.0.0/24", "192.168.0.0/16", "198.18.0.0/15", "224.0.0.0/4", "240.0.0.0/4");

    // ::/96 covers the unspecified address, loopback and the deprecated IPv4-compatible range.
    private static readonly IPNetwork[] BlockedIPv6 = Parse("::/96", "fc00::/7", "fe80::/10", "fec0::/10", "ff00::/8");

    private static readonly IPNetwork Nat64 = IPNetwork.Parse("64:ff9b::/96");

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6 && Nat64.Contains(address))
        {
            // NAT64 embeds the IPv4 destination in the last 32 bits.
            address = new IPAddress(address.GetAddressBytes()[12..]);
        }

        var blocked = address.AddressFamily == AddressFamily.InterNetwork ? BlockedIPv4 : BlockedIPv6;
        return !blocked.Any(network => network.Contains(address));
    }

    private static IPNetwork[] Parse(params string[] networks) => networks.Select(IPNetwork.Parse).ToArray();
}

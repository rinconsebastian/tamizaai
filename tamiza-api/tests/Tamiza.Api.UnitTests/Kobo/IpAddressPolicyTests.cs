using System.Net;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.UnitTests.Kobo;

public sealed class IpAddressPolicyTests
{
    [Theory]
    // One address inside each blocked IPv4 range.
    [InlineData("0.1.2.3")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.168.1.10")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    // IPv6.
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::7f00:1")]
    [InlineData("fc00::1")]
    [InlineData("fdff:ffff::1")]
    [InlineData("fe80::1")]
    [InlineData("febf::1")]
    [InlineData("fec0::1")]
    [InlineData("ff00::")]
    [InlineData("ff02::1")]
    // IPv4 embedded in IPv6.
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    public void Blocked_addresses_are_not_public(string address) =>
        Assert.False(IpAddressPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory]
    // Just outside each blocked IPv4 range.
    [InlineData("1.0.0.1")]
    [InlineData("11.0.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("128.0.0.1")]
    [InlineData("169.255.0.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("192.0.1.1")]
    [InlineData("192.169.0.1")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.1")]
    [InlineData("223.255.255.255")]
    [InlineData("8.8.8.8")]
    // Public IPv6, and embedded public IPv4.
    [InlineData("2606:4700:4700::1111")]
    [InlineData("fbff::1")]
    [InlineData("2001:db8::1")]
    public void Addresses_outside_the_blocked_ranges_are_public(string address) =>
        Assert.True(IpAddressPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    public void Embedded_public_IPv4_is_public(string address) =>
        Assert.True(IpAddressPolicy.IsPublic(IPAddress.Parse(address)));
}

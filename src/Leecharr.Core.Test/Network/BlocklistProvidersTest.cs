// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Network.Blocklist;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class BlocklistProvidersTest
{
    [Test]
    public async Task RadixTreeBlocklistProvider_WithInlineComments_ParsesRulesCorrectly()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new List<string>
        {
            "10.0.0.0/8 # internal network",
            "192.168.1.50 # malicious host",
            "Spamhaus:1.2.3.4/32 ; botnet",
            "5.6.7.0/24 // dangerous subnet",
            "# Full comment line",
            "; Another comment",
            "// Third comment",
        };

        var count = await provider.LoadRulesAsync(rules);

        count.Should().Be(4);
        provider.IsIpBlocked("10.5.5.5").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.50").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.51").Should().BeFalse();
        provider.IsIpBlocked("1.2.3.4").Should().BeTrue();
        provider.IsIpBlocked("5.6.7.88").Should().BeTrue();
        provider.IsIpBlocked("8.8.8.8").Should().BeFalse();
    }

    [Test]
    public async Task P2PDatBlocklistProvider_WithInlineCommentsAndLevelSuffixes_ParsesRangesCorrectly()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new List<string>
        {
            "Bad_ISP:1.2.3.4-1.2.3.10:0",
            "10.0.0.1-10.0.0.50 # internal range",
            "Spammer:192.168.1.1-192.168.1.200:123 // spammer range",
            "Single_Host:172.16.0.5:0",
            "# comment",
            "; another comment",
        };

        var count = await provider.LoadRulesAsync(rules);

        count.Should().Be(4);
        provider.IsIpBlocked("1.2.3.5").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.10").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.11").Should().BeFalse();
        provider.IsIpBlocked("10.0.0.25").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.100").Should().BeTrue();
        provider.IsIpBlocked("172.16.0.5").Should().BeTrue();
        provider.IsIpBlocked("172.16.0.6").Should().BeFalse();
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_IPv4MappedIPv6Cidr_ParsesAndMatchesCorrectly()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new List<string>
        {
            "::ffff:192.168.1.0/120", // Equivalent to 192.168.1.0/24
            "::ffff:10.0.0.1/128", // Equivalent to 10.0.0.1/32
            "::ffff:172.16.0.0/96", // Prefix length exactly 96 (maps to 0.0.0.0/0)
        };

        var count = await provider.LoadRulesAsync(rules);

        count.Should().Be(3);
        provider.IsIpBlocked("192.168.1.50").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.1").Should().BeTrue();
        provider.IsIpBlocked("::ffff:192.168.1.50").Should().BeTrue();
        provider.IsIpBlocked("::ffff:10.0.0.1").Should().BeTrue();
        provider.IsIpBlocked("8.8.8.8").Should().BeTrue(); // blocked because ::ffff:172.16.0.0/96 clamps to /0
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_IPv4MappedIPv6CidrPrefixLessThan96_ClampsToZero()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new List<string>
        {
            "::ffff:192.168.1.0/64", // Prefix length < 96 clamps to 0 (blocks all IPv4)
        };

        var count = await provider.LoadRulesAsync(rules);

        count.Should().Be(1);
        provider.IsIpBlocked("1.1.1.1").Should().BeTrue();
        provider.IsIpBlocked("203.0.113.1").Should().BeTrue();
    }

    [Test]
    public async Task P2PDatBlocklistProvider_WithColonsInDescriptionHeaders_ParsesRulesCorrectly()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new List<string>
        {
            "US:ISP:1.2.3.4-1.2.3.10:0",
            "Bad:Org:ISP:10.0.0.1-10.0.0.100:0",
            "US:ISP:172.16.0.5:0",
            "US:ISP:192.168.1.0/24:0",
        };

        var count = await provider.LoadRulesAsync(rules);

        count.Should().Be(4);
        provider.IsIpBlocked("1.2.3.5").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.10").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.11").Should().BeFalse();
        provider.IsIpBlocked("10.0.0.50").Should().BeTrue();
        provider.IsIpBlocked("172.16.0.5").Should().BeTrue();
        provider.IsIpBlocked("172.16.0.6").Should().BeFalse();
        provider.IsIpBlocked("192.168.1.100").Should().BeTrue();
        provider.IsIpBlocked("192.168.2.1").Should().BeFalse();
    }

    [Test]
    public async Task P2PDatBlocklistProvider_WithIPv6RangesAndCidrs_ParsesAndMatchesCorrectly()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new List<string>
        {
            "2001:db8::1-2001:db8::10",
            "US:ISP:2001:db8:1234::1-2001:db8:1234::100:0",
            "2001:db8:abcd::/48",
            "US:ISP:fe80::/10:0",
            "::ffff:192.168.1.0/120", // IPv4-mapped IPv6 CIDR normalized to 192.168.1.0/24
        };

        var count = await provider.LoadRulesAsync(rules);

        count.Should().Be(5);

        // IPv6 range match
        provider.IsIpBlocked("2001:db8::5").Should().BeTrue();
        provider.IsIpBlocked("2001:db8::10").Should().BeTrue();
        provider.IsIpBlocked("2001:db8::11").Should().BeFalse();

        // IPv6 range with colon header and level suffix
        provider.IsIpBlocked("2001:db8:1234::50").Should().BeTrue();
        provider.IsIpBlocked("2001:db8:1234::101").Should().BeFalse();

        // IPv6 CIDR match
        provider.IsIpBlocked("2001:db8:abcd:1::1").Should().BeTrue();
        provider.IsIpBlocked("2001:db8:abce::1").Should().BeFalse();

        // IPv6 CIDR with colon header
        provider.IsIpBlocked("fe80::1ff:fe00:1").Should().BeTrue();

        // IPv4-mapped IPv6 CIDR match on both IPv4 and mapped IPv6
        provider.IsIpBlocked("192.168.1.50").Should().BeTrue();
        provider.IsIpBlocked("::ffff:192.168.1.50").Should().BeTrue();
        provider.IsIpBlocked("192.168.2.50").Should().BeFalse();
    }
}

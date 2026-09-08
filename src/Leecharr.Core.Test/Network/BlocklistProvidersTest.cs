// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
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

    [Test]
    public async Task LinuxIpSetBlocklistProvider_WhenKernelExecutionSucceeds_ActivatesKernelOffloadAndPopulatesRules()
    {
        var diskMock = NSubstitute.Substitute.For<NzbDrone.Common.Disk.IDiskProvider>();
        diskMock.FileExists("/usr/sbin/ipset").Returns(true);

        var executedCommands = new List<(string Args, string Stdin)>();

        var provider = new TestableLinuxIpSetBlocklistProvider(
            diskMock,
            (args, stdin) =>
            {
                executedCommands.Add((args, stdin));
                return Task.FromResult((0, "ipset v7.19", string.Empty));
            });

        var rules = new List<string>
        {
            "192.168.1.0/24 # subnet",
            "10.0.0.1 // single host",
            "2001:db8::/32 ; ipv6 prefix",
            "::ffff:172.16.0.0/120",
        };

        var loaded = await provider.LoadRulesAsync(rules);
        loaded.Should().Be(4);
        provider.IsKernelOffloadActive.Should().BeTrue();

        provider.IsIpBlocked("192.168.1.100").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.1").Should().BeTrue();
        provider.IsIpBlocked("2001:db8:1::5").Should().BeTrue();
        provider.IsIpBlocked("172.16.0.5").Should().BeTrue();
        provider.IsIpBlocked("8.8.8.8").Should().BeFalse();

        executedCommands.Should().Contain(c => c.Args == "restore" && c.Stdin.Contains("add leecharr_tmp_v4 192.168.1.0/24 -exist"));
        executedCommands.Should().Contain(c => c.Args == "restore" && c.Stdin.Contains("add leecharr_tmp_v6 2001:db8::/32 -exist"));

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("kernel netfilter offload enabled");

        provider.ClearRules();
        provider.RuleCount.Should().Be(0);
        provider.IsKernelOffloadActive.Should().BeFalse();
    }

    [Test]
    public async Task LinuxIpSetBlocklistProvider_WhenKernelExecutionFails_FallsBackToUserSpaceRadixTree()
    {
        var diskMock = NSubstitute.Substitute.For<NzbDrone.Common.Disk.IDiskProvider>();
        diskMock.FileExists("/usr/sbin/ipset").Returns(true);

        var provider = new TestableLinuxIpSetBlocklistProvider(
            diskMock,
            (args, stdin) => Task.FromResult((1, string.Empty, "ipset v7.19: Kernel error received: Operation not permitted")));

        var rules = new List<string>
        {
            "192.168.1.0/24",
            "10.0.0.50",
        };

        var loaded = await provider.LoadRulesAsync(rules);
        loaded.Should().Be(2);
        provider.IsKernelOffloadActive.Should().BeFalse();

        // User space lookups still work via Radix tree
        provider.IsIpBlocked("192.168.1.100").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.50").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.51").Should().BeFalse();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("user-space fallback mode");
        health.Warnings.Should().Contain(w => w.Contains("Operation not permitted"));
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_ParentCidrAfterChildCidr_PrunesSubtreeNodesAndBlocksBroadRange()
    {
        var provider = new RadixTreeBlocklistProvider();

        // 10.1.2.0/24 alone creates 25 nodes (root + 24 depth)
        var singleCount = await provider.LoadRulesAsync(new[] { "10.1.2.0/24" });
        singleCount.Should().Be(1);
        provider.GetNodeCounts().Ipv4Nodes.Should().Be(25);

        // Loading 10.1.2.0/24 followed by parent 10.0.0.0/8 prunes the /24 subtree to 9 nodes (root + 8 depth)
        var rules = new List<string>
        {
            "10.1.2.0/24",
            "10.0.0.0/8",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(2);
        provider.GetNodeCounts().Ipv4Nodes.Should().Be(9);

        provider.IsIpBlocked("10.1.2.5").Should().BeTrue();
        provider.IsIpBlocked("10.200.50.1").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.1").Should().BeFalse();
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_ChildCidrAfterParentCidr_ShortCircuitsDescentWithoutAllocatingNodes()
    {
        var provider = new RadixTreeBlocklistProvider();

        // Parent /8 loaded before /24 and /32 should short-circuit and not allocate any extra nodes
        var rules = new List<string>
        {
            "10.0.0.0/8",
            "10.1.2.0/24",
            "10.1.2.3/32",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(3);
        provider.GetNodeCounts().Ipv4Nodes.Should().Be(9);

        provider.IsIpBlocked("10.1.2.3").Should().BeTrue();
        provider.IsIpBlocked("10.254.1.1").Should().BeTrue();
        provider.IsIpBlocked("172.16.0.1").Should().BeFalse();
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_IPv6ParentAndChildSubnetPruning_PrunesAndShortCircuitsCorrectly()
    {
        var provider = new RadixTreeBlocklistProvider();

        // Child /48 then parent /32 then child /64
        var rules = new List<string>
        {
            "2001:db8:1234::/48",
            "2001:db8::/32",
            "2001:db8:5678::/64",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(3);
        provider.GetNodeCounts().Ipv6Nodes.Should().Be(33); // root + 32 levels

        provider.IsIpBlocked("2001:db8:1234::1").Should().BeTrue();
        provider.IsIpBlocked("2001:db8:9999::1").Should().BeTrue();
        provider.IsIpBlocked("2001:db9::1").Should().BeFalse();
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_ZeroPrefixLength_BlocksAllAndPrunesTree()
    {
        var provider = new RadixTreeBlocklistProvider();

        var rules = new List<string>
        {
            "192.168.1.0/24",
            "10.0.0.0/8",
            "0.0.0.0/0",
            "172.16.0.0/12",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(4);
        provider.GetNodeCounts().Ipv4Nodes.Should().Be(1); // root only

        provider.IsIpBlocked("1.2.3.4").Should().BeTrue();
        provider.IsIpBlocked("203.0.113.1").Should().BeTrue();
        provider.IsIpBlocked("10.5.5.5").Should().BeTrue();
    }

    private class TestableLinuxIpSetBlocklistProvider : LinuxIpSetBlocklistProvider
    {
        private readonly Func<string, string, Task<(int ExitCode, string StdOut, string StdErr)>> commandExecutor;

        public TestableLinuxIpSetBlocklistProvider(
            NzbDrone.Common.Disk.IDiskProvider diskProvider,
            Func<string, string, Task<(int ExitCode, string StdOut, string StdErr)>> commandExecutor)
            : base(diskProvider)
        {
            this.commandExecutor = commandExecutor;
        }

        protected override Task<(int ExitCode, string StdOut, string StdErr)> ExecuteIpSetCommandAsync(
            string arguments,
            string stdIn = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return this.commandExecutor(arguments, stdIn);
        }
    }
}

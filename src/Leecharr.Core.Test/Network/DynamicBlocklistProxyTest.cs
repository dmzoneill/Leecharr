// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Blocklist;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class DynamicBlocklistProxyTest
{
    private IBlocklistProvider radixTreeProvider = null!;
    private IBlocklistProvider p2pDatProvider = null!;
    private IBlocklistProvider linuxIpSetProvider = null!;
    private IConfigService configService = null!;
    private IEventAggregator eventAggregator = null!;
    private DynamicBlocklistProxy proxy = null!;

    [SetUp]
    public void SetUp()
    {
        this.radixTreeProvider = Substitute.For<IBlocklistProvider>();
        this.radixTreeProvider.ProviderId.Returns("RadixTree");
        this.radixTreeProvider.DisplayName.Returns("Managed Radix Trie (CIDR IPv4/IPv6)");
        this.radixTreeProvider.Version.Returns("1.0");
        this.radixTreeProvider.IsAvailable.Returns(true);
        this.radixTreeProvider.Capabilities.Returns(BlocklistCapabilities.IPv4 | BlocklistCapabilities.IPv6 | BlocklistCapabilities.Cidr);
        this.radixTreeProvider.RuleCount.Returns(5);
        this.radixTreeProvider.ProbeHealthAsync().Returns(Task.FromResult(new BlocklistHealthResult { IsHealthy = true, StatusMessage = "OK" }));
        this.radixTreeProvider.IsIpBlocked(Arg.Is<string>(ip => ip == "1.2.3.4" || ip.StartsWith("10."))).Returns(true);

        this.p2pDatProvider = Substitute.For<IBlocklistProvider>();
        this.p2pDatProvider.ProviderId.Returns("P2PDat");
        this.p2pDatProvider.DisplayName.Returns("PeerGuardian / eMule (.p2p / .dat Range Filter)");
        this.p2pDatProvider.Version.Returns("1.0");
        this.p2pDatProvider.IsAvailable.Returns(true);
        this.p2pDatProvider.Capabilities.Returns(BlocklistCapabilities.IPv4 | BlocklistCapabilities.P2PDat);
        this.p2pDatProvider.RuleCount.Returns(5);
        this.p2pDatProvider.ProbeHealthAsync().Returns(Task.FromResult(new BlocklistHealthResult { IsHealthy = true, StatusMessage = "OK" }));
        this.p2pDatProvider.LoadRulesAsync(Arg.Any<IEnumerable<string>>()).Returns(Task.FromResult(5));

        this.linuxIpSetProvider = Substitute.For<IBlocklistProvider>();
        this.linuxIpSetProvider.ProviderId.Returns("LinuxIpSet");
        this.linuxIpSetProvider.DisplayName.Returns("Linux Kernel IPSet / Netfilter Drop");
        this.linuxIpSetProvider.Version.Returns("1.0");
        this.linuxIpSetProvider.IsAvailable.Returns(true);
        this.linuxIpSetProvider.Capabilities.Returns(BlocklistCapabilities.All);
        this.linuxIpSetProvider.ProbeHealthAsync().Returns(Task.FromResult(new BlocklistHealthResult { IsHealthy = true, StatusMessage = "OK" }));

        this.configService = Substitute.For<IConfigService>();
        this.configService.GetValue("ActiveBlocklistProvider", Arg.Any<string>()).Returns("RadixTree");

        this.eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IBlocklistProvider> { this.radixTreeProvider, this.p2pDatProvider, this.linuxIpSetProvider };
        this.proxy = new DynamicBlocklistProxy(providers, this.configService, this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        this.proxy.ActiveProviderId.Should().Be("RadixTree");
        this.proxy.ActiveProvider.Should().BeSameAs(this.radixTreeProvider);
        this.proxy.TotalRulesLoaded.Should().Be(5);
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = this.proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "RadixTree", "P2PDat", "LinuxIpSet" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = this.proxy.GetProvider("P2PDat");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("P2PDat");
    }

    [Test]
    public void GetProvider_WithInvalidId_ReturnsNull()
    {
        var provider = this.proxy.GetProvider("NonExistent");
        provider.Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await this.proxy.ProbeProviderAsync("P2PDat");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeProviderAsync_WithInvalidProvider_ReturnsUnhealthy()
    {
        var probe = await this.proxy.ProbeProviderAsync("InvalidProvider");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProviderAndMigratesRules()
    {
        var rules = new List<string> { "1.2.3.4", "10.0.0.0/8" };
        await this.proxy.LoadRulesAsync(rules);

        var result = await this.proxy.SwitchProviderAsync("P2PDat");
        result.Should().BeTrue();
        this.proxy.ActiveProviderId.Should().Be("P2PDat");
        this.proxy.ActiveProvider.Should().BeSameAs(this.p2pDatProvider);

        await this.p2pDatProvider.Received(1).LoadRulesAsync(Arg.Is<IEnumerable<string>>(r => r.SequenceEqual(rules)));
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveBlocklistProvider"] == "P2PDat"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<BlocklistProviderSwitchedEvent>(e => e.PreviousProvider == "RadixTree" && e.NewProvider == "P2PDat" && e.RulesMigrated == 5));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsTrueWithoutWork()
    {
        var result = await this.proxy.SwitchProviderAsync("RadixTree");
        result.Should().BeTrue();
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<BlocklistProviderSwitchedEvent>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownProvider_ReturnsFalse()
    {
        var result = await this.proxy.SwitchProviderAsync("UnknownProvider");
        result.Should().BeFalse();
        this.proxy.ActiveProviderId.Should().Be("RadixTree");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        this.p2pDatProvider.ProbeHealthAsync().Returns(Task.FromResult(new BlocklistHealthResult
        {
            IsHealthy = false,
            StatusMessage = "File format corrupted",
        }));

        var result = await this.proxy.SwitchProviderAsync("P2PDat");
        result.Should().BeFalse();
        this.proxy.ActiveProviderId.Should().Be("RadixTree");
    }

    [Test]
    public void IsIpBlocked_DelegatesToActiveProvider()
    {
        this.proxy.IsIpBlocked("1.2.3.4").Should().BeTrue();
        this.proxy.IsIpBlocked("10.0.5.1").Should().BeTrue();
        this.proxy.IsIpBlocked("8.8.8.8").Should().BeFalse();

        this.radixTreeProvider.Received(1).IsIpBlocked("1.2.3.4");
        this.radixTreeProvider.Received(1).IsIpBlocked("8.8.8.8");
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_Ipv4_SingleIpAndCidr()
    {
        var provider = new RadixTreeBlocklistProvider();
        provider.ProviderId.Should().Be("RadixTree");
        provider.DisplayName.Should().Contain("Radix Trie");

        var rules = new[]
        {
            "1.2.3.4",
            "192.168.1.0/24",
            "10.0.0.0/8",
            "Bad Swarm:172.16.0.0/12",
        };

        var loaded = await provider.LoadRulesAsync(rules);
        loaded.Should().Be(4);
        provider.RuleCount.Should().Be(4);

        // Exact match
        provider.IsIpBlocked("1.2.3.4").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.5").Should().BeFalse();

        // /24 subnet match
        provider.IsIpBlocked("192.168.1.1").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.254").Should().BeTrue();
        provider.IsIpBlocked("192.168.2.1").Should().BeFalse();

        // /8 subnet match
        provider.IsIpBlocked("10.50.12.3").Should().BeTrue();
        provider.IsIpBlocked("11.0.0.1").Should().BeFalse();

        // /12 subnet match
        provider.IsIpBlocked("172.16.5.99").Should().BeTrue();
        provider.IsIpBlocked("172.31.255.255").Should().BeTrue();
        provider.IsIpBlocked("172.32.0.1").Should().BeFalse();

        // Probe health
        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.LoadedRuleCount.Should().Be(4);

        // Clear rules
        provider.ClearRules();
        provider.RuleCount.Should().Be(0);
        provider.IsIpBlocked("1.2.3.4").Should().BeFalse();
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_Ipv6_SingleIpAndCidr()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "2001:db8::1",
            "fe80::/10",
            "2001:db8:abcd::/48",
        };

        var loaded = await provider.LoadRulesAsync(rules);
        loaded.Should().Be(3);

        provider.IsIpBlocked("2001:db8::1").Should().BeTrue();
        provider.IsIpBlocked("2001:db8::2").Should().BeFalse();

        provider.IsIpBlocked("fe80::1ff:fe00:1").Should().BeTrue();
        provider.IsIpBlocked("2001:db8:abcd:1234::1").Should().BeTrue();
        provider.IsIpBlocked("2001:db8:abce::1").Should().BeFalse();
    }

    [Test]
    public async Task P2PDatBlocklistProvider_RangeAndCidrParsing()
    {
        var provider = new P2PDatBlocklistProvider();
        provider.ProviderId.Should().Be("P2PDat");

        var rules = new[]
        {
            "Malicious Swarm:1.2.3.10-1.2.3.20",
            "Rogue Node:5.5.5.5-5.5.5.5",
            "Corporate Range:10.0.0.100-10.0.0.200",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(3);

        // Range 1.2.3.10 - 1.2.3.20
        provider.IsIpBlocked("1.2.3.9").Should().BeFalse();
        provider.IsIpBlocked("1.2.3.10").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.15").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.20").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.21").Should().BeFalse();

        // Single IP range
        provider.IsIpBlocked("5.5.5.5").Should().BeTrue();
        provider.IsIpBlocked("5.5.5.6").Should().BeFalse();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.LoadedRuleCount.Should().Be(3);

        provider.ClearRules();
        provider.RuleCount.Should().Be(0);
        provider.IsIpBlocked("1.2.3.15").Should().BeFalse();
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_Ipv4MappedIpv6_MatchesIpv4Rules()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "1.2.3.4",
            "192.168.1.0/24",
        };

        await provider.LoadRulesAsync(rules);

        // IPv4-mapped IPv6 notation for blocked IPs
        provider.IsIpBlocked("::ffff:1.2.3.4").Should().BeTrue();
        provider.IsIpBlocked("::ffff:192.168.1.100").Should().BeTrue();

        // Non-blocked IPs
        provider.IsIpBlocked("::ffff:1.2.3.5").Should().BeFalse();
        provider.IsIpBlocked("::ffff:192.168.2.1").Should().BeFalse();
    }

    [Test]
    public async Task RadixTreeBlocklistProvider_Ipv4MappedIpv6_RulesLoadedProperly()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "::ffff:10.0.0.1",
            "::ffff:172.16.0.0/12",
        };

        var loaded = await provider.LoadRulesAsync(rules);
        loaded.Should().Be(2);

        // Check against both IPv4 and IPv4-mapped IPv6
        provider.IsIpBlocked("10.0.0.1").Should().BeTrue();
        provider.IsIpBlocked("::ffff:10.0.0.1").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.2").Should().BeFalse();

        provider.IsIpBlocked("172.16.5.1").Should().BeTrue();
        provider.IsIpBlocked("::ffff:172.16.5.1").Should().BeTrue();
        provider.IsIpBlocked("172.32.0.1").Should().BeFalse();
    }

    [Test]
    public async Task P2PDatBlocklistProvider_Ipv4MappedIpv6_MatchesIpv4Rules()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new[]
        {
            "Malicious Swarm:1.2.3.10-1.2.3.20",
            "Rogue Node:5.5.5.5-5.5.5.5",
        };

        await provider.LoadRulesAsync(rules);

        // IPv4-mapped IPv6 queries
        provider.IsIpBlocked("::ffff:1.2.3.15").Should().BeTrue();
        provider.IsIpBlocked("::ffff:5.5.5.5").Should().BeTrue();

        provider.IsIpBlocked("::ffff:1.2.3.9").Should().BeFalse();
        provider.IsIpBlocked("::ffff:5.5.5.6").Should().BeFalse();
    }

    [Test]
    public async Task P2PDatBlocklistProvider_Ipv4MappedIpv6_RulesLoadedProperly()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new[]
        {
            "Mapped Range:::ffff:1.2.3.10-::ffff:1.2.3.20",
            "::ffff:5.5.5.5",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(2);

        // Check against both IPv4 and IPv4-mapped IPv6
        provider.IsIpBlocked("1.2.3.15").Should().BeTrue();
        provider.IsIpBlocked("::ffff:1.2.3.15").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.9").Should().BeFalse();

        provider.IsIpBlocked("5.5.5.5").Should().BeTrue();
        provider.IsIpBlocked("::ffff:5.5.5.5").Should().BeTrue();
        provider.IsIpBlocked("5.5.5.6").Should().BeFalse();
    }

    [Test]
    public async Task LinuxIpSetBlocklistProvider_ProbeHealthAndDelegation()
    {
        var diskProvider = Substitute.For<IDiskProvider>();
        diskProvider.FileExists(Arg.Any<string>()).Returns(false);

        var provider = new LinuxIpSetBlocklistProvider(diskProvider);
        provider.ProviderId.Should().Be("LinuxIpSet");
        provider.DisplayName.Should().Contain("Linux Kernel IPSet");
        provider.DisplayName.Should().Contain("Stub / Experimental");
        provider.Capabilities.HasFlag(BlocklistCapabilities.LinuxIpSet).Should().BeFalse();

        var healthWithoutBinary = await provider.ProbeHealthAsync();
        healthWithoutBinary.Should().NotBeNull();
        healthWithoutBinary.IsHealthy.Should().BeFalse();

        diskProvider.FileExists("/usr/sbin/ipset").Returns(true);
        var healthWithBinary = await provider.ProbeHealthAsync();
        healthWithBinary.Should().NotBeNull();
        healthWithBinary.IsHealthy.Should().BeTrue();
        healthWithBinary.StatusMessage.Should().Contain("user-space fallback mode");
        healthWithBinary.Warnings.Should().Contain(w => w.Contains("Kernel-level packet dropping is currently disabled"));

        await provider.LoadRulesAsync(new[] { "192.168.1.0/24" });
        provider.IsIpBlocked("192.168.1.50").Should().BeTrue();
        provider.IsIpBlocked("192.168.2.50").Should().BeFalse();

        provider.ClearRules();
        provider.IsIpBlocked("192.168.1.50").Should().BeFalse();
    }
}

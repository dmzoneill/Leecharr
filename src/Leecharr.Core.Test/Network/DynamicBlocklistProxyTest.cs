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
    private IBlocklistProvider _radixTreeProvider = null!;
    private IBlocklistProvider _p2pDatProvider = null!;
    private IBlocklistProvider _linuxIpSetProvider = null!;
    private IConfigService _configService = null!;
    private IEventAggregator _eventAggregator = null!;
    private DynamicBlocklistProxy _proxy = null!;

    [SetUp]
    public void SetUp()
    {
        _radixTreeProvider = Substitute.For<IBlocklistProvider>();
        _radixTreeProvider.ProviderId.Returns("RadixTree");
        _radixTreeProvider.DisplayName.Returns("Managed Radix Trie (CIDR IPv4/IPv6)");
        _radixTreeProvider.Version.Returns("1.0");
        _radixTreeProvider.IsAvailable.Returns(true);
        _radixTreeProvider.Capabilities.Returns(BlocklistCapabilities.IPv4 | BlocklistCapabilities.IPv6 | BlocklistCapabilities.Cidr);
        _radixTreeProvider.RuleCount.Returns(5);
        _radixTreeProvider.ProbeHealthAsync().Returns(Task.FromResult(new BlocklistHealthResult { IsHealthy = true, StatusMessage = "OK" }));
        _radixTreeProvider.IsIpBlocked(Arg.Is<string>(ip => ip == "1.2.3.4" || ip.StartsWith("10."))).Returns(true);

        _p2pDatProvider = Substitute.For<IBlocklistProvider>();
        _p2pDatProvider.ProviderId.Returns("P2PDat");
        _p2pDatProvider.DisplayName.Returns("PeerGuardian / eMule (.p2p / .dat Range Filter)");
        _p2pDatProvider.Version.Returns("1.0");
        _p2pDatProvider.IsAvailable.Returns(true);
        _p2pDatProvider.Capabilities.Returns(BlocklistCapabilities.IPv4 | BlocklistCapabilities.P2PDat);
        _p2pDatProvider.RuleCount.Returns(5);
        _p2pDatProvider.ProbeHealthAsync().Returns(Task.FromResult(new BlocklistHealthResult { IsHealthy = true, StatusMessage = "OK" }));
        _p2pDatProvider.LoadRulesAsync(Arg.Any<IEnumerable<string>>()).Returns(Task.FromResult(5));

        _linuxIpSetProvider = Substitute.For<IBlocklistProvider>();
        _linuxIpSetProvider.ProviderId.Returns("LinuxIpSet");
        _linuxIpSetProvider.DisplayName.Returns("Linux Kernel IPSet / Netfilter Drop");
        _linuxIpSetProvider.Version.Returns("1.0");
        _linuxIpSetProvider.IsAvailable.Returns(true);
        _linuxIpSetProvider.Capabilities.Returns(BlocklistCapabilities.All);
        _linuxIpSetProvider.ProbeHealthAsync().Returns(Task.FromResult(new BlocklistHealthResult { IsHealthy = true, StatusMessage = "OK" }));

        _configService = Substitute.For<IConfigService>();
        _configService.GetValue("ActiveBlocklistProvider", Arg.Any<string>()).Returns("RadixTree");

        _eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IBlocklistProvider> { _radixTreeProvider, _p2pDatProvider, _linuxIpSetProvider };
        _proxy = new DynamicBlocklistProxy(providers, _configService, _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        _proxy.ActiveProviderId.Should().Be("RadixTree");
        _proxy.ActiveProvider.Should().BeSameAs(_radixTreeProvider);
        _proxy.TotalRulesLoaded.Should().Be(5);
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = _proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "RadixTree", "P2PDat", "LinuxIpSet" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = _proxy.GetProvider("P2PDat");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("P2PDat");
    }

    [Test]
    public void GetProvider_WithInvalidId_ReturnsNull()
    {
        var provider = _proxy.GetProvider("NonExistent");
        provider.Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await _proxy.ProbeProviderAsync("P2PDat");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeProviderAsync_WithInvalidProvider_ReturnsUnhealthy()
    {
        var probe = await _proxy.ProbeProviderAsync("InvalidProvider");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProviderAndMigratesRules()
    {
        var rules = new List<string> { "1.2.3.4", "10.0.0.0/8" };
        await _proxy.LoadRulesAsync(rules);

        var result = await _proxy.SwitchProviderAsync("P2PDat");
        result.Should().BeTrue();
        _proxy.ActiveProviderId.Should().Be("P2PDat");
        _proxy.ActiveProvider.Should().BeSameAs(_p2pDatProvider);

        await _p2pDatProvider.Received(1).LoadRulesAsync(Arg.Is<IEnumerable<string>>(r => r.SequenceEqual(rules)));
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveBlocklistProvider"] == "P2PDat"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<BlocklistProviderSwitchedEvent>(e => e.PreviousProvider == "RadixTree" && e.NewProvider == "P2PDat" && e.RulesMigrated == 5));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsTrueWithoutWork()
    {
        var result = await _proxy.SwitchProviderAsync("RadixTree");
        result.Should().BeTrue();
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<BlocklistProviderSwitchedEvent>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownProvider_ReturnsFalse()
    {
        var result = await _proxy.SwitchProviderAsync("UnknownProvider");
        result.Should().BeFalse();
        _proxy.ActiveProviderId.Should().Be("RadixTree");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        _p2pDatProvider.ProbeHealthAsync().Returns(Task.FromResult(new BlocklistHealthResult
        {
            IsHealthy = false,
            StatusMessage = "File format corrupted"
        }));

        var result = await _proxy.SwitchProviderAsync("P2PDat");
        result.Should().BeFalse();
        _proxy.ActiveProviderId.Should().Be("RadixTree");
    }

    [Test]
    public void IsIpBlocked_DelegatesToActiveProvider()
    {
        _proxy.IsIpBlocked("1.2.3.4").Should().BeTrue();
        _proxy.IsIpBlocked("10.0.5.1").Should().BeTrue();
        _proxy.IsIpBlocked("8.8.8.8").Should().BeFalse();

        _radixTreeProvider.Received(1).IsIpBlocked("1.2.3.4");
        _radixTreeProvider.Received(1).IsIpBlocked("8.8.8.8");
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
            "Bad Swarm:172.16.0.0/12"
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
            "2001:db8:abcd::/48"
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
            "Corporate Range:10.0.0.100-10.0.0.200"
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
    public async Task LinuxIpSetBlocklistProvider_ProbeHealthAndDelegation()
    {
        var diskProvider = Substitute.For<IDiskProvider>();
        diskProvider.FileExists(Arg.Any<string>()).Returns(false);

        var provider = new LinuxIpSetBlocklistProvider(diskProvider);
        provider.ProviderId.Should().Be("LinuxIpSet");
        provider.DisplayName.Should().Contain("Linux Kernel IPSet");

        var health = await provider.ProbeHealthAsync();
        health.Should().NotBeNull();

        await provider.LoadRulesAsync(new[] { "192.168.1.0/24" });
        provider.IsIpBlocked("192.168.1.50").Should().BeTrue();
        provider.IsIpBlocked("192.168.2.50").Should().BeFalse();

        provider.ClearRules();
        provider.IsIpBlocked("192.168.1.50").Should().BeFalse();
    }
}

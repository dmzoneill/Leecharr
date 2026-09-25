// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Network.Blocklist;

namespace Leecharr.Core.Test.Blocklist;

[TestFixture]
public class BlocklistProvidersAndTaskTest
{
    [Test]
    public async Task BlocklistUpdateTask_ExecuteAsync_WhenUpdateServiceSucceeds_ReturnsRuleCount()
    {
        var updateService = Substitute.For<IBlocklistUpdateService>();
        var configService = Substitute.For<IConfigService>();
        updateService.UpdateRulesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1234));

        using var task = new BlocklistUpdateTask(updateService, configService);
        var result = await task.ExecuteAsync(CancellationToken.None);

        result.Should().Be(1234);
        await updateService.Received(1).UpdateRulesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BlocklistUpdateTask_ExecuteAsync_WhenUpdateServiceThrows_CatchesAndReturnsZero()
    {
        var updateService = Substitute.For<IBlocklistUpdateService>();
        var configService = Substitute.For<IConfigService>();
        updateService.UpdateRulesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new IOException("Remote host closed connection.")));

        using var task = new BlocklistUpdateTask(updateService, configService);
        var result = await task.ExecuteAsync(CancellationToken.None);

        result.Should().Be(0);
    }

    [Test]
    public void BlocklistUpdateTask_Execute_WithCommand_CallsExecuteAsyncSynchronously()
    {
        var updateService = Substitute.For<IBlocklistUpdateService>();
        var configService = Substitute.For<IConfigService>();
        updateService.UpdateRulesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(55));

        using var task = new BlocklistUpdateTask(updateService, configService);
        task.Execute(new BlocklistUpdateCommand());

        updateService.Received(1).UpdateRulesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BlocklistUpdateTask_ExecuteAsync_WithCommandAndToken_ForwardsToken()
    {
        var updateService = Substitute.For<IBlocklistUpdateService>();
        var configService = Substitute.For<IConfigService>();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        updateService.UpdateRulesAsync(token).Returns(Task.FromResult(88));

        using var task = new BlocklistUpdateTask(updateService, configService);
        await task.ExecuteAsync(new BlocklistUpdateCommand(), token);

        await updateService.Received(1).UpdateRulesAsync(token);
    }

    [Test]
    public async Task BlocklistUpdateTask_Handle_WhenBlocklistEnabled_StartsLoop()
    {
        var updateService = Substitute.For<IBlocklistUpdateService>();
        var configService = Substitute.For<IConfigService>();
        configService.BlocklistEnabled.Returns(true);
        configService.BlocklistUpdateIntervalHours.Returns(100);
        updateService.UpdateRulesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(10));

        using var task = new BlocklistUpdateTask(updateService, configService);
        task.Handle(new ApplicationStartedEvent());

        // Wait briefly for background Task.Run to trigger the initial run
        await Task.Delay(100);

        // Initial run on startup in loop
        await updateService.Received().UpdateRulesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void BlocklistUpdateTask_Handle_WhenBlocklistDisabled_DoesNotStartLoop()
    {
        var updateService = Substitute.For<IBlocklistUpdateService>();
        var configService = Substitute.For<IConfigService>();
        configService.BlocklistEnabled.Returns(false);

        using var task = new BlocklistUpdateTask(updateService, configService);
        task.Handle(new ApplicationStartedEvent());

        updateService.DidNotReceive().UpdateRulesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void BlocklistUpdateTask_Dispose_CanBeCalledMultipleTimesWithoutThrowing()
    {
        var updateService = Substitute.For<IBlocklistUpdateService>();
        var configService = Substitute.For<IConfigService>();

        var task = new BlocklistUpdateTask(updateService, configService);
        var act = () =>
        {
            task.Dispose();
            task.Dispose();
        };

        act.Should().NotThrow();
    }

    [Test]
    public void RadixTree_Metadata_ReturnsExpectedProperties()
    {
        var provider = new RadixTreeBlocklistProvider();

        provider.ProviderId.Should().Be("RadixTree");
        provider.DisplayName.Should().Be("Managed Radix Trie (CIDR IPv4/IPv6)");
        provider.Version.Should().Be("1.0");
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.Should().Be(
            BlocklistCapabilities.IPv4 |
            BlocklistCapabilities.IPv6 |
            BlocklistCapabilities.Cidr |
            BlocklistCapabilities.LiveAutoRefresh);
        provider.RuleCount.Should().Be(0);
    }

    [Test]
    public async Task RadixTree_ProbeHealthAsync_ReturnsHealthyWithAccurateCount()
    {
        var provider = new RadixTreeBlocklistProvider();
        await provider.LoadRulesAsync(new[] { "192.168.1.0/24", "10.0.0.0/8" });

        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeTrue();
        health.LoadedRuleCount.Should().Be(2);
        health.StatusMessage.Should().Contain("2 active CIDR/IP rules");
    }

    [Test]
    public async Task RadixTree_LoadRulesAsync_WithNullOrEmpty_ReturnsZero()
    {
        var provider = new RadixTreeBlocklistProvider();

        var count1 = await provider.LoadRulesAsync(null!);
        count1.Should().Be(0);

        var count2 = await provider.LoadRulesAsync(new List<string>());
        count2.Should().Be(0);

        var count3 = await provider.LoadRulesAsync(new[] { "", "   ", "# comment", "; comment", "// comment" });
        count3.Should().Be(0);
        provider.RuleCount.Should().Be(0);
    }

    [Test]
    public async Task RadixTree_LoadRulesAsync_WithIpv4CidrRanges_MatchesExactBoundaries()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "192.168.1.0/24",
            "10.0.0.0/8",
            "172.16.5.0/28",
            "192.0.2.0/30",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(4);

        // /24 boundary testing
        provider.IsIpBlocked("192.168.1.0").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.1").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.254").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.255").Should().BeTrue();
        provider.IsIpBlocked("192.168.0.255").Should().BeFalse();
        provider.IsIpBlocked("192.168.2.0").Should().BeFalse();

        // /8 boundary testing
        provider.IsIpBlocked("10.0.0.0").Should().BeTrue();
        provider.IsIpBlocked("10.255.255.255").Should().BeTrue();
        provider.IsIpBlocked("9.255.255.255").Should().BeFalse();
        provider.IsIpBlocked("11.0.0.0").Should().BeFalse();

        // /28 boundary testing (range: 172.16.5.0 - 172.16.5.15)
        provider.IsIpBlocked("172.16.5.0").Should().BeTrue();
        provider.IsIpBlocked("172.16.5.15").Should().BeTrue();
        provider.IsIpBlocked("172.16.5.16").Should().BeFalse();

        // /30 boundary testing (range: 192.0.2.0 - 192.0.2.3)
        provider.IsIpBlocked("192.0.2.0").Should().BeTrue();
        provider.IsIpBlocked("192.0.2.1").Should().BeTrue();
        provider.IsIpBlocked("192.0.2.2").Should().BeTrue();
        provider.IsIpBlocked("192.0.2.3").Should().BeTrue();
        provider.IsIpBlocked("192.0.2.4").Should().BeFalse();
    }

    [Test]
    public async Task RadixTree_LoadRulesAsync_WithSingleIps_MatchesExactHost()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "1.1.1.1",
            "8.8.8.8/32",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(2);

        provider.IsIpBlocked("1.1.1.1").Should().BeTrue();
        provider.IsIpBlocked("1.1.1.2").Should().BeFalse();
        provider.IsIpBlocked("8.8.8.8").Should().BeTrue();
        provider.IsIpBlocked("8.8.8.9").Should().BeFalse();
    }

    [Test]
    public async Task RadixTree_LoadRulesAsync_WithIpv6Cidr_MatchesSubnet()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "2001:db8:1:2::/64",
            "fe80::/10",
            "::1/128",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(3);

        provider.IsIpBlocked("2001:db8:1:2::1").Should().BeTrue();
        provider.IsIpBlocked("2001:db8:1:2:ffff:ffff:ffff:ffff").Should().BeTrue();
        provider.IsIpBlocked("2001:db8:1:3::1").Should().BeFalse();

        provider.IsIpBlocked("fe80::1").Should().BeTrue();
        provider.IsIpBlocked("fe80::dead:beef").Should().BeTrue();
        provider.IsIpBlocked("febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff").Should().BeTrue();
        provider.IsIpBlocked("fec0::1").Should().BeFalse();

        provider.IsIpBlocked("::1").Should().BeTrue();
        provider.IsIpBlocked("::2").Should().BeFalse();
    }

    [Test]
    public async Task RadixTree_LoadRulesAsync_WithIpv4MappedIpv6_NormalizesAndMatches()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "192.168.1.0/24",
            "::ffff:10.0.0.0/104",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(2);

        provider.IsIpBlocked("::ffff:192.168.1.100").Should().BeTrue();
        provider.IsIpBlocked("::ffff:192.168.2.1").Should().BeFalse();

        provider.IsIpBlocked("10.5.5.5").Should().BeTrue();
        provider.IsIpBlocked("::ffff:10.5.5.5").Should().BeTrue();
        provider.IsIpBlocked("11.1.1.1").Should().BeFalse();
    }

    [Test]
    public async Task RadixTree_LoadRulesAsync_WithInlineCommentsAndLabels_ExtractsCleanRule()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "Spamhaus:1.2.3.4/32 ; malicious botnet",
            "DShield:5.6.7.0/24 // active scanner",
            "Internal:10.0.0.0/8 # private network",
            "PureComment: // line comment without rule",
            "# Another full comment line",
            "; Semicolon line",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(3);

        provider.IsIpBlocked("1.2.3.4").Should().BeTrue();
        provider.IsIpBlocked("5.6.7.50").Should().BeTrue();
        provider.IsIpBlocked("10.20.30.40").Should().BeTrue();
    }

    [Test]
    public async Task RadixTree_LoadRulesAsync_WithMalformedRules_IgnoresInvalidWithoutThrowing()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "999.999.999.999/24",
            "10.0.0.1/33",
            "10.0.0.1/-1",
            "10.0.0.1/abc",
            "not_an_ip",
            "2001::/129",
            "192.168.1.1/32",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(1);

        provider.IsIpBlocked("192.168.1.1").Should().BeTrue();
    }

    [Test]
    public async Task RadixTree_EdgeCaseIps_MatchesZeroBroadcastLoopbackAndPrivateRanges()
    {
        var provider = new RadixTreeBlocklistProvider();
        var rules = new[]
        {
            "0.0.0.0/8",
            "255.255.255.255/32",
            "127.0.0.0/8",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(3);

        // 0.0.0.0/8
        provider.IsIpBlocked("0.0.0.0").Should().BeTrue();
        provider.IsIpBlocked("0.255.255.255").Should().BeTrue();
        provider.IsIpBlocked("1.0.0.0").Should().BeFalse();

        // 255.255.255.255/32
        provider.IsIpBlocked("255.255.255.255").Should().BeTrue();
        provider.IsIpBlocked("255.255.255.254").Should().BeFalse();

        // 127.0.0.0/8
        provider.IsIpBlocked("127.0.0.1").Should().BeTrue();
        provider.IsIpBlocked("127.255.255.255").Should().BeTrue();
        provider.IsIpBlocked("128.0.0.1").Should().BeFalse();
    }

    [Test]
    public async Task RadixTree_ClearRules_ResetsTreeAndRuleCount()
    {
        var provider = new RadixTreeBlocklistProvider();
        await provider.LoadRulesAsync(new[] { "192.168.1.0/24", "10.0.0.1/32" });

        provider.IsIpBlocked("192.168.1.5").Should().BeTrue();
        provider.RuleCount.Should().Be(2);

        provider.ClearRules();

        provider.RuleCount.Should().Be(0);
        provider.IsIpBlocked("192.168.1.5").Should().BeFalse();
        provider.IsIpBlocked("10.0.0.1").Should().BeFalse();

        var health = await provider.ProbeHealthAsync();
        health.LoadedRuleCount.Should().Be(0);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not_valid")]
    [TestCase("300.0.0.1")]
    public void RadixTree_IsIpBlocked_WithNullOrInvalidInput_ReturnsFalse(string ip)
    {
        var provider = new RadixTreeBlocklistProvider();
        provider.IsIpBlocked(ip!).Should().BeFalse();
    }

    [Test]
    public void P2PDat_Metadata_ReturnsExpectedProperties()
    {
        var provider = new P2PDatBlocklistProvider();

        provider.ProviderId.Should().Be("P2PDat");
        provider.DisplayName.Should().Be("PeerGuardian / eMule (.p2p / .dat Range Filter)");
        provider.Version.Should().Be("1.0");
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.Should().Be(
            BlocklistCapabilities.IPv4 |
            BlocklistCapabilities.IPv6 |
            BlocklistCapabilities.Cidr |
            BlocklistCapabilities.P2PDat |
            BlocklistCapabilities.LiveAutoRefresh);
        provider.RuleCount.Should().Be(0);
    }

    [Test]
    public async Task P2PDat_ProbeHealthAsync_ReturnsHealthyWithRangeCount()
    {
        var provider = new P2PDatBlocklistProvider();
        await provider.LoadRulesAsync(new[] { "Test:1.2.3.4-1.2.3.10:0" });

        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeTrue();
        health.LoadedRuleCount.Should().Be(1);
        health.StatusMessage.Should().Contain("1 IP ranges loaded");
    }

    [Test]
    public async Task P2PDat_LoadRulesAsync_WithNullOrEmpty_ReturnsZero()
    {
        var provider = new P2PDatBlocklistProvider();

        var count1 = await provider.LoadRulesAsync(null!);
        count1.Should().Be(0);

        var count2 = await provider.LoadRulesAsync(new List<string>());
        count2.Should().Be(0);

        var count3 = await provider.LoadRulesAsync(new[] { "", "   ", "# comment" });
        count3.Should().Be(0);
    }

    [Test]
    public async Task P2PDat_LoadRulesAsync_WithP2PRangeFormat_MatchesRangeBoundaries()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new[]
        {
            "Bad_ISP:1.2.3.4-1.2.3.10:0",
            "10.0.0.1-10.0.0.50",
            "Spammer:192.168.1.1-192.168.1.200:123 // spammer range",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(3);

        // 1.2.3.4 - 1.2.3.10 boundary
        provider.IsIpBlocked("1.2.3.3").Should().BeFalse();
        provider.IsIpBlocked("1.2.3.4").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.7").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.10").Should().BeTrue();
        provider.IsIpBlocked("1.2.3.11").Should().BeFalse();

        // 10.0.0.1 - 10.0.0.50 boundary
        provider.IsIpBlocked("10.0.0.0").Should().BeFalse();
        provider.IsIpBlocked("10.0.0.1").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.50").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.51").Should().BeFalse();

        // 192.168.1.1 - 192.168.1.200
        provider.IsIpBlocked("192.168.1.1").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.200").Should().BeTrue();
        provider.IsIpBlocked("192.168.1.201").Should().BeFalse();
    }

    [Test]
    public async Task P2PDat_LoadRulesAsync_WithSingleIpAndCidrFormat_MatchesCorrectly()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new[]
        {
            "Single_Host:172.16.0.5:0",
            "Subnet:192.168.10.0/24:0",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(2);

        provider.IsIpBlocked("172.16.0.5").Should().BeTrue();
        provider.IsIpBlocked("172.16.0.6").Should().BeFalse();

        provider.IsIpBlocked("192.168.10.0").Should().BeTrue();
        provider.IsIpBlocked("192.168.10.128").Should().BeTrue();
        provider.IsIpBlocked("192.168.10.255").Should().BeTrue();
        provider.IsIpBlocked("192.168.11.0").Should().BeFalse();
    }

    [Test]
    public async Task P2PDat_LoadRulesAsync_WithOverlappingAndAdjacentRanges_MergesCorrectly()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new[]
        {
            "Range1:10.0.0.1-10.0.0.10:0",
            "Range2:10.0.0.11-10.0.0.20:0", // adjacent to Range1
            "Range3:10.0.0.15-10.0.0.30:0", // overlaps with Range2
        };

        var count = await provider.LoadRulesAsync(rules);

        // All 3 ranges merge into a single contiguous interval 10.0.0.1 - 10.0.0.30
        count.Should().Be(1);
        provider.RuleCount.Should().Be(1);

        provider.IsIpBlocked("10.0.0.0").Should().BeFalse();
        provider.IsIpBlocked("10.0.0.1").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.15").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.30").Should().BeTrue();
        provider.IsIpBlocked("10.0.0.31").Should().BeFalse();
    }

    [Test]
    public async Task P2PDat_LoadRulesAsync_WithIpv6Ranges_MatchesBoundaries()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new[]
        {
            "IPv6_Range:2001:db8::1-2001:db8::ff:0",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(1);

        provider.IsIpBlocked("2001:db8::1").Should().BeTrue();
        provider.IsIpBlocked("2001:db8::50").Should().BeTrue();
        provider.IsIpBlocked("2001:db8::ff").Should().BeTrue();
        provider.IsIpBlocked("2001:db8::100").Should().BeFalse();
    }

    [Test]
    public async Task P2PDat_LoadRulesAsync_WithIpv4MappedIpv6_UnmapsAndMatches()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new[]
        {
            "Mapped:10.0.0.1-10.0.0.50:0",
        };

        await provider.LoadRulesAsync(rules);

        provider.IsIpBlocked("::ffff:10.0.0.25").Should().BeTrue();
        provider.IsIpBlocked("::ffff:10.0.0.51").Should().BeFalse();
    }

    [Test]
    public async Task P2PDat_LoadRulesAsync_WithInvertedRange_IgnoresInvalidRange()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new[]
        {
            "Inverted:192.168.1.100-192.168.1.10:0",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(0);
        provider.RuleCount.Should().Be(0);
        provider.IsIpBlocked("192.168.1.50").Should().BeFalse();
    }

    [Test]
    public async Task P2PDat_LoadRulesAsync_WithExtremeIps_ZeroAndBroadcast()
    {
        var provider = new P2PDatBlocklistProvider();
        var rules = new[]
        {
            "ZeroRange:0.0.0.0-0.0.0.255:0",
            "HighRange:255.255.255.0-255.255.255.255:0",
        };

        var count = await provider.LoadRulesAsync(rules);
        count.Should().Be(2);

        provider.IsIpBlocked("0.0.0.0").Should().BeTrue();
        provider.IsIpBlocked("0.0.0.255").Should().BeTrue();
        provider.IsIpBlocked("0.0.1.0").Should().BeFalse();

        provider.IsIpBlocked("255.255.255.0").Should().BeTrue();
        provider.IsIpBlocked("255.255.255.255").Should().BeTrue();
        provider.IsIpBlocked("255.255.254.255").Should().BeFalse();
    }

    [Test]
    public async Task P2PDat_ClearRules_ResetsRangesAndCount()
    {
        var provider = new P2PDatBlocklistProvider();
        await provider.LoadRulesAsync(new[] { "Test:1.2.3.4-1.2.3.10:0" });

        provider.RuleCount.Should().Be(1);
        provider.IsIpBlocked("1.2.3.5").Should().BeTrue();

        provider.ClearRules();

        provider.RuleCount.Should().Be(0);
        provider.IsIpBlocked("1.2.3.5").Should().BeFalse();

        var health = await provider.ProbeHealthAsync();
        health.LoadedRuleCount.Should().Be(0);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("invalid-ip")]
    public void P2PDat_IsIpBlocked_WithNullOrInvalidInput_ReturnsFalse(string ip)
    {
        var provider = new P2PDatBlocklistProvider();
        provider.IsIpBlocked(ip!).Should().BeFalse();
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.PortMapping;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class P2PDatBlocklistAndPortMapperComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task P2PDatBlocklist_LoadRulesAndFilterIps_FiltersAccurately()
    {
        var provider = new P2PDatBlocklistProvider();

        provider.ProviderId.Should().Be("P2PDat");
        provider.DisplayName.Should().NotBeNullOrWhiteSpace();
        provider.IsAvailable.Should().BeTrue();

        // 1. Load P2P format, CIDR format, and single IP rules
        var rules = new List<string>
        {
            "# Comment line to skip",
            "BadActorOrg:192.168.10.1-192.168.10.50",
            "SpammerSubnet:10.50.0.0/16",
            "eMuleFormatRule:172.16.1.100-172.16.1.200",
            "SingleHost:192.0.2.1-192.0.2.1",
            "IPv6Subnet:2001:db8:abcd::1-2001:db8:abcd::ffff",
        };

        var loadedCount = await provider.LoadRulesAsync(rules);
        loadedCount.Should().BeGreaterThan(0);
        provider.RuleCount.Should().Be(loadedCount);

        // 2. Test blocked IPv4
        provider.IsIpBlocked("192.168.10.25").Should().BeTrue();
        provider.IsIpBlocked("10.50.1.1").Should().BeTrue();
        provider.IsIpBlocked("172.16.1.150").Should().BeTrue();
        provider.IsIpBlocked("192.0.2.1").Should().BeTrue();

        // 3. Test allowed IPv4
        provider.IsIpBlocked("192.168.10.51").Should().BeFalse();
        provider.IsIpBlocked("10.51.0.1").Should().BeFalse();
        provider.IsIpBlocked("8.8.8.8").Should().BeFalse();

        // 4. Test blocked IPv6
        provider.IsIpBlocked("2001:db8:abcd::42").Should().BeTrue();
        provider.IsIpBlocked("2001:db8:ffff::1").Should().BeFalse();

        // 5. Invalid / empty strings
        provider.IsIpBlocked("").Should().BeFalse();
        provider.IsIpBlocked("not_an_ip").Should().BeFalse();

        // 6. Health check
        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.LoadedRuleCount.Should().Be(loadedCount);
    }

    [Test]
    public void NatPmpPortMapper_SuspendResumeAndLifecycle_MaintainsState()
    {
        var mapper = new NatPmpPortMapperService();

        // 1. Initial state
        mapper.IsSuspended.Should().BeFalse();
        mapper.ActiveMappings.Should().NotBeNull();

        // 2. Suspend
        mapper.Suspend();
        mapper.IsSuspended.Should().BeTrue();

        // 3. Resume
        mapper.Resume();
        mapper.IsSuspended.Should().BeFalse();

        // 4. Gateway discovery
        var gw = NatPmpPortMapperService.DiscoverDefaultGateway();
        // May be null or loopback in CI without default gateway, but exercises the gateway enumeration logic
        mapper.ActiveMappings.Should().BeEmpty();
    }
}

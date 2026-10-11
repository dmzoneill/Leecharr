// Copyright (c) FeedItOut. All rights reserved.

using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class ClientSimulationPolicyTest
{
    [Test]
    public void ResolveWireIdentity_WhenBehaviorEngineDisabled_UsesConfiguredIdentity()
    {
        var config = Substitute.For<IConfigService>();
        config.ClientBehaviorEngineEnabled.Returns(false);
        config.BitTorrentUserAgent.Returns("Custom/1.0");
        config.PeerIdPrefix.Returns("-CU1000-");

        var identity = ClientSimulationPolicy.ResolveWireIdentity(config);

        identity.UserAgent.Should().Be("Custom/1.0");
        identity.PeerIdPrefix.Should().Be("-CU1000-");
    }

    [Test]
    public void ShouldRotateClientProfile_RequiresEngineAndSwitchingEnabled()
    {
        var config = Substitute.For<IConfigService>();
        config.ClientBehaviorEngineEnabled.Returns(true);
        config.ClientProfileSwitching.Returns(true);
        config.SwitchClientProbability.Returns(1.0);

        ClientSimulationPolicy.ShouldRotateClientProfile(config, 0.1).Should().BeTrue();

        config.ClientBehaviorEngineEnabled.Returns(false);
        ClientSimulationPolicy.ShouldRotateClientProfile(config, 0.0).Should().BeFalse();
    }

    [Test]
    public void ApplySwarmConnectionLimit_IncreasesConnectionsWhenSeedersAreScarce()
    {
        var config = Substitute.For<IConfigService>();
        config.ClientBehaviorEngineEnabled.Returns(true);
        config.SwarmIntelligenceEnabled.Returns(true);
        config.SwarmAdaptationRate.Returns(0.2);

        var metrics = new SwarmPeerMetrics(10, 1, 9);
        var adjusted = ClientSimulationPolicy.ApplySwarmConnectionLimit(config, 50, metrics);

        adjusted.Should().BeGreaterThan(50);
    }

    [Test]
    public void ApplyVariationMultiplier_AppliesJitterWhenEnabled()
    {
        ClientSimulationPolicy.ApplyVariationMultiplier(true, 0.5, 1.0).Should().Be(1.5);
        ClientSimulationPolicy.ApplyVariationMultiplier(false, 0.5, 1.0).Should().Be(1.0);
    }

    [Test]
    public void GetDiurnalActivityScale_ReducesEarlyMorningTraffic()
    {
        var earlyMorning = new DateTime(2026, 10, 11, 4, 0, 0, DateTimeKind.Utc);
        ClientSimulationPolicy.GetDiurnalActivityScale(true, earlyMorning).Should().Be(0.7);
        ClientSimulationPolicy.GetDiurnalActivityScale(false, earlyMorning).Should().Be(1.0);
    }
}

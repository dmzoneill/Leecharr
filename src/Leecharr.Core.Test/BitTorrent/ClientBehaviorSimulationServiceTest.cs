// Copyright (c) FeedItOut. All rights reserved.

using System;
using FluentAssertions;
using MonoTorrent.Client;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class ClientBehaviorSimulationServiceTest
{
    [Test]
    public void ApplyEngineSimulation_WhenDisabled_DoesNotScaleConnections()
    {
        var config = Substitute.For<IConfigService>();
        config.ClientBehaviorEngineEnabled.Returns(false);

        var service = new ClientBehaviorSimulationService(config, () => 0.5);
        var builder = new EngineSettingsBuilder
        {
            MaximumConnections = 300,
            MaximumHalfOpenConnections = 50,
            StaleRequestTimeout = TimeSpan.FromSeconds(20),
            WebSeedDelay = TimeSpan.FromSeconds(30),
        };

        service.ApplyEngineSimulation(builder);

        builder.MaximumConnections.Should().Be(300);
        service.LastEngineActivityMultiplier.Should().Be(1.0);
    }

    [Test]
    public void ApplyEngineSimulation_WhenBurstProfileEnabled_ScalesConnectionsUp()
    {
        var config = Substitute.For<IConfigService>();
        config.ClientBehaviorEngineEnabled.Returns(true);
        config.RealisticVariations.Returns(false);
        config.TimeBasedPatterns.Returns(false);
        config.TrafficPatternProfile.Returns("burst");

        var service = new ClientBehaviorSimulationService(config, () => 0.5);
        var builder = new EngineSettingsBuilder
        {
            MaximumConnections = 200,
            MaximumHalfOpenConnections = 40,
            StaleRequestTimeout = TimeSpan.FromSeconds(20),
            WebSeedDelay = TimeSpan.FromSeconds(30),
        };

        service.ApplyEngineSimulation(builder);

        builder.MaximumConnections.Should().BeGreaterThan(200);
        service.LastEngineActivityMultiplier.Should().BeGreaterThan(1.0);
    }

    [Test]
    public void TryRotateProfileForAnnounce_WhenProbabilityMet_ReturnsAlternatePreset()
    {
        var config = Substitute.For<IConfigService>();
        config.ClientBehaviorEngineEnabled.Returns(true);
        config.ClientProfileSwitching.Returns(true);
        config.SwitchClientProbability.Returns(1.0);
        config.PrimaryClient.Returns("qBittorrent");

        var rolls = new[] { 0.0, 0.0 };
        var index = 0;
        var service = new ClientBehaviorSimulationService(config, () => rolls[Math.Min(index++, rolls.Length - 1)]);

        var rotated = service.TryRotateProfileForAnnounce(out var identity);

        rotated.Should().BeTrue();
        identity.PeerIdPrefix.Should().NotBe("-qB4650-");
    }
}

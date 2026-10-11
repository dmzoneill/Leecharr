// Copyright (c) FeedItOut. All rights reserved.

using System;
using MonoTorrent.Client;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

public interface IClientBehaviorSimulationService
{
    ClientWireIdentity GetWireIdentity();

    bool TryRotateProfileForAnnounce(out ClientWireIdentity identity);

    void ApplyEngineSimulation(EngineSettingsBuilder builder);

    int GetEffectivePerTorrentConnections(int baseMaxPerTorrent, SwarmPeerMetrics metrics);

    double LastEngineActivityMultiplier { get; }
}

public class ClientBehaviorSimulationService : IClientBehaviorSimulationService
{
    private readonly IConfigService configService;
    private readonly Func<double> randomRollProvider;
    private readonly Func<DateTime> utcNowProvider;
    private string activeProfileOverride;
    private double lastEngineActivityMultiplier = 1.0;

    public ClientBehaviorSimulationService(
        IConfigService configService,
        Func<double> randomRollProvider = null,
        Func<DateTime> utcNowProvider = null)
    {
        this.configService = configService;
        this.randomRollProvider = randomRollProvider ?? (() => Random.Shared.NextDouble());
        this.utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    public double LastEngineActivityMultiplier => this.lastEngineActivityMultiplier;

    public ClientWireIdentity GetWireIdentity()
    {
        if (!string.IsNullOrWhiteSpace(this.activeProfileOverride) && this.configService.ClientBehaviorEngineEnabled)
        {
            var preset = ClientEmulationPresets.GetPreset(this.activeProfileOverride);
            return new ClientWireIdentity(preset.UserAgent, preset.PeerIdPrefix);
        }

        return ClientSimulationPolicy.ResolveWireIdentity(this.configService, this.activeProfileOverride);
    }

    public bool TryRotateProfileForAnnounce(out ClientWireIdentity identity)
    {
        identity = this.GetWireIdentity();
        var roll = this.randomRollProvider();
        if (!ClientSimulationPolicy.ShouldRotateClientProfile(this.configService, roll))
        {
            return false;
        }

        var current = string.IsNullOrWhiteSpace(this.activeProfileOverride)
            ? this.configService.PrimaryClient
            : this.activeProfileOverride;

        this.activeProfileOverride = ClientSimulationPolicy.PickAlternateClientProfile(current, this.randomRollProvider());
        identity = this.GetWireIdentity();
        return true;
    }

    public void ApplyEngineSimulation(EngineSettingsBuilder builder)
    {
        if (builder == null || this.configService == null || !this.configService.ClientBehaviorEngineEnabled)
        {
            this.lastEngineActivityMultiplier = 1.0;
            return;
        }

        var variationRoll = this.randomRollProvider();
        var variationMultiplier = ClientSimulationPolicy.ApplyVariationMultiplier(
            this.configService.RealisticVariations,
            this.configService.BehaviorVariation,
            variationRoll);

        var trafficProfile = this.configService.TrafficPatternProfile;
        var diurnal = ClientSimulationPolicy.GetDiurnalActivityScale(
            this.configService.TimeBasedPatterns,
            this.utcNowProvider());

        var connectionMultiplier = ClientSimulationPolicy.GetTrafficPatternConnectionScale(trafficProfile) * variationMultiplier * diurnal;
        var halfOpenMultiplier = ClientSimulationPolicy.GetTrafficPatternHalfOpenScale(trafficProfile) * variationMultiplier;
        var staleMultiplier = ClientSimulationPolicy.GetTrafficPatternStaleRequestScale(trafficProfile) * variationMultiplier;
        var webSeedMultiplier = ClientSimulationPolicy.GetTrafficPatternWebSeedDelayScale(trafficProfile) * variationMultiplier;

        this.lastEngineActivityMultiplier = connectionMultiplier;

        if (builder.MaximumConnections > 0)
        {
            builder.MaximumConnections = ClientSimulationPolicy.ScaleBounded(builder.MaximumConnections, connectionMultiplier, minimum: 10);
        }

        if (builder.MaximumHalfOpenConnections > 0)
        {
            builder.MaximumHalfOpenConnections = ClientSimulationPolicy.ScaleBounded(builder.MaximumHalfOpenConnections, halfOpenMultiplier, minimum: 5);
        }

        if (builder.StaleRequestTimeout > TimeSpan.Zero)
        {
            var staleSeconds = ClientSimulationPolicy.ScaleSeconds(builder.StaleRequestTimeout.TotalSeconds, staleMultiplier);
            builder.StaleRequestTimeout = TimeSpan.FromSeconds(staleSeconds);
        }

        if (builder.WebSeedDelay > TimeSpan.Zero)
        {
            var webSeedSeconds = ClientSimulationPolicy.ScaleSeconds(builder.WebSeedDelay.TotalSeconds, webSeedMultiplier);
            builder.WebSeedDelay = TimeSpan.FromSeconds(webSeedSeconds);
        }
    }

    public int GetEffectivePerTorrentConnections(int baseMaxPerTorrent, SwarmPeerMetrics metrics)
    {
        return ClientSimulationPolicy.ApplySwarmConnectionLimit(this.configService, baseMaxPerTorrent, metrics);
    }
}

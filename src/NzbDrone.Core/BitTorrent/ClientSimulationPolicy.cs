// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

public readonly record struct ClientWireIdentity(string UserAgent, string PeerIdPrefix);

public readonly record struct SwarmPeerMetrics(int SampledPeers, int Seeders, int Leechers);

public static class ClientSimulationPolicy
{
    public static readonly string[] RotatingClients =
    [
        "qBittorrent",
        "Deluge",
        "Transmission",
        "uTorrent",
        "BiglyBT",
        "Leecharr",
    ];

    public static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    public static ClientWireIdentity ResolveWireIdentity(IConfigService config, string activeProfileOverride = null)
    {
        if (config == null)
        {
            return new ClientWireIdentity(ClientEmulationPresets.DefaultUserAgent, ClientEmulationPresets.DefaultPeerIdPrefix);
        }

        if (config.AnonymousMode)
        {
            return new ClientWireIdentity(string.Empty, string.Empty);
        }

        if (!config.ClientBehaviorEngineEnabled)
        {
            return new ClientWireIdentity(config.BitTorrentUserAgent, config.PeerIdPrefix);
        }

        var profile = string.IsNullOrWhiteSpace(activeProfileOverride)
            ? config.PrimaryClient
            : activeProfileOverride;

        var preset = ClientEmulationPresets.GetPreset(profile);
        var storedUserAgent = config.GetValue("BitTorrentUserAgent", string.Empty);
        var storedPeerPrefix = config.GetValue("PeerIdPrefix", string.Empty);

        var userAgent = !string.IsNullOrWhiteSpace(storedUserAgent) ? storedUserAgent : preset.UserAgent;
        var peerIdPrefix = !string.IsNullOrWhiteSpace(storedPeerPrefix) ? storedPeerPrefix : preset.PeerIdPrefix;

        return new ClientWireIdentity(userAgent, peerIdPrefix);
    }

    public static bool ShouldRotateClientProfile(IConfigService config, double roll)
    {
        if (config == null || !config.ClientBehaviorEngineEnabled || !config.ClientProfileSwitching)
        {
            return false;
        }

        var probability = Clamp01(config.SwitchClientProbability);
        return roll < probability;
    }

    public static string PickAlternateClientProfile(string currentProfile, double roll)
    {
        var normalizedCurrent = string.IsNullOrWhiteSpace(currentProfile)
            ? ClientEmulationPresets.DefaultClient
            : currentProfile;

        var candidates = RotatingClients
            .Where(c => !string.Equals(c, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 0)
        {
            return normalizedCurrent;
        }

        var index = (int)Math.Floor(Clamp01(roll) * candidates.Length);
        if (index >= candidates.Length)
        {
            index = candidates.Length - 1;
        }

        return candidates[index];
    }

    public static double GetTrafficPatternConnectionScale(string trafficPatternProfile)
    {
        return trafficPatternProfile?.ToLowerInvariant() switch
        {
            "burst" => 1.25,
            "stealth" => 0.75,
            "balanced" => 1.0,
            _ => 1.0,
        };
    }

    public static double GetTrafficPatternHalfOpenScale(string trafficPatternProfile)
    {
        return trafficPatternProfile?.ToLowerInvariant() switch
        {
            "burst" => 1.15,
            "stealth" => 0.85,
            "balanced" => 1.0,
            _ => 1.0,
        };
    }

    public static double GetTrafficPatternStaleRequestScale(string trafficPatternProfile)
    {
        return trafficPatternProfile?.ToLowerInvariant() switch
        {
            "burst" => 0.85,
            "stealth" => 1.15,
            "balanced" => 1.0,
            _ => 1.0,
        };
    }

    public static double GetTrafficPatternWebSeedDelayScale(string trafficPatternProfile)
    {
        return trafficPatternProfile?.ToLowerInvariant() switch
        {
            "burst" => 0.7,
            "stealth" => 1.3,
            "balanced" => 1.0,
            _ => 1.0,
        };
    }

    public static double GetDiurnalActivityScale(bool timeBasedPatterns, DateTime utcNow)
    {
        if (!timeBasedPatterns)
        {
            return 1.0;
        }

        var hour = utcNow.Hour;
        if (hour >= 2 && hour <= 6)
        {
            return 0.7;
        }

        if (hour >= 18 && hour <= 23)
        {
            return 1.05;
        }

        return 1.0;
    }

    public static double ApplyVariationMultiplier(
        bool realisticVariations,
        double behaviorVariation,
        double roll)
    {
        if (!realisticVariations)
        {
            return 1.0;
        }

        var variation = Clamp01(behaviorVariation);
        var jitter = (roll * 2.0) - 1.0;
        return Math.Max(0.25, 1.0 + (jitter * variation));
    }

    public static int ScaleBounded(int baseValue, double multiplier, int minimum = 1, int maximum = int.MaxValue)
    {
        if (baseValue <= 0)
        {
            return baseValue;
        }

        var scaled = (int)Math.Round(baseValue * Math.Max(0.1, multiplier));
        return Math.Clamp(scaled, minimum, maximum);
    }

    public static double ScaleSeconds(double baseSeconds, double multiplier, double minimumSeconds = 1.0)
    {
        if (baseSeconds <= 0)
        {
            return baseSeconds;
        }

        return Math.Max(minimumSeconds, baseSeconds * Math.Max(0.1, multiplier));
    }

    public static int ApplySwarmConnectionLimit(
        IConfigService config,
        int baseMaxPerTorrent,
        SwarmPeerMetrics metrics)
    {
        if (config == null || !config.ClientBehaviorEngineEnabled || !config.SwarmIntelligenceEnabled || baseMaxPerTorrent <= 0)
        {
            return baseMaxPerTorrent;
        }

        if (metrics.SampledPeers <= 0)
        {
            return baseMaxPerTorrent;
        }

        var adaptation = ClientSimulationPolicy.Clamp01(config.SwarmAdaptationRate);
        var seederRatio = metrics.Seeders / (double)metrics.SampledPeers;

        var multiplier = 1.0;
        if (seederRatio < 0.2)
        {
            multiplier = 1.0 + adaptation;
        }
        else if (seederRatio > 0.55)
        {
            multiplier = 1.0 - (adaptation * 0.5);
        }

        return ScaleBounded(baseMaxPerTorrent, multiplier, minimum: 5, maximum: Math.Max(baseMaxPerTorrent, baseMaxPerTorrent * 2));
    }

    public static SwarmPeerMetrics BuildSwarmMetrics(IEnumerable<(bool IsSeeder, bool IsConnected)> peers, int analysisDepth)
    {
        var depth = Math.Max(1, analysisDepth);
        var sample = peers?.Where(p => p.IsConnected).Take(depth).ToArray() ?? Array.Empty<(bool, bool)>();
        if (sample.Length == 0)
        {
            return new SwarmPeerMetrics(0, 0, 0);
        }

        var seeders = sample.Count(p => p.IsSeeder);
        var leechers = sample.Length - seeders;
        return new SwarmPeerMetrics(sample.Length, seeders, leechers);
    }
}

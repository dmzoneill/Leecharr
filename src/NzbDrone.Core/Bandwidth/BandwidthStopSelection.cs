// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Bandwidth;

public interface IBandwidthStopSelection
{
    HashSet<int> SelectUploadStoppedIndices(IReadOnlyList<Torrent> torrents);

    HashSet<int> SelectDownloadStoppedIndices(IReadOnlyList<Torrent> torrents);
}

public class BandwidthStopSelection : IBandwidthStopSelection
{
    private readonly IConfigService configService;
    private readonly Func<double> randomRollProvider;

    public BandwidthStopSelection(IConfigService configService, Func<double> randomRollProvider = null)
    {
        this.configService = configService;
        this.randomRollProvider = randomRollProvider ?? RandomUnitInterval;
    }

    public HashSet<int> SelectUploadStoppedIndices(IReadOnlyList<Torrent> torrents)
    {
        return SelectStoppedIndices(
            torrents,
            this.configService.UploadStoppedMinPercentage,
            this.configService.UploadStoppedMaxPercentage);
    }

    public HashSet<int> SelectDownloadStoppedIndices(IReadOnlyList<Torrent> torrents)
    {
        return SelectStoppedIndices(
            torrents,
            this.configService.DownloadStoppedMinPercentage,
            this.configService.DownloadStoppedMaxPercentage);
    }

    public static HashSet<int> SelectStoppedIndices(IReadOnlyList<Torrent> torrents, double minPct, double maxPct, Func<double> randomRoll = null)
    {
        var useInjectedRandom = randomRoll != null;
        randomRoll ??= RandomUnitInterval;
        var torrentCount = torrents?.Count ?? 0;
        if (maxPct <= 0 || torrentCount == 0)
        {
            return new HashSet<int>();
        }

        var eligibleIndices = new List<int>();
        for (var i = 0; i < torrentCount; i++)
        {
            if (!torrents[i].ForceStart)
            {
                eligibleIndices.Add(i);
            }
        }

        if (eligibleIndices.Count == 0)
        {
            return new HashSet<int>();
        }

        var stoppedPct = minPct + (randomRoll() * (maxPct - minPct));
        var stoppedCount = (int)Math.Ceiling(eligibleIndices.Count * (stoppedPct / 100.0));
        stoppedCount = Math.Min(stoppedCount, eligibleIndices.Count - 1);

        if (stoppedCount <= 0)
        {
            return new HashSet<int>();
        }

        var seededState = 1u;
        for (var j = eligibleIndices.Count - 1; j > 0; j--)
        {
            var k = useInjectedRandom ? NextSeeded(ref seededState, j + 1) : RandomNumberGenerator.GetInt32(j + 1);
            (eligibleIndices[j], eligibleIndices[k]) = (eligibleIndices[k], eligibleIndices[j]);
        }

        var stopped = new HashSet<int>();
        for (var i = 0; i < stoppedCount; i++)
        {
            stopped.Add(eligibleIndices[i]);
        }

        return stopped;
    }

    private static double RandomUnitInterval()
    {
        return RandomNumberGenerator.GetInt32(0, int.MaxValue) / (double)int.MaxValue;
    }

    private static int NextSeeded(ref uint state, int exclusiveMax)
    {
        state = unchecked((state * 1664525u) + 1013904223u);
        return (int)(state % (uint)exclusiveMax);
    }
}

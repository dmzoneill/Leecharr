// Copyright (c) FeedItOut. All rights reserved.

using System;

namespace NzbDrone.Core.Bandwidth.Distribution;

public class PowerLawDistributor : ISpeedDistributor
{
    private readonly double _alpha;

    public string Name => "PowerLaw";

    public PowerLawDistributor()
        : this(1.5)
    {
    }

    public PowerLawDistributor(double alpha)
    {
        _alpha = Math.Clamp(alpha, 0.5, 3.0);
    }

    public long[] Distribute(long totalBytesPerSecond, int torrentCount)
    {
        var speeds = new long[torrentCount];
        if (torrentCount <= 0 || totalBytesPerSecond <= 0)
        {
            return speeds;
        }

        if (torrentCount == 1)
        {
            speeds[0] = totalBytesPerSecond;
            return speeds;
        }

        var totalWeight = 0.0;
        var weights = new double[torrentCount];

        for (var i = 0; i < torrentCount; i++)
        {
            weights[i] = 1.0 / Math.Pow(i + 1, _alpha);
            totalWeight += weights[i];
        }

        if (totalWeight <= 0.0)
        {
            return speeds;
        }

        if (totalBytesPerSecond >= torrentCount)
        {
            var remainingBandwidth = totalBytesPerSecond - torrentCount;
            var allocated = 0L;

            for (var i = 0; i < torrentCount; i++)
            {
                speeds[i] = 1L + (long)(remainingBandwidth * (weights[i] / totalWeight));
                allocated += speeds[i];
            }

            var remainder = totalBytesPerSecond - allocated;
            for (var i = 0; i < remainder && i < torrentCount; i++)
            {
                speeds[i]++;
            }
        }
        else
        {
            for (var i = 0; i < totalBytesPerSecond; i++)
            {
                speeds[i] = 1L;
            }
        }

        return speeds;
    }
}

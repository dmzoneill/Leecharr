// Copyright (c) FeedItOut. All rights reserved.

using System;

namespace NzbDrone.Core.Bandwidth.Distribution;

public class LogNormalDistributor : ISpeedDistributor
{
    private readonly double _sigma;

    public string Name => "LogNormal";

    public LogNormalDistributor()
        : this(1.0)
    {
    }

    public LogNormalDistributor(double sigma)
    {
        _sigma = Math.Clamp(sigma, 0.1, 2.0);
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
            var p = (torrentCount - i - 0.5) / torrentCount;
            var z = NormalQuantile(p);
            weights[i] = Math.Exp(_sigma * z);
            totalWeight += weights[i];
        }

        return WeightedSpeedAllocator.Allocate(totalBytesPerSecond, torrentCount, weights, totalWeight);
    }

    private static double NormalQuantile(double p)
    {
        if (p <= 0.0)
        {
            return -8.0;
        }

        if (p >= 1.0)
        {
            return 8.0;
        }

        if (Math.Abs(p - 0.5) < 1e-10)
        {
            return 0.0;
        }

        var sign = p < 0.5 ? -1.0 : 1.0;
        var q = p < 0.5 ? p : 1.0 - p;
        var t = Math.Sqrt(-2.0 * Math.Log(q));

        const double c0 = 2.515517;
        const double c1 = 0.802853;
        const double c2 = 0.010328;
        const double d1 = 1.432788;
        const double d2 = 0.189269;
        const double d3 = 0.001308;

        var result = t - ((c0 + (c1 * t) + (c2 * t * t)) /
                (1.0 + (d1 * t) + (d2 * t * t) + (d3 * t * t * t)));

        return sign * result;
    }
}

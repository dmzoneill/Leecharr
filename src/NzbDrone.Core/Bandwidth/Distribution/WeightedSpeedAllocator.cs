// Copyright (c) FeedItOut. All rights reserved.

namespace NzbDrone.Core.Bandwidth.Distribution;

internal static class WeightedSpeedAllocator
{
    public static long[] Allocate(long totalBytesPerSecond, int torrentCount, double[] weights, double totalWeight)
    {
        var speeds = new long[torrentCount];
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

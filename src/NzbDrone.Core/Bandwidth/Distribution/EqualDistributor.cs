// Copyright (c) FeedItOut. All rights reserved.

namespace NzbDrone.Core.Bandwidth.Distribution;

public class EqualDistributor : ISpeedDistributor
{
    public string Name => "Equal";

    public long[] Distribute(long totalBytesPerSecond, int torrentCount)
    {
        var speeds = new long[torrentCount];
        if (torrentCount <= 0 || totalBytesPerSecond <= 0)
        {
            return speeds;
        }

        var perTorrent = totalBytesPerSecond / torrentCount;
        var remainder = totalBytesPerSecond % torrentCount;

        for (var i = 0; i < torrentCount; i++)
        {
            speeds[i] = perTorrent + (i < remainder ? 1L : 0L);
        }

        return speeds;
    }
}

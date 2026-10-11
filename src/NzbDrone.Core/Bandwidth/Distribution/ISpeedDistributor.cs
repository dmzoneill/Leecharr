// Copyright (c) FeedItOut. All rights reserved.

namespace NzbDrone.Core.Bandwidth.Distribution;

public interface ISpeedDistributor
{
    string Name { get; }

    long[] Distribute(long totalBytesPerSecond, int torrentCount);
}

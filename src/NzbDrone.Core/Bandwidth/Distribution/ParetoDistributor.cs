// Copyright (c) FeedItOut. All rights reserved.

namespace NzbDrone.Core.Bandwidth.Distribution;

public class ParetoDistributor : ISpeedDistributor
{
    private readonly PowerLawDistributor _powerLaw = new(1.16);

    public string Name => "Pareto";

    public long[] Distribute(long totalBytesPerSecond, int torrentCount)
    {
        return _powerLaw.Distribute(totalBytesPerSecond, torrentCount);
    }
}

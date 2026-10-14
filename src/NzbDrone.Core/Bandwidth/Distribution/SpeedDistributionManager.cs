using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Bandwidth.Distribution;

public interface ISpeedDistributionManager
{
    long[] DistributeSpeeds(int torrentCount);
    long[] DistributeSpeeds(int torrentCount, long maxSpeed);
    long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed);
    long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights);
    long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights, long[] caps);
    long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed);
    long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights);
    long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights, long[] caps);
    List<string> GetAvailableDistributions();
    string CurrentDistribution { get; }
    void InvalidateCache();
}

public class SpeedDistributionManager : ISpeedDistributionManager
{
    public const long MinimumFloorBytesPerSec = 5 * 1024;
    public const long MinimumTransmissionQuantum = 1024;
    private const long DefaultBytesPerSecond = 1_048_576;

    private readonly IEnumerable<ISpeedDistributor> _distributors;
    private readonly IConfigService _configService;
    private readonly Func<DateTime> _utcNow;
    private readonly Logger _logger;

    private readonly object _uploadCacheLock = new object();
    private readonly object _downloadCacheLock = new object();

    private DateTime _lastUploadRedistribution = DateTime.MinValue;
    private long[] _cachedUploadSpeeds;
    private int _cachedUploadCount;
    private long _cachedUploadMaxSpeed;
    private string _cachedUploadAlgorithm;
    private int _cachedUploadSpreadPercentage;

    private DateTime _lastDownloadRedistribution = DateTime.MinValue;
    private long[] _cachedDownloadSpeeds;
    private int _cachedDownloadCount;
    private long _cachedDownloadMaxSpeed;
    private string _cachedDownloadAlgorithm;
    private int _cachedDownloadSpreadPercentage;

    public string CurrentDistribution => _configService.UploadDistributionAlgorithm;

    public SpeedDistributionManager(
        IEnumerable<ISpeedDistributor> distributors,
        IConfigService configService,
        Func<DateTime> utcNowProvider = null)
    {
        _distributors = distributors;
        _configService = configService;
        _utcNow = utcNowProvider ?? (() => DateTime.UtcNow);
        _logger = LogManager.GetCurrentClassLogger();
    }

    public long[] DistributeSpeeds(int torrentCount)
    {
        return DistributeSpeeds(torrentCount, 0);
    }

    public long[] DistributeSpeeds(int torrentCount, long maxSpeed)
    {
        return DistributeWithConfig(
            torrentCount,
            maxSpeed,
            _configService.UploadDistributionAlgorithm,
            _configService.UploadDistributionSpreadPercentage);
    }

    public long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed)
    {
        var mode = _configService.UploadRedistributionMode;
        var intervalMinutes = _configService.UploadCustomIntervalMinutes;
        var algorithm = _configService.UploadDistributionAlgorithm;
        var spread = _configService.UploadDistributionSpreadPercentage;

        lock (_uploadCacheLock)
        {
            if (ShouldRedistribute(
                mode,
                intervalMinutes,
                _lastUploadRedistribution,
                _cachedUploadSpeeds,
                _cachedUploadCount,
                _cachedUploadMaxSpeed,
                _cachedUploadAlgorithm,
                _cachedUploadSpreadPercentage,
                torrentCount,
                maxSpeed,
                algorithm,
                spread))
            {
                _cachedUploadSpeeds = DistributeWithConfig(torrentCount, maxSpeed, algorithm, spread);
                _cachedUploadCount = torrentCount;
                _cachedUploadMaxSpeed = maxSpeed;
                _cachedUploadAlgorithm = algorithm;
                _cachedUploadSpreadPercentage = spread;
                _lastUploadRedistribution = _utcNow();
                _logger.Debug(
                    "Redistributed upload speeds using {0} (spread {1}%) across {2} torrents",
                    algorithm,
                    spread,
                    torrentCount);
            }

            return (long[])_cachedUploadSpeeds.Clone();
        }
    }

    public long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights)
    {
        return DistributeUploadSpeeds(torrentCount, maxSpeed, priorityWeights, null);
    }

    public long[] DistributeUploadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights, long[] caps)
    {
        var speeds = DistributeUploadSpeeds(torrentCount, maxSpeed);
        return ApplyPriorityWeights(speeds, priorityWeights, caps);
    }

    public long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed)
    {
        var mode = _configService.DownloadRedistributionMode;
        var intervalMinutes = _configService.DownloadCustomIntervalMinutes;
        var algorithm = _configService.DownloadDistributionAlgorithm;
        var spread = _configService.DownloadDistributionSpreadPercentage;

        lock (_downloadCacheLock)
        {
            if (ShouldRedistribute(
                mode,
                intervalMinutes,
                _lastDownloadRedistribution,
                _cachedDownloadSpeeds,
                _cachedDownloadCount,
                _cachedDownloadMaxSpeed,
                _cachedDownloadAlgorithm,
                _cachedDownloadSpreadPercentage,
                torrentCount,
                maxSpeed,
                algorithm,
                spread))
            {
                _cachedDownloadSpeeds = DistributeWithConfig(torrentCount, maxSpeed, algorithm, spread);
                _cachedDownloadCount = torrentCount;
                _cachedDownloadMaxSpeed = maxSpeed;
                _cachedDownloadAlgorithm = algorithm;
                _cachedDownloadSpreadPercentage = spread;
                _lastDownloadRedistribution = _utcNow();
                _logger.Debug(
                    "Redistributed download speeds using {0} (spread {1}%) across {2} torrents",
                    algorithm,
                    spread,
                    torrentCount);
            }

            return (long[])_cachedDownloadSpeeds.Clone();
        }
    }

    public long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights)
    {
        return DistributeDownloadSpeeds(torrentCount, maxSpeed, priorityWeights, null);
    }

    public long[] DistributeDownloadSpeeds(int torrentCount, long maxSpeed, double[] priorityWeights, long[] caps)
    {
        var speeds = DistributeDownloadSpeeds(torrentCount, maxSpeed);
        return ApplyPriorityWeights(speeds, priorityWeights, caps);
    }

    public void InvalidateCache()
    {
        lock (_uploadCacheLock)
        {
            _cachedUploadSpeeds = null;
            _cachedUploadCount = 0;
            _cachedUploadMaxSpeed = 0;
            _cachedUploadAlgorithm = null;
            _cachedUploadSpreadPercentage = 0;
            _lastUploadRedistribution = DateTime.MinValue;
        }

        lock (_downloadCacheLock)
        {
            _cachedDownloadSpeeds = null;
            _cachedDownloadCount = 0;
            _cachedDownloadMaxSpeed = 0;
            _cachedDownloadAlgorithm = null;
            _cachedDownloadSpreadPercentage = 0;
            _lastDownloadRedistribution = DateTime.MinValue;
        }
    }

    public List<string> GetAvailableDistributions()
    {
        return _distributors.Select(d => d.Name).ToList();
    }

    public static long[] ApplyPriorityWeights(
        long[] speeds,
        double[] weights,
        long[] caps = null,
        long floorBytesPerSec = MinimumFloorBytesPerSec)
    {
        if (speeds == null)
        {
            return Array.Empty<long>();
        }

        if (speeds.Length == 0)
        {
            return (long[])speeds.Clone();
        }

        if (weights == null || weights.Length != speeds.Length)
        {
            return (long[])speeds.Clone();
        }

        var totalOriginal = SumSpeeds(speeds);
        if (totalOriginal <= 0)
        {
            return (long[])speeds.Clone();
        }

        var count = speeds.Length;
        var sanitizedWeights = SanitizePriorityWeights(weights);
        var effectiveCaps = BuildEffectiveCaps(caps, count);

        var allocated = new long[count];
        var saturated = new bool[count];
        var totalFloorRequired = (long)count * floorBytesPerSec;

        if (floorBytesPerSec > 0 && totalOriginal < totalFloorRequired)
        {
            // Tier 1: If total bandwidth is less than N * floor, divide total bandwidth equally with surplus reallocation.
            var remainingBandwidth = totalOriginal;
            while (remainingBandwidth > 0)
            {
                var eligible = new List<int>();
                for (var i = 0; i < count; i++)
                {
                    if (!saturated[i] && allocated[i] < effectiveCaps[i])
                    {
                        eligible.Add(i);
                    }
                }

                if (eligible.Count == 0)
                {
                    break;
                }

                var tentativeShare = new long[count];
                var cappedAny = false;
                var equalShare = remainingBandwidth / eligible.Count;

                foreach (var i in eligible)
                {
                    tentativeShare[i] = equalShare;
                    if (allocated[i] + equalShare >= effectiveCaps[i])
                    {
                        cappedAny = true;
                    }
                }

                if (cappedAny)
                {
                    foreach (var i in eligible)
                    {
                        if (allocated[i] + tentativeShare[i] >= effectiveCaps[i])
                        {
                            var consumed = effectiveCaps[i] - allocated[i];
                            allocated[i] = effectiveCaps[i];
                            saturated[i] = true;
                            remainingBandwidth -= consumed;
                        }
                    }
                }
                else
                {
                    foreach (var i in eligible)
                    {
                        allocated[i] += tentativeShare[i];
                        remainingBandwidth -= tentativeShare[i];
                    }

                    if (remainingBandwidth > 0)
                    {
                        while (remainingBandwidth > 0)
                        {
                            var allocatedAnyRemainder = false;
                            foreach (var idx in eligible)
                            {
                                if (allocated[idx] < effectiveCaps[idx])
                                {
                                    allocated[idx]++;
                                    remainingBandwidth--;
                                    allocatedAnyRemainder = true;
                                    if (remainingBandwidth == 0)
                                    {
                                        break;
                                    }
                                }
                            }

                            if (!allocatedAnyRemainder)
                            {
                                break;
                            }
                        }
                    }

                    break;
                }
            }

            return allocated;
        }

        var effectiveFloor = floorBytesPerSec > 0
            ? floorBytesPerSec
            : (totalOriginal >= (long)count * MinimumTransmissionQuantum
                ? MinimumTransmissionQuantum
                : (totalOriginal >= count ? Math.Max(1L, totalOriginal / count) : 0L));

        // Tier 1: Guarantee minimum floor or transmission quantum to all active torrents to prevent peer starvation.
        for (var i = 0; i < count; i++)
        {
            var floorToApply = Math.Min(effectiveCaps[i], effectiveFloor);
            allocated[i] = floorToApply;
            if (allocated[i] >= effectiveCaps[i])
            {
                saturated[i] = true;
            }
        }

        var allocatedSoFar = 0L;
        for (var i = 0; i < count; i++)
        {
            allocatedSoFar += allocated[i];
        }

        var spareBandwidth = totalOriginal - allocatedSoFar;

        // Tier 2: Distribute remaining spare bandwidth proportionally based on priority weights with Largest-Remainder (Hamilton-Hare) allocation.
        while (spareBandwidth > 0)
        {
            var eligible = new List<int>();
            for (var i = 0; i < count; i++)
            {
                if (!saturated[i] && allocated[i] < effectiveCaps[i])
                {
                    eligible.Add(i);
                }
            }

            if (eligible.Count == 0)
            {
                break;
            }

            var activeWeightSum = 0.0;
            foreach (var i in eligible)
            {
                activeWeightSum += sanitizedWeights[i];
            }

            if (activeWeightSum <= 0.0)
            {
                foreach (var i in eligible)
                {
                    sanitizedWeights[i] = 1.0;
                }

                activeWeightSum = eligible.Count;
            }

            var exactShares = new double[count];
            var tentativeShare = new long[count];
            var cappedAny = false;

            foreach (var i in eligible)
            {
                var exact = spareBandwidth * (sanitizedWeights[i] / activeWeightSum);
                exactShares[i] = exact;
                var share = (long)exact;
                tentativeShare[i] = share;
                if (allocated[i] + share >= effectiveCaps[i])
                {
                    cappedAny = true;
                }
            }

            if (cappedAny)
            {
                foreach (var i in eligible)
                {
                    if (allocated[i] + tentativeShare[i] >= effectiveCaps[i])
                    {
                        var consumed = effectiveCaps[i] - allocated[i];
                        allocated[i] = effectiveCaps[i];
                        saturated[i] = true;
                        spareBandwidth -= consumed;
                    }
                }
            }
            else
            {
                var allocatedInRound = 0L;
                foreach (var i in eligible)
                {
                    allocated[i] += tentativeShare[i];
                    allocatedInRound += tentativeShare[i];
                }

                spareBandwidth -= allocatedInRound;

                if (spareBandwidth > 0)
                {
                    var sortedEligible = eligible
                        .OrderByDescending(i => exactShares[i] - tentativeShare[i])
                        .ThenByDescending(i => sanitizedWeights[i])
                        .ThenBy(i => i)
                        .ToList();

                    while (spareBandwidth > 0)
                    {
                        var allocatedAnyRemainder = false;
                        foreach (var idx in sortedEligible)
                        {
                            if (allocated[idx] < effectiveCaps[idx])
                            {
                                allocated[idx]++;
                                spareBandwidth--;
                                allocatedAnyRemainder = true;
                                if (spareBandwidth == 0)
                                {
                                    break;
                                }
                            }
                        }

                        if (!allocatedAnyRemainder)
                        {
                            break;
                        }
                    }
                }

                break;
            }
        }

        return allocated;
    }

    private static long SumSpeeds(long[] speeds)
    {
        var totalOriginal = 0L;
        for (var i = 0; i < speeds.Length; i++)
        {
            totalOriginal += speeds[i];
        }

        return totalOriginal;
    }

    private static double[] SanitizePriorityWeights(double[] weights)
    {
        var count = weights.Length;
        var sanitizedWeights = new double[count];
        var totalWeight = 0.0;
        for (var i = 0; i < count; i++)
        {
            var w = weights[i];
            double sanitizedWeight;
            if (double.IsNaN(w) || double.IsInfinity(w) || w <= 0.0)
            {
                sanitizedWeight = 1.0;
            }
            else
            {
                sanitizedWeight = Math.Clamp(w, 0.01, 100.0);
            }

            sanitizedWeights[i] = sanitizedWeight;
            totalWeight += sanitizedWeight;
        }

        if (double.IsNaN(totalWeight) || double.IsInfinity(totalWeight) || totalWeight <= 0.0)
        {
            for (var i = 0; i < count; i++)
            {
                sanitizedWeights[i] = 1.0;
            }
        }

        return sanitizedWeights;
    }

    private static long[] BuildEffectiveCaps(long[] caps, int count)
    {
        var effectiveCaps = new long[count];
        for (var i = 0; i < count; i++)
        {
            if (caps != null && i < caps.Length && caps[i] > 0)
            {
                effectiveCaps[i] = caps[i];
            }
            else
            {
                effectiveCaps[i] = long.MaxValue;
            }
        }

        return effectiveCaps;
    }

    private long[] DistributeWithConfig(int torrentCount, long maxSpeed, string algorithm, int spreadPercentage)
    {
        var distributor = _distributors.FirstOrDefault(d =>
                string.Equals(d.Name, algorithm, StringComparison.OrdinalIgnoreCase))
            ?? _distributors.First();

        var effectiveSpeed = maxSpeed > 0 ? maxSpeed : DefaultBytesPerSecond;
        var speeds = distributor.Distribute(effectiveSpeed, torrentCount);

        if (spreadPercentage < 100 && torrentCount > 0)
        {
            var equalShare = (double)effectiveSpeed / torrentCount;
            var spreadFactor = spreadPercentage / 100.0;
            var exactShares = new double[torrentCount];

            for (var i = 0; i < torrentCount; i++)
            {
                var exact = equalShare + ((speeds[i] - equalShare) * spreadFactor);
                exactShares[i] = exact;
                speeds[i] = (long)exact;
            }

            var allocated = 0L;
            for (var i = 0; i < torrentCount; i++)
            {
                allocated += speeds[i];
            }

            var remainder = effectiveSpeed - allocated;
            if (remainder > 0)
            {
                var sortedIndices = Enumerable.Range(0, torrentCount)
                    .OrderByDescending(i => exactShares[i] - speeds[i])
                    .ThenBy(i => i)
                    .ToList();

                for (var r = 0; r < remainder; r++)
                {
                    speeds[sortedIndices[r % torrentCount]]++;
                }
            }
        }

        return speeds;
    }

    private bool ShouldRedistribute(
        string mode,
        int intervalMinutes,
        DateTime lastRedistribution,
        long[] cached,
        int cachedCount,
        long cachedMaxSpeed,
        string cachedAlgorithm,
        int cachedSpreadPercentage,
        int currentCount,
        long currentMaxSpeed,
        string currentAlgorithm,
        int currentSpreadPercentage)
    {
        if (cached == null ||
            cachedCount != currentCount ||
            cachedMaxSpeed != currentMaxSpeed ||
            !string.Equals(cachedAlgorithm, currentAlgorithm, StringComparison.OrdinalIgnoreCase) ||
            cachedSpreadPercentage != currentSpreadPercentage)
        {
            return true;
        }

        return mode switch
        {
            "tick" => true,
            "interval" => _utcNow() - lastRedistribution >= TimeSpan.FromMinutes(intervalMinutes),
            "fixed" => false,
            _ => true
        };
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Network.Blocklist;

public class P2PDatBlocklistProvider : IBlocklistProvider
{
    private readonly Logger _logger;
    private readonly object _lock = new();

    private List<IpRange> _ranges = new();
    private int _ruleCount;

    public string ProviderId => "P2PDat";
    public string DisplayName => "PeerGuardian / eMule (.p2p / .dat Range Filter)";
    public string Version => "1.0";
    public bool IsAvailable => true;
    public BlocklistCapabilities Capabilities => BlocklistCapabilities.IPv4 | BlocklistCapabilities.P2PDat | BlocklistCapabilities.LiveAutoRefresh;
    public int RuleCount => _ruleCount;

    public P2PDatBlocklistProvider()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public Task<BlocklistHealthResult> ProbeHealthAsync()
    {
        return Task.FromResult(new BlocklistHealthResult
        {
            IsHealthy = true,
            StatusMessage = $"P2P Range Filter operational with {_ruleCount} IP ranges loaded.",
            LoadedRuleCount = _ruleCount
        });
    }

    public bool IsIpBlocked(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        if (!IPAddress.TryParse(ipAddress, out var parsedIp) || parsedIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var ipBytes = parsedIp.GetAddressBytes();
        var ipNum = ((uint)ipBytes[0] << 24) | ((uint)ipBytes[1] << 16) | ((uint)ipBytes[2] << 8) | ipBytes[3];

        List<IpRange> snapshot;
        lock (_lock)
        {
            snapshot = _ranges;
        }

        if (snapshot.Count == 0)
        {
            return false;
        }

        var low = 0;
        var high = snapshot.Count - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var range = snapshot[mid];

            if (ipNum >= range.Start && ipNum <= range.End)
            {
                return true;
            }

            if (ipNum < range.Start)
            {
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return false;
    }

    public Task<int> LoadRulesAsync(IEnumerable<string> rules)
    {
        if (rules == null)
        {
            return Task.FromResult(0);
        }

        var parsedRanges = new List<IpRange>();

        foreach (var rawLine in rules)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var line = rawLine.Trim();
            if (line.StartsWith('#') || line.StartsWith("//") || line.StartsWith(';'))
            {
                continue;
            }

            if (TryParseP2PLine(line, out var range))
            {
                parsedRanges.Add(range);
            }
        }

        parsedRanges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = MergeRanges(parsedRanges);

        lock (_lock)
        {
            _ranges = merged;
            _ruleCount = merged.Count;
        }

        _logger.Info("Loaded and merged {0} IP ranges into P2P blocklist.", merged.Count);
        return Task.FromResult(merged.Count);
    }

    public void ClearRules()
    {
        lock (_lock)
        {
            _ranges = new List<IpRange>();
            _ruleCount = 0;
        }
    }

    private static bool TryParseP2PLine(string line, out IpRange range)
    {
        range = default;
        var name = string.Empty;
        var rangePart = line;

        var colonIdx = line.LastIndexOf(':');
        if (colonIdx >= 0 && !line.Contains("::"))
        {
            name = line[..colonIdx].Trim();
            rangePart = line[(colonIdx + 1)..].Trim();
        }

        var hyphenIdx = rangePart.IndexOf('-');
        if (hyphenIdx >= 0)
        {
            var startStr = rangePart[..hyphenIdx].Trim();
            var endStr = rangePart[(hyphenIdx + 1)..].Trim();

            if (IPAddress.TryParse(startStr, out var startIp) && IPAddress.TryParse(endStr, out var endIp) &&
                startIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                endIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var sBytes = startIp.GetAddressBytes();
                var eBytes = endIp.GetAddressBytes();

                var startNum = ((uint)sBytes[0] << 24) | ((uint)sBytes[1] << 16) | ((uint)sBytes[2] << 8) | sBytes[3];
                var endNum = ((uint)eBytes[0] << 24) | ((uint)eBytes[1] << 16) | ((uint)eBytes[2] << 8) | eBytes[3];

                if (startNum <= endNum)
                {
                    range = new IpRange(startNum, endNum, name);
                    return true;
                }
            }

            return false;
        }

        if (IPAddress.TryParse(rangePart, out var singleIp) && singleIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = singleIp.GetAddressBytes();
            var num = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            range = new IpRange(num, num, name);
            return true;
        }

        return false;
    }

    private static List<IpRange> MergeRanges(List<IpRange> sorted)
    {
        if (sorted.Count <= 1)
        {
            return sorted;
        }

        var result = new List<IpRange>();
        var current = sorted[0];

        for (var i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            if (next.Start <= current.End + 1)
            {
                current = new IpRange(current.Start, Math.Max(current.End, next.End), current.Name);
            }
            else
            {
                result.Add(current);
                current = next;
            }
        }

        result.Add(current);
        return result;
    }

    private readonly struct IpRange
    {
        public uint Start { get; }
        public uint End { get; }
        public string Name { get; }

        public IpRange(uint start, uint end, string name)
        {
            Start = start;
            End = end;
            Name = name;
        }
    }
}

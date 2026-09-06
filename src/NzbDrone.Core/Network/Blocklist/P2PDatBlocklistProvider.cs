// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Network.Blocklist;

public class P2PDatBlocklistProvider : IBlocklistProvider
{
    private readonly Logger logger;
    private readonly object @lock = new();

    private List<IpRange> ranges = new();
    private int ruleCount;

    public string ProviderId => "P2PDat";

    public string DisplayName => "PeerGuardian / eMule (.p2p / .dat Range Filter)";

    public string Version => "1.0";

    public bool IsAvailable => true;

    public BlocklistCapabilities Capabilities => BlocklistCapabilities.IPv4 | BlocklistCapabilities.P2PDat | BlocklistCapabilities.LiveAutoRefresh;

    public int RuleCount => this.ruleCount;

    public P2PDatBlocklistProvider()
    {
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task<BlocklistHealthResult> ProbeHealthAsync()
    {
        return Task.FromResult(new BlocklistHealthResult
        {
            IsHealthy = true,
            StatusMessage = $"P2P Range Filter operational with {this.ruleCount} IP ranges loaded.",
            LoadedRuleCount = this.ruleCount,
        });
    }

    public bool IsIpBlocked(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        if (!IPAddress.TryParse(ipAddress, out var parsedIp))
        {
            return false;
        }

        if (parsedIp.IsIPv4MappedToIPv6)
        {
            parsedIp = parsedIp.MapToIPv4();
        }

        if (parsedIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var ipBytes = parsedIp.GetAddressBytes();
        var ipNum = ((uint)ipBytes[0] << 24) | ((uint)ipBytes[1] << 16) | ((uint)ipBytes[2] << 8) | ipBytes[3];

        List<IpRange> snapshot;
        lock (this.@lock)
        {
            snapshot = this.ranges;
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

            var line = StripComments(rawLine);
            if (string.IsNullOrWhiteSpace(line))
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

        lock (this.@lock)
        {
            this.ranges = merged;
            this.ruleCount = merged.Count;
        }

        this.logger.Info("Loaded and merged {0} IP ranges into P2P blocklist.", merged.Count);
        return Task.FromResult(merged.Count);
    }

    public void ClearRules()
    {
        lock (this.@lock)
        {
            this.ranges = new List<IpRange>();
            this.ruleCount = 0;
        }
    }

    private static string StripComments(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var commentIdx = -1;
        var hashIdx = line.IndexOf('#');
        var slashSlashIdx = line.IndexOf("//", System.StringComparison.Ordinal);
        var semiIdx = line.IndexOf(';');

        if (hashIdx >= 0)
        {
            commentIdx = hashIdx;
        }

        if (slashSlashIdx >= 0 && (commentIdx < 0 || slashSlashIdx < commentIdx))
        {
            commentIdx = slashSlashIdx;
        }

        if (semiIdx >= 0 && (commentIdx < 0 || semiIdx < commentIdx))
        {
            commentIdx = semiIdx;
        }

        if (commentIdx >= 0)
        {
            line = line[..commentIdx];
        }

        return line.Trim();
    }

    private static bool TryParseP2PLine(string line, out IpRange range)
    {
        range = default;
        line = StripComments(line);
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var name = string.Empty;

        var hyphenIdx = line.IndexOf('-');
        if (hyphenIdx >= 0)
        {
            var startCandidate = line[..hyphenIdx].Trim();
            var endStr = line[(hyphenIdx + 1)..].Trim();
            var startStr = startCandidate;

            if (!IPAddress.TryParse(startCandidate, out var startIp))
            {
                var colonIdx = startCandidate.IndexOf(':');
                if (colonIdx >= 0)
                {
                    name = startCandidate[..colonIdx].Trim();
                    startStr = startCandidate[(colonIdx + 1)..].Trim();
                    if (!IPAddress.TryParse(startStr, out startIp))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            if (!IPAddress.TryParse(endStr, out var endIp))
            {
                var lastColon = endStr.LastIndexOf(':');
                if (lastColon >= 0)
                {
                    var candidateEnd = endStr[..lastColon].Trim();
                    if (!IPAddress.TryParse(candidateEnd, out endIp))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            if (startIp.IsIPv4MappedToIPv6)
            {
                startIp = startIp.MapToIPv4();
            }

            if (endIp.IsIPv4MappedToIPv6)
            {
                endIp = endIp.MapToIPv4();
            }

            if (startIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
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

        var singleCandidate = line;
        if (!IPAddress.TryParse(singleCandidate, out var singleIp))
        {
            var colonIdx = singleCandidate.IndexOf(':');
            if (colonIdx >= 0)
            {
                name = singleCandidate[..colonIdx].Trim();
                singleCandidate = singleCandidate[(colonIdx + 1)..].Trim();
                if (!IPAddress.TryParse(singleCandidate, out singleIp))
                {
                    var lastColon = singleCandidate.LastIndexOf(':');
                    if (lastColon >= 0)
                    {
                        var candidateSingle = singleCandidate[..lastColon].Trim();
                        if (!IPAddress.TryParse(candidateSingle, out singleIp))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
        }

        if (singleIp.IsIPv4MappedToIPv6)
        {
            singleIp = singleIp.MapToIPv4();
        }

        if (singleIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
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
            if (current.End == uint.MaxValue || next.Start <= current.End + 1)
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
            this.Start = start;
            this.End = end;
            this.Name = name;
        }
    }
}

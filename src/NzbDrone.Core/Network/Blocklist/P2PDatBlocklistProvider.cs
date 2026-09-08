// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Network.Blocklist;

public class P2PDatBlocklistProvider : IBlocklistProvider
{
    private readonly Logger logger;
    private readonly object @lock = new();

    private List<IpRangeV4> v4Ranges = new();
    private List<IpRangeV6> v6Ranges = new();
    private int ruleCount;

    public string ProviderId => "P2PDat";

    public string DisplayName => "PeerGuardian / eMule (.p2p / .dat Range Filter)";

    public string Version => "1.0";

    public bool IsAvailable => true;

    public BlocklistCapabilities Capabilities => BlocklistCapabilities.IPv4 | BlocklistCapabilities.IPv6 | BlocklistCapabilities.Cidr | BlocklistCapabilities.P2PDat | BlocklistCapabilities.LiveAutoRefresh;

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

        if (parsedIp.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> ipBytes = stackalloc byte[4];
            if (!parsedIp.TryWriteBytes(ipBytes, out _))
            {
                return false;
            }

            var ipNum = ((uint)ipBytes[0] << 24) | ((uint)ipBytes[1] << 16) | ((uint)ipBytes[2] << 8) | ipBytes[3];

            List<IpRangeV4> snapshot;
            lock (this.@lock)
            {
                snapshot = this.v4Ranges;
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

        if (parsedIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Span<byte> ipBytes = stackalloc byte[16];
            if (!parsedIp.TryWriteBytes(ipBytes, out _))
            {
                return false;
            }

            var ipNum = BinaryPrimitives.ReadUInt128BigEndian(ipBytes);

            List<IpRangeV6> snapshot;
            lock (this.@lock)
            {
                snapshot = this.v6Ranges;
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

        return false;
    }

    public Task<int> LoadRulesAsync(IEnumerable<string> rules)
    {
        if (rules == null)
        {
            return Task.FromResult(0);
        }

        var parsedV4Ranges = new List<IpRangeV4>();
        var parsedV6Ranges = new List<IpRangeV6>();

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

            if (TryParseP2PLine(line, parsedV4Ranges, parsedV6Ranges))
            {
            }
        }

        parsedV4Ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var mergedV4 = MergeRangesV4(parsedV4Ranges);

        parsedV6Ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var mergedV6 = MergeRangesV6(parsedV6Ranges);

        var totalCount = mergedV4.Count + mergedV6.Count;

        lock (this.@lock)
        {
            this.v4Ranges = mergedV4;
            this.v6Ranges = mergedV6;
            this.ruleCount = totalCount;
        }

        this.logger.Info("Loaded and merged {0} IP ranges ({1} IPv4, {2} IPv6) into P2P blocklist.", totalCount, mergedV4.Count, mergedV6.Count);
        return Task.FromResult(totalCount);
    }

    public void ClearRules()
    {
        lock (this.@lock)
        {
            this.v4Ranges = new List<IpRangeV4>();
            this.v6Ranges = new List<IpRangeV6>();
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
        var slashSlashIdx = line.IndexOf("//", StringComparison.Ordinal);
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

    private static bool TryParseP2PLine(string line, List<IpRangeV4> v4List, List<IpRangeV6> v6List)
    {
        line = StripComments(line);
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var hyphenIdx = line.IndexOf('-');
        if (hyphenIdx >= 0)
        {
            var leftCandidate = line[..hyphenIdx].Trim();
            var rightCandidate = line[(hyphenIdx + 1)..].Trim();

            if (!TryExtractStartIp(leftCandidate, out var startIp, out var name) ||
                !TryExtractEndIp(rightCandidate, out var endIp))
            {
                return false;
            }

            if (startIp.IsIPv4MappedToIPv6)
            {
                startIp = startIp.MapToIPv4();
            }

            if (endIp.IsIPv4MappedToIPv6)
            {
                endIp = endIp.MapToIPv4();
            }

            if (startIp.AddressFamily == AddressFamily.InterNetwork && endIp.AddressFamily == AddressFamily.InterNetwork)
            {
                Span<byte> sBytes = stackalloc byte[4];
                Span<byte> eBytes = stackalloc byte[4];
                if (!startIp.TryWriteBytes(sBytes, out _) || !endIp.TryWriteBytes(eBytes, out _))
                {
                    return false;
                }

                var startNum = ((uint)sBytes[0] << 24) | ((uint)sBytes[1] << 16) | ((uint)sBytes[2] << 8) | sBytes[3];
                var endNum = ((uint)eBytes[0] << 24) | ((uint)eBytes[1] << 16) | ((uint)eBytes[2] << 8) | eBytes[3];

                if (startNum <= endNum)
                {
                    v4List.Add(new IpRangeV4(startNum, endNum, name));
                    return true;
                }

                return false;
            }

            if (startIp.AddressFamily == AddressFamily.InterNetworkV6 && endIp.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Span<byte> sBytes = stackalloc byte[16];
                Span<byte> eBytes = stackalloc byte[16];
                if (!startIp.TryWriteBytes(sBytes, out _) || !endIp.TryWriteBytes(eBytes, out _))
                {
                    return false;
                }

                var startNum = BinaryPrimitives.ReadUInt128BigEndian(sBytes);
                var endNum = BinaryPrimitives.ReadUInt128BigEndian(eBytes);

                if (startNum <= endNum)
                {
                    v6List.Add(new IpRangeV6(startNum, endNum, name));
                    return true;
                }

                return false;
            }

            return false;
        }

        if (TryExtractIpOrCidr(line, out var singleIp, out var prefixLength, out var singleName))
        {
            if (singleIp.IsIPv4MappedToIPv6)
            {
                singleIp = singleIp.MapToIPv4();
                if (prefixLength > 32)
                {
                    prefixLength = Math.Clamp(prefixLength - 96, 0, 32);
                }
            }

            if (singleIp.AddressFamily == AddressFamily.InterNetwork)
            {
                Span<byte> bytes = stackalloc byte[4];
                if (!singleIp.TryWriteBytes(bytes, out _))
                {
                    return false;
                }

                var ipNum = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                var mask = prefixLength == 0 ? 0 : (uint.MaxValue << (32 - prefixLength));
                var start = ipNum & mask;
                var end = start | ~mask;

                v4List.Add(new IpRangeV4(start, end, singleName));
                return true;
            }

            if (singleIp.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Span<byte> bytes = stackalloc byte[16];
                if (!singleIp.TryWriteBytes(bytes, out _))
                {
                    return false;
                }

                var ipNum = BinaryPrimitives.ReadUInt128BigEndian(bytes);
                var mask = prefixLength == 0 ? UInt128.Zero : (UInt128.MaxValue << (128 - prefixLength));
                var start = ipNum & mask;
                var end = start | ~mask;

                v6List.Add(new IpRangeV6(start, end, singleName));
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractStartIp(string candidate, out IPAddress startIp, out string name)
    {
        startIp = null;
        name = string.Empty;

        candidate = candidate.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        if (TryParseStrictIp(candidate, out startIp))
        {
            return true;
        }

        for (var i = 0; i < candidate.Length; i++)
        {
            if (candidate[i] == ':')
            {
                var subCandidate = candidate[(i + 1)..].Trim();
                if (TryParseStrictIp(subCandidate, out startIp))
                {
                    name = candidate[..i].Trim();
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryExtractEndIp(string candidate, out IPAddress endIp)
    {
        endIp = null;

        candidate = candidate.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var lastColon = candidate.LastIndexOf(':');
        if (lastColon >= 0 && lastColon < candidate.Length - 1)
        {
            var levelStr = candidate[(lastColon + 1)..].Trim();
            if (int.TryParse(levelStr, out _) && !levelStr.Contains('.') && !candidate[..lastColon].EndsWith(':'))
            {
                var withoutLevel = candidate[..lastColon].Trim();
                if (TryParseStrictIp(withoutLevel, out endIp))
                {
                    return true;
                }
            }
        }

        if (TryParseStrictIp(candidate, out endIp))
        {
            return true;
        }

        return false;
    }

    private static bool TryExtractIpOrCidr(string candidate, out IPAddress ip, out int prefixLength, out string name)
    {
        ip = null;
        prefixLength = 0;
        name = string.Empty;

        candidate = candidate.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var lastColon = candidate.LastIndexOf(':');
        if (lastColon >= 0 && lastColon < candidate.Length - 1)
        {
            var levelStr = candidate[(lastColon + 1)..].Trim();
            if (int.TryParse(levelStr, out _) && !levelStr.Contains('.') && !candidate[..lastColon].EndsWith(':'))
            {
                var withoutLevel = candidate[..lastColon].Trim();
                if (TryExtractIpOrCidrCore(withoutLevel, out ip, out prefixLength, out name))
                {
                    return true;
                }
            }
        }

        return TryExtractIpOrCidrCore(candidate, out ip, out prefixLength, out name);
    }

    private static bool TryExtractIpOrCidrCore(string candidate, out IPAddress ip, out int prefixLength, out string name)
    {
        ip = null;
        prefixLength = 0;
        name = string.Empty;

        candidate = candidate.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        if (TryParseCidrOrIp(candidate, out ip, out prefixLength))
        {
            return true;
        }

        for (var i = 0; i < candidate.Length; i++)
        {
            if (candidate[i] == ':')
            {
                var subCandidate = candidate[(i + 1)..].Trim();
                if (TryParseCidrOrIp(subCandidate, out ip, out prefixLength))
                {
                    name = candidate[..i].Trim();
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseStrictIp(string str, out IPAddress ip)
    {
        ip = null;
        if (string.IsNullOrWhiteSpace(str))
        {
            return false;
        }

        if (IPAddress.TryParse(str, out ip))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && str.Contains('.'))
            {
                return true;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6 && str.Contains(':'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseCidrOrIp(string str, out IPAddress ip, out int prefixLength)
    {
        ip = null;
        prefixLength = 0;

        if (string.IsNullOrWhiteSpace(str))
        {
            return false;
        }

        var slashIdx = str.IndexOf('/');
        if (slashIdx >= 0)
        {
            var ipStr = str[..slashIdx].Trim();
            var lenStr = str[(slashIdx + 1)..].Trim();

            if (TryParseStrictIp(ipStr, out ip) && int.TryParse(lenStr, out prefixLength))
            {
                if (ip.IsIPv4MappedToIPv6)
                {
                    ip = ip.MapToIPv4();
                    if (prefixLength > 32)
                    {
                        prefixLength = Math.Clamp(prefixLength - 96, 0, 32);
                    }
                }

                var maxLen = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
                return prefixLength >= 0 && prefixLength <= maxLen;
            }

            return false;
        }

        if (TryParseStrictIp(str, out ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            prefixLength = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            return true;
        }

        return false;
    }

    private static List<IpRangeV4> MergeRangesV4(List<IpRangeV4> sorted)
    {
        if (sorted.Count <= 1)
        {
            return sorted;
        }

        var result = new List<IpRangeV4>();
        var current = sorted[0];

        for (var i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            if (current.End == uint.MaxValue || next.Start <= current.End + 1)
            {
                current = new IpRangeV4(current.Start, Math.Max(current.End, next.End), current.Name);
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

    private static List<IpRangeV6> MergeRangesV6(List<IpRangeV6> sorted)
    {
        if (sorted.Count <= 1)
        {
            return sorted;
        }

        var result = new List<IpRangeV6>();
        var current = sorted[0];

        for (var i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            if (current.End == UInt128.MaxValue || next.Start <= current.End + 1)
            {
                current = new IpRangeV6(current.Start, current.End > next.End ? current.End : next.End, current.Name);
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

    private readonly struct IpRangeV4
    {
        public uint Start { get; }

        public uint End { get; }

        public string Name { get; }

        public IpRangeV4(uint start, uint end, string name)
        {
            this.Start = start;
            this.End = end;
            this.Name = name;
        }
    }

    private readonly struct IpRangeV6
    {
        public UInt128 Start { get; }

        public UInt128 End { get; }

        public string Name { get; }

        public IpRangeV6(UInt128 start, UInt128 end, string name)
        {
            this.Start = start;
            this.End = end;
            this.Name = name;
        }
    }
}

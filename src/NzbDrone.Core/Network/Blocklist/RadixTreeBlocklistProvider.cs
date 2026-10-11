// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Network.Blocklist;

public class RadixTreeBlocklistProvider : IBlocklistProvider
{
    private readonly Logger logger;
    private readonly object @lock = new();

    private RadixNode ipv4Root = new();
    private RadixNode ipv6Root = new();
    private int ruleCount;

    public string ProviderId => "RadixTree";

    public string DisplayName => "Managed Radix Trie (CIDR IPv4/IPv6)";

    public string Version => "1.0";

    public bool IsAvailable => true;

    public BlocklistCapabilities Capabilities => BlocklistCapabilities.IPv4 | BlocklistCapabilities.IPv6 | BlocklistCapabilities.Cidr | BlocklistCapabilities.LiveAutoRefresh;

    public int RuleCount => this.ruleCount;

    public RadixTreeBlocklistProvider()
    {
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task<BlocklistHealthResult> ProbeHealthAsync()
    {
        return Task.FromResult(new BlocklistHealthResult
        {
            IsHealthy = true,
            StatusMessage = $"Radix Trie filter operational with {this.ruleCount} active CIDR/IP rules.",
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
            return this.IsIpv4Blocked(parsedIp);
        }

        if (parsedIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return this.IsIpv6Blocked(parsedIp);
        }

        return false;
    }

    public Task<int> LoadRulesAsync(IEnumerable<string> rules)
    {
        if (rules == null)
        {
            return Task.FromResult(0);
        }

        var newIpv4Root = new RadixNode();
        var newIpv6Root = new RadixNode();
        var added = 0;

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

            if (TryParseRangeLine(line, newIpv4Root, newIpv6Root))
            {
                added++;
                continue;
            }

            if (!TryExtractCidrRule(line, out var ip, out var prefixLength))
            {
                continue;
            }

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                InsertIpv4(newIpv4Root, ip, prefixLength);
                added++;
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                InsertIpv6(newIpv6Root, ip, prefixLength);
                added++;
            }
        }

        lock (this.@lock)
        {
            Volatile.Write(ref this.ipv4Root, newIpv4Root);
            Volatile.Write(ref this.ipv6Root, newIpv6Root);
            this.ruleCount = added;
        }

        this.logger.Info("Loaded {0} IP blocklist rules into Radix Trie.", added);
        return Task.FromResult(added);
    }

    public void ClearRules()
    {
        lock (this.@lock)
        {
            Volatile.Write(ref this.ipv4Root, new RadixNode());
            Volatile.Write(ref this.ipv6Root, new RadixNode());
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

    private static bool TryExtractCidrRule(string line, out IPAddress ip, out int prefixLength)
    {
        ip = null;
        prefixLength = 0;
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var lastColon = line.LastIndexOf(':');
        if (lastColon >= 0 && lastColon < line.Length - 1)
        {
            var levelStr = line[(lastColon + 1)..].Trim();
            if (int.TryParse(levelStr, out _) && !levelStr.Contains('.') && !line[..lastColon].EndsWith(':'))
            {
                line = line[..lastColon].Trim();
            }
        }

        if (TryParseCidr(line, out ip, out prefixLength))
        {
            return true;
        }

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == ':')
            {
                var candidate = line[(i + 1)..].Trim();
                if (TryParseCidr(candidate, out ip, out prefixLength))
                {
                    if (ip.AddressFamily == AddressFamily.InterNetworkV6 && prefixLength == 128 && !candidate.Contains('/') && !line.StartsWith('['))
                    {
                        ip = null;
                        prefixLength = 0;
                        continue;
                    }

                    return true;
                }
            }
        }

        return false;
    }

    private bool IsIpv4Blocked(IPAddress ip)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!ip.TryWriteBytes(bytes, out _))
        {
            return false;
        }

        var ipNum = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

        var current = Volatile.Read(ref this.ipv4Root);
        for (var i = 31; i >= 0; i--)
        {
            if (current.IsBlocked)
            {
                return true;
            }

            var bit = (ipNum >> i) & 1;
            current = bit == 0 ? current.Zero : current.One;

            if (current == null)
            {
                return false;
            }
        }

        return current.IsBlocked;
    }

    private bool IsIpv6Blocked(IPAddress ip)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!ip.TryWriteBytes(bytes, out _))
        {
            return false;
        }

        var current = Volatile.Read(ref this.ipv6Root);
        for (var bitIndex = 0; bitIndex < 128; bitIndex++)
        {
            if (current.IsBlocked)
            {
                return true;
            }

            var byteIndex = bitIndex / 8;
            var bitInByte = 7 - (bitIndex % 8);
            var bit = (bytes[byteIndex] >> bitInByte) & 1;

            current = bit == 0 ? current.Zero : current.One;

            if (current == null)
            {
                return false;
            }
        }

        return current.IsBlocked;
    }

    internal (int Ipv4Nodes, int Ipv6Nodes) GetNodeCounts()
    {
        lock (this.@lock)
        {
            return (CountNodes(this.ipv4Root), CountNodes(this.ipv6Root));
        }
    }

    private static int CountNodes(RadixNode node)
    {
        if (node == null)
        {
            return 0;
        }

        return 1 + CountNodes(node.Zero) + CountNodes(node.One);
    }

    private static void InsertIpv4(RadixNode root, IPAddress ip, int prefixLength)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (!ip.TryWriteBytes(bytes, out _))
        {
            return;
        }

        var ipNum = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

        var current = root;
        for (var i = 31; i >= 32 - prefixLength; i--)
        {
            if (current.IsBlocked)
            {
                return;
            }

            var bit = (ipNum >> i) & 1;
            if (bit == 0)
            {
                current.Zero ??= new RadixNode();
                current = current.Zero;
            }
            else
            {
                current.One ??= new RadixNode();
                current = current.One;
            }
        }

        current.IsBlocked = true;
        current.Zero = null;
        current.One = null;
    }

    private static void InsertIpv6(RadixNode root, IPAddress ip, int prefixLength)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!ip.TryWriteBytes(bytes, out _))
        {
            return;
        }

        var current = root;
        for (var bitIndex = 0; bitIndex < prefixLength; bitIndex++)
        {
            if (current.IsBlocked)
            {
                return;
            }

            var byteIndex = bitIndex / 8;
            var bitInByte = 7 - (bitIndex % 8);
            var bit = (bytes[byteIndex] >> bitInByte) & 1;

            if (bit == 0)
            {
                current.Zero ??= new RadixNode();
                current = current.Zero;
            }
            else
            {
                current.One ??= new RadixNode();
                current = current.One;
            }
        }

        current.IsBlocked = true;
        current.Zero = null;
        current.One = null;
    }

    private static bool TryParseCidr(string cidr, out IPAddress ip, out int prefixLength)
    {
        ip = null;
        prefixLength = 0;

        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        var slashIdx = cidr.IndexOf('/');
        if (slashIdx >= 0)
        {
            var ipStr = cidr[..slashIdx].Trim();
            var lenStr = cidr[(slashIdx + 1)..].Trim();

            if (IPAddress.TryParse(ipStr, out ip) && int.TryParse(lenStr, out prefixLength))
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

        if (IPAddress.TryParse(cidr, out ip))
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

    private static bool TryParseRangeLine(string line, RadixNode ipv4Root, RadixNode ipv6Root)
    {
        var hyphenIdx = line.IndexOf('-');
        if (hyphenIdx < 0)
        {
            return false;
        }

        var leftCandidate = line[..hyphenIdx].Trim();
        var rightCandidate = line[(hyphenIdx + 1)..].Trim();

        if (!TryExtractStartIp(leftCandidate, out var startIp, out _) ||
            !TryExtractEndIp(rightCandidate, startIp, out var endIp))
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

            if (startNum > endNum)
            {
                return false;
            }

            InsertIpv4Range(ipv4Root, startNum, endNum);
            return true;
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

            if (startNum > endNum)
            {
                return false;
            }

            InsertIpv6Range(ipv6Root, startNum, endNum);
            return true;
        }

        return false;
    }

    private static void InsertIpv4Range(RadixNode root, uint start, uint end)
    {
        while (start <= end)
        {
            var maxSize = LargestAlignedCidrSize(start);
            var remaining = end - start + 1;
            while (maxSize > remaining)
            {
                maxSize >>= 1;
            }

            var prefixLength = 32 - BitOperations.Log2(maxSize);
            var ip = new IPAddress(new byte[] { (byte)(start >> 24), (byte)(start >> 16), (byte)(start >> 8), (byte)start });
            InsertIpv4(root, ip, prefixLength);
            start += maxSize;
        }
    }

    private static void InsertIpv6Range(RadixNode root, UInt128 start, UInt128 end)
    {
        Span<byte> bytes = stackalloc byte[16];
        while (start <= end)
        {
            var maxSize = LargestAlignedCidrSizeV6(start);
            var remaining = end - start + 1;
            while (maxSize > remaining)
            {
                maxSize >>= 1;
            }

            var prefixLength = 128 - BitOperations.Log2(maxSize);
            BinaryPrimitives.WriteUInt128BigEndian(bytes, start);
            InsertIpv6(root, new IPAddress(bytes), prefixLength);
            start += maxSize;
        }
    }

    private static uint LargestAlignedCidrSize(uint baseAddress)
    {
        if (baseAddress == 0)
        {
            return 1u << 31;
        }

        return baseAddress & (uint)-(int)baseAddress;
    }

    private static UInt128 LargestAlignedCidrSizeV6(UInt128 baseAddress)
    {
        if (baseAddress == UInt128.Zero)
        {
            return (UInt128)1 << 127;
        }

        return baseAddress & (UInt128.Zero - baseAddress);
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

    private static bool TryExtractEndIp(string candidate, IPAddress startIp, out IPAddress endIp)
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
                if (TryParseStrictIp(withoutLevel, out var parsedWithoutLevel))
                {
                    if (parsedWithoutLevel.AddressFamily == AddressFamily.InterNetwork)
                    {
                        endIp = parsedWithoutLevel;
                        return true;
                    }

                    if (parsedWithoutLevel.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        if (candidate.StartsWith('['))
                        {
                            endIp = parsedWithoutLevel;
                            return true;
                        }

                        if (TryParseStrictIp(candidate, out var parsedCandidate))
                        {
                            if (startIp != null && startIp.AddressFamily == AddressFamily.InterNetworkV6 && CompareIpv6(startIp, parsedWithoutLevel) > 0)
                            {
                                endIp = parsedCandidate;
                                return true;
                            }
                        }

                        endIp = parsedWithoutLevel;
                        return true;
                    }
                }
            }
        }

        if (TryParseStrictIp(candidate, out endIp))
        {
            return true;
        }

        return false;
    }

    private static int CompareIpv6(IPAddress ip1, IPAddress ip2)
    {
        Span<byte> b1 = stackalloc byte[16];
        Span<byte> b2 = stackalloc byte[16];
        if (ip1.TryWriteBytes(b1, out _) && ip2.TryWriteBytes(b2, out _))
        {
            var u1 = BinaryPrimitives.ReadUInt128BigEndian(b1);
            var u2 = BinaryPrimitives.ReadUInt128BigEndian(b2);
            return u1.CompareTo(u2);
        }

        return 0;
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

    private class RadixNode
    {
        public RadixNode Zero { get; set; }
        public RadixNode One { get; set; }
        public bool IsBlocked { get; set; }
    }
}

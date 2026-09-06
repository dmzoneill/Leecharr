// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
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

            if (!TryExtractRule(line, out var token) || !TryParseCidr(token, out var ip, out var prefixLength))
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

    private static bool TryExtractRule(string line, out string rule)
    {
        line = line.Trim();
        if (TryParseCidr(line, out _, out _))
        {
            rule = line;
            return true;
        }

        var colon = line.IndexOf(':');
        if (colon >= 0)
        {
            var candidate = line[(colon + 1)..].Trim();
            if (TryParseCidr(candidate, out _, out _))
            {
                rule = candidate;
                return true;
            }
        }

        rule = line;
        return false;
    }

    private bool IsIpv4Blocked(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
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
        var bytes = ip.GetAddressBytes();

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

    private static void InsertIpv4(RadixNode root, IPAddress ip, int prefixLength)
    {
        var bytes = ip.GetAddressBytes();
        var ipNum = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

        var current = root;
        for (var i = 31; i >= 32 - prefixLength; i--)
        {
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
    }

    private static void InsertIpv6(RadixNode root, IPAddress ip, int prefixLength)
    {
        var bytes = ip.GetAddressBytes();

        var current = root;
        for (var bitIndex = 0; bitIndex < prefixLength; bitIndex++)
        {
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
                    if (prefixLength > 96)
                    {
                        prefixLength -= 96;
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

    private class RadixNode
    {
        public RadixNode Zero;
        public RadixNode One;
        public bool IsBlocked;
    }
}

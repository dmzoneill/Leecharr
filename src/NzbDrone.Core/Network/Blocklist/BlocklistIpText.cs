// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace NzbDrone.Core.Network.Blocklist;

internal static class BlocklistIpText
{
    public static bool TryExtractStartIp(string candidate, out IPAddress startIp, out string name)
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

    public static bool TryExtractEndIp(string candidate, IPAddress startIp, out IPAddress endIp, bool includeEqualIpv6)
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
                            if (startIp != null && startIp.AddressFamily == AddressFamily.InterNetworkV6 && Ipv6StartIsBeyond(startIp, parsedWithoutLevel, includeEqualIpv6))
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

    public static bool TryParseStrictIp(string str, out IPAddress ip)
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

    private static bool Ipv6StartIsBeyond(IPAddress startIp, IPAddress parsedWithoutLevel, bool includeEqual)
    {
        var comparison = CompareIpv6(startIp, parsedWithoutLevel);
        return includeEqual ? comparison >= 0 : comparison > 0;
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
}

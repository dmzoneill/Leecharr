// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Net.Sockets;
using System.Security;

namespace NzbDrone.Core.Http;

internal static class SsrfAddressPolicy
{
    private static readonly char[] SubnetSeparators = [',', ';', ' ', '\t', '\r', '\n'];

    public static void EnsureConnectable(string host, IPAddress[] addresses, bool allowPrivateNetworkRequests, string allowedSsrfSubnets)
    {
        if (addresses == null || addresses.Length == 0)
        {
            throw new SecurityException($"Unable to resolve IP address for host '{host}'.");
        }

        foreach (var addr in addresses)
        {
            if (IsBlockedIp(addr, allowPrivateNetworkRequests, allowedSsrfSubnets))
            {
                throw new SecurityException($"SSRF blocked: IP address '{addr}' for host '{host}' is prohibited.");
            }
        }
    }

    public static bool IsAllowedIp(IPAddress ip, bool allowPrivateNetworkRequests, string allowedSsrfSubnets)
    {
        if (ip == null)
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (!string.IsNullOrWhiteSpace(allowedSsrfSubnets))
        {
            var entries = allowedSsrfSubnets.Split(SubnetSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (trimmed.Contains('/'))
                {
                    if (IPNetwork.TryParse(trimmed, out var network) && network.Contains(ip))
                    {
                        return true;
                    }
                }
                else if (IPAddress.TryParse(trimmed, out var parsedIp))
                {
                    if (parsedIp.IsIPv4MappedToIPv6)
                    {
                        parsedIp = parsedIp.MapToIPv4();
                    }

                    if (parsedIp.Equals(ip))
                    {
                        return true;
                    }
                }
            }
        }

        if (allowPrivateNetworkRequests)
        {
            if (IPAddress.IsLoopback(ip))
            {
                return true;
            }

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();

                // 10.0.0.0/8
                if (bytes[0] == 10)
                {
                    return true;
                }

                // 100.64.0.0/10 (CGNAT)
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                {
                    return true;
                }

                // 127.0.0.0/8 (Loopback)
                if (bytes[0] == 127)
                {
                    return true;
                }

                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }

                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || IPAddress.IsLoopback(ip))
                {
                    return true;
                }

                var bytes = ip.GetAddressBytes();

                // fc00::/7 (Unique Local Address)
                if ((bytes[0] & 0xfe) == 0xfc)
                {
                    return true;
                }

                // fe80::/10 (Link-local)
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                {
                    return true;
                }

                // fec0::/10 (Site-local)
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0xc0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsBlockedIp(IPAddress ip, bool allowPrivateNetworkRequests, string allowedSsrfSubnets)
    {
        if (ip == null)
        {
            return true;
        }

        if (IsAllowedIp(ip, allowPrivateNetworkRequests, allowedSsrfSubnets))
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None) || ip.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // 0.0.0.0/8 (Current network)
            if (bytes[0] == 0)
            {
                return true;
            }

            // 10.0.0.0/8 (Private network)
            if (bytes[0] == 10)
            {
                return true;
            }

            // 100.64.0.0/10 (Carrier-grade NAT)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
            {
                return true;
            }

            // 127.0.0.0/8 (Loopback)
            if (bytes[0] == 127)
            {
                return true;
            }

            // 169.254.0.0/16 (Link-local / Cloud metadata: 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // 172.16.0.0/12 (Private network)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16 (Private network)
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // Broadcast
            if (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255)
            {
                return true;
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || IPAddress.IsLoopback(ip))
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();

            // fc00::/7 (Unique Local Address)
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }

            // fe80::/10 (Link-local)
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
            {
                return true;
            }

            // fec0::/10 (Site-local)
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0xc0)
            {
                return true;
            }
        }

        return false;
    }
}

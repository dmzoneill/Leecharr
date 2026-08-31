// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Sockets;

namespace NzbDrone.Core.Authentication;

public class TrustedNetworkService : ITrustedNetworkService
{
    public bool IsLocalOrPrivateNetwork(IPAddress remoteIp)
    {
        if (remoteIp == null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        if (remoteIp.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = remoteIp.GetAddressBytes();

            // 10.0.0.0/8
            if (bytes[0] == 10)
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

            // 127.0.0.0/8
            if (bytes[0] == 127)
            {
                return true;
            }

            // 169.254.0.0/16 (Link Local)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }
        }
        else if (remoteIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv6 Unique Local Address (fc00::/7) or Link-Local (fe80::/10)
            if (remoteIp.IsIPv6LinkLocal || remoteIp.IsIPv6SiteLocal)
            {
                return true;
            }

            var bytes = remoteIp.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsTrustedProxy(IPAddress remoteIp, string configuredCidrs)
    {
        if (remoteIp == null)
        {
            return false;
        }

        // Loopback is always trusted
        if (IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(configuredCidrs))
        {
            return this.IsLocalOrPrivateNetwork(remoteIp);
        }

        var cidrList = configuredCidrs.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var cidr in cidrList)
        {
            if (IPNetworkMatch(remoteIp, cidr.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IPNetworkMatch(IPAddress ip, string cidr)
    {
        try
        {
            if (cidr.Contains('/'))
            {
                var parts = cidr.Split('/');
                var baseIp = IPAddress.Parse(parts[0]);
                var prefixLength = int.Parse(parts[1]);

                if (ip.AddressFamily != baseIp.AddressFamily)
                {
                    if (ip.IsIPv4MappedToIPv6)
                    {
                        ip = ip.MapToIPv4();
                    }

                    if (baseIp.IsIPv4MappedToIPv6)
                    {
                        baseIp = baseIp.MapToIPv4();
                    }

                    if (ip.AddressFamily != baseIp.AddressFamily)
                    {
                        return false;
                    }
                }

                var ipBytes = ip.GetAddressBytes();
                var baseBytes = baseIp.GetAddressBytes();

                var fullBytes = prefixLength / 8;
                var remBits = prefixLength % 8;

                for (var i = 0; i < fullBytes; i++)
                {
                    if (ipBytes[i] != baseBytes[i])
                    {
                        return false;
                    }
                }

                if (remBits > 0 && fullBytes < ipBytes.Length)
                {
                    var mask = (byte)(0xFF << (8 - remBits));
                    if ((ipBytes[fullBytes] & mask) != (baseBytes[fullBytes] & mask))
                    {
                        return false;
                    }
                }

                return true;
            }
            else
            {
                var targetIp = IPAddress.Parse(cidr);
                return ip.Equals(targetIp);
            }
        }
        catch
        {
            return false;
        }
    }
}

// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Net.Sockets;
using NLog;

namespace NzbDrone.Core.ArrIntegration;

public static class ArrTargetUrlValidator
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static bool IsValidTargetUrl(string targetUrl, out string errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            errorMessage = "URL cannot be empty.";
            return false;
        }

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
        {
            errorMessage = $"Invalid URL format: '{targetUrl}'.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Unsupported URL scheme '{uri.Scheme}'. Only HTTP and HTTPS schemes are allowed.";
            return false;
        }

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            errorMessage = "URL host cannot be empty.";
            return false;
        }

        if (string.Equals(host, "instance-data", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".instance-data", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Access to metadata host '{host}' is prohibited.";
            return false;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsBlockedIp(ip))
            {
                errorMessage = $"Access to prohibited IP address '{ip}' is blocked.";
                return false;
            }
        }
        else
        {
            try
            {
                var addresses = Dns.GetHostAddresses(host);
                if (addresses == null || addresses.Length == 0)
                {
                    errorMessage = $"Host '{host}' could not be resolved.";
                    return false;
                }

                foreach (var addr in addresses)
                {
                    if (IsBlockedIp(addr))
                    {
                        errorMessage = $"Host '{host}' resolves to prohibited IP address '{addr}'.";
                        return false;
                    }
                }
            }
            catch (SocketException ex)
            {
                Logger.Trace(ex, "DNS resolution failure during host validation for '{0}'", host);
                errorMessage = $"Host '{host}' could not be resolved.";
                return false;
            }
        }

        return true;
    }

    public static bool IsBlockedIp(IPAddress ip)
    {
        if (ip == null)
        {
            return true;
        }

        if (ip.Equals(IPAddress.Any) ||
            ip.Equals(IPAddress.Broadcast) ||
            ip.Equals(IPAddress.None) ||
            ip.Equals(IPAddress.IPv6Any) ||
            ip.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 0 ||
                (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255) ||
                (bytes[0] == 169 && bytes[1] == 254))
            {
                return true;
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6SiteLocal)
            {
                return true;
            }
        }

        return false;
    }
}

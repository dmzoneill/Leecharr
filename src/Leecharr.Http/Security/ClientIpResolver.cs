// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Security;

public static class ClientIpResolver
{
    public static string ResolveClientIp(
        HttpContext context,
        ITrustedNetworkService trustedNetworkService = null,
        IConfigService configService = null)
    {
        if (context == null)
        {
            return "127.0.0.1";
        }

        var remoteIp = context.Connection?.RemoteIpAddress;
        if (remoteIp == null)
        {
            return "127.0.0.1";
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        trustedNetworkService ??= context.RequestServices?.GetService<ITrustedNetworkService>() ?? new TrustedNetworkService();
        configService ??= context.RequestServices?.GetService<IConfigService>();

        var trustedCidrs = GetTrustedProxies(configService);

        if (trustedNetworkService.IsTrustedProxy(remoteIp, trustedCidrs))
        {
            IPAddress candidateIp = null;
            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
            {
                var firstIp = forwardedFor.ToString().Split(',')[0].Trim();
                if (IPAddress.TryParse(firstIp, out var parsedFwd))
                {
                    candidateIp = parsedFwd.IsIPv4MappedToIPv6 ? parsedFwd.MapToIPv4() : parsedFwd;
                }
            }
            else if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp) && !string.IsNullOrWhiteSpace(realIp))
            {
                if (IPAddress.TryParse(realIp.ToString().Trim(), out var parsedReal))
                {
                    candidateIp = parsedReal.IsIPv4MappedToIPv6 ? parsedReal.MapToIPv4() : parsedReal;
                }
            }

            if (candidateIp != null)
            {
                var isOriginatingLocally = IPAddress.IsLoopback(remoteIp);
                if (isOriginatingLocally || !IsLoopbackOrLinkLocal(candidateIp))
                {
                    return candidateIp.ToString();
                }
            }
        }

        return remoteIp.ToString();
    }

    public static bool IsLoopbackOrLinkLocal(IPAddress ip)
    {
        if (ip == null)
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

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal)
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();
            return bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;
        }

        return false;
    }

    private static string GetTrustedProxies(IConfigService configService)
    {
        if (configService == null)
        {
            return string.Empty;
        }

        var cidrs = configService.GetValue("TrustedProxies", string.Empty);
        if (!string.IsNullOrWhiteSpace(cidrs))
        {
            return cidrs;
        }

        cidrs = configService.GetValue("ForwardAuthTrustedProxies", string.Empty);
        if (!string.IsNullOrWhiteSpace(cidrs))
        {
            return cidrs;
        }

        return configService.GetValue("TrackerTrustedProxies", string.Empty) ?? string.Empty;
    }
}

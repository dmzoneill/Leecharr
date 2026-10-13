// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.Http.Transport;

internal enum HttpOutboundProxyProfile
{
    SocketsHttpHandler,
    CurlImpersonate,
    FlareSolverrFallback,
}

internal static class HttpOutboundProxyConfigurator
{
    internal static void Apply(SocketsHttpHandler handler, IConfigService configService, HttpOutboundProxyProfile profile, Logger logger = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        handler.UseProxy = false;
        handler.Proxy = null;

        var proxyType = configService?.ProxyType?.ToLowerInvariant() ?? "none";
        var proxyHost = configService?.ProxyHost;
        var proxyPort = configService?.ProxyPort ?? GetDefaultPort(proxyType);

        if (string.Equals(proxyType, "none", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(proxyHost))
        {
            return;
        }

        var scheme = MapScheme(proxyType, profile);
        if (scheme == null)
        {
            scheme = "http";
        }

        logger?.Debug("Applying outbound {0} proxy {1}://{2}:{3}", profile, scheme, proxyHost, proxyPort);

        var proxy = new WebProxy(ProxyUriHelper.BuildProxyUri(scheme, proxyHost, proxyPort));
        if (configService.ProxyAuthEnabled && !string.IsNullOrEmpty(configService.ProxyUsername))
        {
            proxy.Credentials = new NetworkCredential(configService.ProxyUsername, configService.ProxyPassword ?? string.Empty);
        }

        handler.Proxy = proxy;
        handler.UseProxy = true;
    }

    private static int GetDefaultPort(string proxyType)
    {
        return proxyType is "socks5" or "socks4" or "socks4a" ? 1080 : 8080;
    }

    // .NET 10 SocketsHttpHandler accepts socks4, socks4a, and socks5. socks5 already sends the
    // destination hostname to the proxy, and socks5h is rejected with NotSupportedException.
    // curl resolves socks5 locally, so that profile must use socks5h. socks4a is the remote-DNS
    // SOCKS4 variant. Leaving it unmapped turns the proxy URI into http and misses the tunnel.
    private static string MapScheme(string proxyType, HttpOutboundProxyProfile profile)
    {
        return profile switch
        {
            HttpOutboundProxyProfile.SocketsHttpHandler => proxyType switch
            {
                "socks5" => "socks5",
                "socks4" => "socks4",
                "socks4a" => "socks4a",
                "http" => "http",
                "https" => "https",
                _ => "http",
            },
            HttpOutboundProxyProfile.CurlImpersonate => proxyType switch
            {
                "socks5" => "socks5h",
                "socks4" => "socks4",
                "socks4a" => "socks4a",
                "http" => "http",
                _ => "http",
            },
            HttpOutboundProxyProfile.FlareSolverrFallback => proxyType switch
            {
                "socks5" => "socks5",
                "socks4" => "socks4",
                "socks4a" => "socks4a",
                "http" => "http",
                _ => "http",
            },
            _ => "http",
        };
    }
}

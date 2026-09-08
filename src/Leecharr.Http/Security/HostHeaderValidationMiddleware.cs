// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Security;

public class HostHeaderValidationMiddleware
{
    private readonly RequestDelegate next;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public HostHeaderValidationMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfigService configService)
    {
        if (configService != null && configService.HostHeaderValidationEnabled)
        {
            var hostHeader = context.Request.Host.Host;

            if (string.IsNullOrWhiteSpace(hostHeader) || !IsHostAllowed(hostHeader, configService.AllowedHosts))
            {
                this.logger.Warn("Blocked request with disallowed Host header: '{0}' from {1}", hostHeader, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("Invalid Host header.");
                return;
            }
        }

        await this.next(context);
    }

    public static bool IsHostAllowed(string host, string allowedHostsConfig)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var cleanHost = host.Trim();
        if (cleanHost.StartsWith('[') && cleanHost.EndsWith(']') && cleanHost.Length >= 2)
        {
            cleanHost = cleanHost.Substring(1, cleanHost.Length - 2).Trim();
        }

        // Loopback is always allowed
        if (cleanHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            cleanHost.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            cleanHost.Equals("::1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check configured allowed hosts
        if (!string.IsNullOrWhiteSpace(allowedHostsConfig))
        {
            var allowed = allowedHostsConfig.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pattern in allowed)
            {
                var trimmedPattern = pattern.Trim();
                if (trimmedPattern == "*")
                {
                    return true;
                }

                if (trimmedPattern.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                    trimmedPattern.Equals(cleanHost, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Wildcard subdomain matching (e.g. *.example.com, *.local, *.lan or .example.com)
                if (trimmedPattern.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
                {
                    var domain = trimmedPattern.Substring(2);
                    if (cleanHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else if (trimmedPattern.StartsWith(".", StringComparison.OrdinalIgnoreCase))
                {
                    var domain = trimmedPattern.Substring(1);
                    if (cleanHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        // Allow local LAN IPs (IPv4 private, link-local, CGNAT/Tailscale, and IPv6 loopback, link-local, ULA, site-local)
        if (IPAddress.TryParse(cleanHost, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (IPAddress.IsLoopback(ip))
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
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

                // 169.254.0.0/16 (Link Local, RFC 3927)
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    return true;
                }

                // 100.64.0.0/10 (Carrier-Grade NAT / Tailscale, RFC 6598)
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                {
                    return true;
                }
            }
            else if (bytes.Length == 16)
            {
                // IPv6 Link-Local (fe80::/10) or Site-Local (fec0::/10)
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                {
                    return true;
                }

                // IPv6 Unique Local Address (fc00::/7 / fd00::/8, RFC 4193)
                if ((bytes[0] & 0xfe) == 0xfc)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

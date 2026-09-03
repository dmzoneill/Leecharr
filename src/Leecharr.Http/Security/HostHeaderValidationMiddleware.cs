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

        // Loopback is always allowed
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("[::1]", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check configured allowed hosts
        if (!string.IsNullOrWhiteSpace(allowedHostsConfig))
        {
            var allowed = allowedHostsConfig.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (allowed.Any(a => a == "*" || a.Equals(host, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Allow local LAN IPs (10.x.x.x, 192.168.x.x, 172.16-31.x.x)
        if (IPAddress.TryParse(host, out var ip))
        {
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
            }
        }

        return false;
    }
}

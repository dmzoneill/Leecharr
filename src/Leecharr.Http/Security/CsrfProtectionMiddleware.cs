// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Security;

public class CsrfProtectionMiddleware
{
    private static readonly string[] DefaultAuthBypassPaths = new[]
    {
        "/auth/login",
        "/auth/callback",
        "/auth/authenticate",
        "/api/v1/auth/login",
        "/api/v1/auth/callback",
        "/api/v2/auth/login",
        "/api/auth/authenticate",
        "/nzbvortex/api/v1/auth/login",
    };

    private readonly RequestDelegate next;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public CsrfProtectionMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public static bool IsAuthPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var bypassPath in DefaultAuthBypassPaths)
        {
            if (path.Equals(bypassPath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(bypassPath + "/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains(bypassPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task InvokeAsync(HttpContext context, IConfigService configService)
    {
        if (configService != null && configService.CsrfProtectionEnabled)
        {
            var method = context.Request.Method;

            // Safe methods don't mutate state
            if (!HttpMethods.IsGet(method) &&
                !HttpMethods.IsHead(method) &&
                !HttpMethods.IsOptions(method) &&
                !HttpMethods.IsTrace(method))
            {
                var path = context.Request.Path.Value ?? string.Empty;

                // Explicit authorization headers, automated RPC clients, and authentication endpoints bypass CSRF check
                var hasExplicitAuthHeader =
                    context.Request.Headers.ContainsKey("X-Api-Key") ||
                    context.Request.Headers.ContainsKey("ApiKey") ||
                    (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
                     (authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                      authHeader.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))) ||
                    context.Request.Headers.ContainsKey("X-Transmission-Session-Id");

                if (!hasExplicitAuthHeader && !IsAuthPath(path))
                {
                    // 1. Check Sec-Fetch-Site (Modern browser defense)
                    if (context.Request.Headers.TryGetValue("Sec-Fetch-Site", out var secFetchSite) &&
                        string.Equals(secFetchSite.ToString(), "cross-site", StringComparison.OrdinalIgnoreCase))
                    {
                        this.logger.Warn("CSRF blocked: cross-site Sec-Fetch-Site on {0} {1}", method, context.Request.Path);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("CSRF check failed: cross-site request blocked.");
                        return;
                    }

                    // 2. Check Origin and Referer headers
                    var hasOrigin = context.Request.Headers.TryGetValue("Origin", out var originHeader) &&
                                    !string.IsNullOrWhiteSpace(originHeader);
                    var hasReferer = context.Request.Headers.TryGetValue("Referer", out var refererHeader) &&
                                     !string.IsNullOrWhiteSpace(refererHeader);

                    if (!hasOrigin && !hasReferer)
                    {
                        this.logger.Warn("CSRF blocked: missing both Origin and Referer headers on {0} {1}", method, context.Request.Path);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("CSRF check failed: missing Origin and Referer.");
                        return;
                    }

                    if (hasOrigin)
                    {
                        if (!IsOriginAllowed(originHeader.ToString(), context.Request.Host))
                        {
                            this.logger.Warn("CSRF blocked: invalid Origin '{0}' on {1} {2}", originHeader, method, context.Request.Path);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("CSRF check failed: invalid Origin.");
                            return;
                        }
                    }
                    else if (hasReferer)
                    {
                        if (!IsOriginAllowed(refererHeader.ToString(), context.Request.Host))
                        {
                            this.logger.Warn("CSRF blocked: invalid Referer '{0}' on {1} {2}", refererHeader, method, context.Request.Path);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("CSRF check failed: invalid Referer.");
                            return;
                        }
                    }
                }
            }
        }

        await this.next(context);
    }

    public static bool IsOriginAllowed(string originOrReferer, HostString requestHost)
    {
        if (!Uri.TryCreate(originOrReferer, UriKind.Absolute, out var uri))
        {
            return false;
        }

        int effectiveRequestPort = requestHost.Port ?? (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        int effectiveOriginPort = uri.Port > 0 ? uri.Port : (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);

        if (effectiveOriginPort != effectiveRequestPort)
        {
            return false;
        }

        // If origin host matches request host
        if (string.Equals(uri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Loopback / localhost match
        var isOriginLoopback = IsLoopbackHost(uri.Host);
        var isRequestLoopback = IsLoopbackHost(requestHost.Host);

        if (isOriginLoopback && isRequestLoopback)
        {
            return true;
        }

        return false;
    }

    private static bool IsLoopbackHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("[::1]", StringComparison.OrdinalIgnoreCase);
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Security;

public class CsrfProtectionMiddleware
{
    private readonly RequestDelegate next;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public CsrfProtectionMiddleware(RequestDelegate next)
    {
        this.next = next;
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
                // API keys and automated RPC clients bypass CSRF check
                var hasApiKey = context.Request.Headers.ContainsKey("X-Api-Key") ||
                                context.Request.Query.ContainsKey("apikey");
                var isBasicAuth = context.Request.Headers.ContainsKey("Authorization") &&
                                  context.Request.Headers["Authorization"].ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase);
                var isTransmissionRpc = context.Request.Headers.ContainsKey("X-Transmission-Session-Id");

                if (!hasApiKey && !isBasicAuth && !isTransmissionRpc)
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

                    // 2. Check Origin header
                    if (context.Request.Headers.TryGetValue("Origin", out var originHeader) &&
                        !string.IsNullOrWhiteSpace(originHeader))
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
                    else if (context.Request.Headers.TryGetValue("Referer", out var refererHeader) &&
                             !string.IsNullOrWhiteSpace(refererHeader))
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

        // If origin host matches request host
        if (string.Equals(uri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Loopback / localhost match
        if ((uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "::1") &&
            (requestHost.Host == "localhost" || requestHost.Host == "127.0.0.1" || requestHost.Host == "::1"))
        {
            return true;
        }

        return false;
    }
}

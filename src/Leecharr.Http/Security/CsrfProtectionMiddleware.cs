// Copyright (c) FeedItOut. All rights reserved.

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

    private static readonly string[] DefaultRpcBypassPaths = new[]
    {
        "/json",
        "/gui",
        "/jsonrpc",
        "/api/v2",
        "/transmission/rpc",
        "/webapi",
        "/rpc",
        "/RPC2",
        "/RPC1",
        "/nzbget",
        "/hadouken",
        "/aria2",
    };

    private static readonly char[] CorsOriginDelimiters = [',', ';', ' ', '\r', '\n'];
    private readonly RequestDelegate next;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public CsrfProtectionMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public static bool IsAuthPath(string path, string urlBase = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Normalize path by stripping urlBase if present
        if (!string.IsNullOrEmpty(urlBase) && path.StartsWith(urlBase, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(urlBase.Length);
            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var bypassPath in DefaultAuthBypassPaths)
        {
            if (path.Equals(bypassPath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(bypassPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsRpcPath(string path, string urlBase = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // Normalize path by stripping urlBase if present
        if (!string.IsNullOrEmpty(urlBase) && path.StartsWith(urlBase, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(urlBase.Length);
            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var bypassPath in DefaultRpcBypassPaths)
        {
            if (path.Equals(bypassPath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(bypassPath + "/", StringComparison.OrdinalIgnoreCase))
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

                if (!hasExplicitAuthHeader && !IsAuthPath(path, context.Request.PathBase.Value) && !IsRpcPath(path, context.Request.PathBase.Value))
                {
                    var allowedCorsOrigins = configService?.AllowedCorsOrigins;

                    var hasOrigin = context.Request.Headers.TryGetValue("Origin", out var originHeader) &&
                                    !string.IsNullOrWhiteSpace(originHeader);
                    var hasReferer = context.Request.Headers.TryGetValue("Referer", out var refererHeader) &&
                        !string.IsNullOrWhiteSpace(refererHeader);

                    // 1. Check Sec-Fetch-Site (Modern browser defense)
                    if (context.Request.Headers.TryGetValue("Sec-Fetch-Site", out var secFetchSite) &&
                        string.Equals(secFetchSite.ToString(), "cross-site", StringComparison.OrdinalIgnoreCase))
                    {
                        var originCandidate = hasOrigin ? originHeader.ToString() : (hasReferer ? refererHeader.ToString() : null);
                        if (string.IsNullOrWhiteSpace(originCandidate) || !IsOriginAllowed(originCandidate, context.Request.Host, allowedCorsOrigins))
                        {
                            this.logger.Warn("CSRF blocked: cross-site Sec-Fetch-Site on {0} {1}", method, context.Request.Path);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("CSRF check failed: cross-site request blocked.", context.RequestAborted);
                            return;
                        }
                    }

                    // 2. Check Origin and Referer headers
                    if (!hasOrigin && !hasReferer)
                    {
                        this.logger.Warn("CSRF blocked: missing both Origin and Referer headers on {0} {1}", method, context.Request.Path);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("CSRF check failed: missing Origin and Referer.", context.RequestAborted);
                        return;
                    }

                    if (hasOrigin)
                    {
                        if (!IsOriginAllowed(originHeader.ToString(), context.Request.Host, allowedCorsOrigins))
                        {
                            this.logger.Warn("CSRF blocked: invalid Origin '{0}' on {1} {2}", originHeader, method, context.Request.Path);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("CSRF check failed: invalid Origin.", context.RequestAborted);
                            return;
                        }
                    }
                    else if (hasReferer)
                    {
                        if (!IsOriginAllowed(refererHeader.ToString(), context.Request.Host, allowedCorsOrigins))
                        {
                            this.logger.Warn("CSRF blocked: invalid Referer '{0}' on {1} {2}", refererHeader, method, context.Request.Path);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "text/plain";
                            await context.Response.WriteAsync("CSRF check failed: invalid Referer.", context.RequestAborted);
                            return;
                        }
                    }
                }
            }
        }

        await this.next(context);
    }

    public static bool IsCorsOriginAllowed(string originOrReferer, string allowedCorsOrigins)
    {
        if (string.IsNullOrWhiteSpace(originOrReferer) || string.IsNullOrWhiteSpace(allowedCorsOrigins))
        {
            return false;
        }

        var cleanOrigin = originOrReferer.Trim().TrimEnd('/');
        if (!Uri.TryCreate(cleanOrigin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        var originScheme = originUri.Scheme;
        var originHost = originUri.Host;
        var originPort = originUri.Port;

        var patterns = allowedCorsOrigins.Split(CorsOriginDelimiters, StringSplitOptions.RemoveEmptyEntries);
        foreach (var pattern in patterns)
        {
            var trimmedPattern = pattern.Trim().TrimEnd('/');
            if (trimmedPattern == "*")
            {
                return true;
            }

            if (string.Equals(cleanOrigin, trimmedPattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string patternScheme = null;
            var schemeDelimiterIndex = trimmedPattern.IndexOf("://", StringComparison.Ordinal);
            var patternRemainder = trimmedPattern;
            if (schemeDelimiterIndex > 0)
            {
                patternScheme = trimmedPattern.Substring(0, schemeDelimiterIndex);
                patternRemainder = trimmedPattern.Substring(schemeDelimiterIndex + 3);
            }

            if (!string.IsNullOrEmpty(patternScheme) && !string.Equals(originScheme, patternScheme, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var patternHost = patternRemainder;
            int? patternPort = null;
            if (patternHost.StartsWith('['))
            {
                var closingBracket = patternHost.IndexOf(']');
                if (closingBracket > 0)
                {
                    if (closingBracket < patternHost.Length - 1 && patternHost[closingBracket + 1] == ':')
                    {
                        if (int.TryParse(patternHost.AsSpan(closingBracket + 2), out var parsedPort))
                        {
                            patternPort = parsedPort;
                        }
                    }

                    patternHost = patternHost.Substring(0, closingBracket + 1);
                }
            }
            else
            {
                var colonIndex = patternHost.LastIndexOf(':');
                if (colonIndex > 0 && int.TryParse(patternHost.AsSpan(colonIndex + 1), out var parsedPort))
                {
                    patternPort = parsedPort;
                    patternHost = patternHost.Substring(0, colonIndex);
                }
            }

            if (!string.IsNullOrEmpty(patternScheme) && !patternPort.HasValue)
            {
                patternPort = string.Equals(patternScheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 :
                    string.Equals(patternScheme, "http", StringComparison.OrdinalIgnoreCase) ? 80 :
                    (int?)null;
            }

            if (patternPort.HasValue && originPort != patternPort.Value)
            {
                continue;
            }

            if (IsHostMatch(originHost, patternHost))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHostMatch(string host, string pattern)
    {
        var cleanHost = host.TrimStart('[').TrimEnd(']');
        var cleanPattern = pattern.TrimStart('[').TrimEnd(']');

        if (string.Equals(cleanHost, cleanPattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (cleanPattern.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
        {
            var domain = cleanPattern.Substring(2);
            if (cleanHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cleanHost, domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        else if (cleanPattern.StartsWith(".", StringComparison.OrdinalIgnoreCase))
        {
            var domain = cleanPattern.Substring(1);
            if (cleanHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cleanHost, domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsOriginAllowed(string originOrReferer, HostString requestHost, string allowedCorsOrigins)
    {
        if (!string.IsNullOrWhiteSpace(allowedCorsOrigins) && IsCorsOriginAllowed(originOrReferer, allowedCorsOrigins))
        {
            return true;
        }

        if (!Uri.TryCreate(originOrReferer, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var effectiveRequestPort = requestHost.Port ?? (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        var effectiveOriginPort = uri.Port > 0 ? uri.Port : (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);

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

    public static bool IsOriginAllowed(string originOrReferer, HostString requestHost)
    {
        return IsOriginAllowed(originOrReferer, requestHost, null);
    }

    private static bool IsLoopbackHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("[::1]", StringComparison.OrdinalIgnoreCase);
    }
}

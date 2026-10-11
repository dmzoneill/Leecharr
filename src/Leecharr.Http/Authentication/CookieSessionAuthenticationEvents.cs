// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Core.Authentication;

namespace Leecharr.Http.Authentication;

public class CookieSessionAuthenticationEvents : CookieAuthenticationEvents
{
    private readonly IUserSessionRepository userSessionRepository;
    private readonly ICookieSessionManager cookieSessionManager;

    public CookieSessionAuthenticationEvents(
        IUserSessionRepository userSessionRepository = null,
        ICookieSessionManager cookieSessionManager = null)
    {
        this.userSessionRepository = userSessionRepository;
        this.cookieSessionManager = cookieSessionManager;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        await base.ValidatePrincipal(context);

        var manager = this.cookieSessionManager;
        if (manager == null)
        {
            var repo = this.userSessionRepository ??
                        context.HttpContext?.RequestServices?.GetService<IUserSessionRepository>();
            var userRepo = context.HttpContext?.RequestServices?.GetService<IUserRepository>();
            if (repo != null)
            {
                manager = new CookieSessionManager(repo, userRepo);
            }
        }

        if (manager != null)
        {
            await manager.ValidatePrincipal(context);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> ctx)
    {
        if (IsMachineFacingPath(ctx.Request.Path))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> ctx)
    {
        if (IsMachineFacingPath(ctx.Request.Path))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    private static bool IsMachineFacingPath(PathString path)
    {
        return path.StartsWithSegments("/api") ||
               path.StartsWithSegments("/signalr") ||
               path.StartsWithSegments("/transmission") ||
               path.StartsWithSegments("/json") ||
               path.StartsWithSegments("/gui") ||
               path.StartsWithSegments("/rpc") ||
               path.StartsWithSegments("/RPC2") ||
               path.StartsWithSegments("/RPC1") ||
               path.StartsWithSegments("/rutorrent") ||
               path.StartsWithSegments("/plugins") ||
               path.StartsWithSegments("/webapi") ||
               path.StartsWithSegments("/jsonrpc") ||
               path.StartsWithSegments("/xmlrpc") ||
               path.StartsWithSegments("/nzbget") ||
               path.StartsWithSegments("/hadouken") ||
               path.StartsWithSegments("/sabnzbd") ||
               path.StartsWithSegments("/aria2") ||
               path.StartsWithSegments("/nzbvortex") ||
               path.StartsWithSegments("/client/connection-test") ||
               IsNzbgetCredentialInPath(path);
    }

    private static bool IsNzbgetCredentialInPath(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        var segments = path.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        var endpoint = segments[1];
        if (!string.Equals(endpoint, "jsonrpc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(endpoint, "xmlrpc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return segments[0].Contains(':', StringComparison.Ordinal);
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Core.Authentication;

namespace Leecharr.Http.Authentication;

public class CookieSessionAuthenticationEvents : CookieAuthenticationEvents
{
    private readonly IUserSessionRepository userSessionRepository;
    private readonly ICookieSessionManager cookieSessionManager;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

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
            if (repo != null)
            {
                manager = new CookieSessionManager(repo);
            }
        }

        if (manager != null)
        {
            await manager.ValidatePrincipal(context);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") ||
            ctx.Request.Path.StartsWithSegments("/signalr") ||
            ctx.Request.Path.StartsWithSegments("/transmission") ||
            ctx.Request.Path.StartsWithSegments("/json"))
        {
            ctx.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }
}

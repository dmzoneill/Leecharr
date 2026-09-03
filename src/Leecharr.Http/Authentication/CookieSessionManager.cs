// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Core.Authentication;

namespace Leecharr.Http.Authentication;

public interface ICookieSessionManager
{
    Task ValidatePrincipal(CookieValidatePrincipalContext context);

    bool ValidateSession(ClaimsPrincipal principal);
}

public class CookieSessionManager : ICookieSessionManager
{
    private readonly IUserSessionRepository userSessionRepository;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public CookieSessionManager(IUserSessionRepository userSessionRepository = null)
    {
        this.userSessionRepository = userSessionRepository;
    }

    public async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var userPrincipal = context.Principal;
        if (userPrincipal == null)
        {
            context.RejectPrincipal();
            await this.SignOutSafelyAsync(context);
            return;
        }

        var sessionClaim = userPrincipal.FindFirst("SessionId") ??
                           userPrincipal.FindFirst("TicketId") ??
                           userPrincipal.FindFirst("SessionToken");

        if (sessionClaim == null || string.IsNullOrWhiteSpace(sessionClaim.Value))
        {
            this.logger.Warn("Rejecting cookie principal without SessionId/TicketId claim.");
            context.RejectPrincipal();
            await this.SignOutSafelyAsync(context);
            return;
        }

        var repository = this.userSessionRepository ??
                         context.HttpContext?.RequestServices?.GetService<IUserSessionRepository>();

        if (repository == null)
        {
            return;
        }

        var token = sessionClaim.Value;
        var session = repository.FindBySessionToken(token);

        if (session == null || session.IsRevoked || session.Expiry < DateTime.UtcNow)
        {
            this.logger.Warn("Rejecting revoked or expired session '{0}'.", token);
            context.RejectPrincipal();
            await this.SignOutSafelyAsync(context);
            return;
        }
    }

    public bool ValidateSession(ClaimsPrincipal principal)
    {
        if (principal == null || this.userSessionRepository == null)
        {
            return false;
        }

        var sessionClaim = principal.FindFirst("SessionId") ??
                           principal.FindFirst("TicketId") ??
                           principal.FindFirst("SessionToken");

        if (sessionClaim == null || string.IsNullOrWhiteSpace(sessionClaim.Value))
        {
            return false;
        }

        var session = this.userSessionRepository.FindBySessionToken(sessionClaim.Value);
        return session != null && !session.IsRevoked && session.Expiry >= DateTime.UtcNow;
    }

    private async Task SignOutSafelyAsync(CookieValidatePrincipalContext context)
    {
        try
        {
            if (context.HttpContext != null)
            {
                await context.HttpContext.SignOutAsync("Cookies");
            }
        }
        catch (Exception)
        {
            // Suppress if IAuthenticationService is not registered in unit test harness
        }
    }
}

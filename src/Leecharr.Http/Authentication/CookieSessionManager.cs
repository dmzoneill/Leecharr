// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
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

    void InvalidateCache(string token);

    void ClearCache();
}

public class CookieSessionManager : ICookieSessionManager
{
    private static readonly ConcurrentDictionary<string, (UserSession Session, DateTime CachedAt)> SharedCache = new();
    private readonly ConcurrentDictionary<string, (UserSession Session, DateTime CachedAt)> sessionCache;
    private readonly IUserSessionRepository userSessionRepository;
    private readonly TimeSpan cacheTtl;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public CookieSessionManager(
        IUserSessionRepository userSessionRepository = null,
        TimeSpan? cacheTtl = null,
        ConcurrentDictionary<string, (UserSession Session, DateTime CachedAt)> cache = null)
    {
        this.userSessionRepository = userSessionRepository;
        this.cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(1);
        this.sessionCache = cache ?? SharedCache;
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
        var now = DateTime.UtcNow;
        UserSession session = null;

        if (this.sessionCache.TryGetValue(token, out var cached) && now - cached.CachedAt < this.cacheTtl)
        {
            session = cached.Session;
        }
        else
        {
            session = repository.FindBySessionToken(token);
            if (session != null)
            {
                this.sessionCache[token] = (session, now);
            }
        }

        if (session == null || session.IsRevoked || session.Expiry < now)
        {
            this.sessionCache.TryRemove(token, out _);
            this.logger.Warn("Rejecting revoked or expired session '{0}'.", token);
            context.RejectPrincipal();
            await this.SignOutSafelyAsync(context);
            return;
        }

        // Sliding Expiry: If context.ShouldRenew is true, or if session.Expiry - DateTime.UtcNow < TimeSpan.FromHours(4)
        if (context.ShouldRenew || (session.Expiry - now < TimeSpan.FromHours(4)))
        {
            var newExpiry = now.AddHours(8);
            session.Expiry = newExpiry;
            session.LastActivity = now;

            if (context.Properties != null)
            {
                context.Properties.ExpiresUtc = newExpiry;
            }

            context.ShouldRenew = true;

            await repository.UpdateExpiryAndActivityAsync(token, newExpiry, now);
            this.sessionCache[token] = (session, now);
        }
        else if (now - session.LastActivity > TimeSpan.FromMinutes(5))
        {
            // Throttled Activity: If DateTime.UtcNow - session.LastActivity > TimeSpan.FromMinutes(5)
            session.LastActivity = now;
            await repository.UpdateLastActivityAsync(token, now);
            this.sessionCache[token] = (session, now);
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

        var token = sessionClaim.Value;
        var now = DateTime.UtcNow;

        if (this.sessionCache.TryGetValue(token, out var cached) && now - cached.CachedAt < this.cacheTtl)
        {
            return cached.Session != null && !cached.Session.IsRevoked && cached.Session.Expiry >= now;
        }

        var session = this.userSessionRepository.FindBySessionToken(token);
        if (session != null)
        {
            this.sessionCache[token] = (session, now);
        }

        return session != null && !session.IsRevoked && session.Expiry >= now;
    }

    public void InvalidateCache(string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            this.sessionCache.TryRemove(token, out _);
        }
    }

    public void ClearCache()
    {
        this.sessionCache.Clear();
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

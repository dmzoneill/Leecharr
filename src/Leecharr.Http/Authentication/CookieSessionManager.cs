// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Core.Authentication;

namespace Leecharr.Http.Authentication;

public interface ICookieSessionManager : IUserSessionCache
{
    Task ValidatePrincipal(CookieValidatePrincipalContext context);

    bool ValidateSession(ClaimsPrincipal principal);

    void Remove(string token);
}

public class CookieSessionManager : ICookieSessionManager
{
    public const int DefaultMaxCacheCapacity = 5000;

    private static readonly ConcurrentDictionary<string, (UserSession Session, DateTime CachedAt)> SharedCache = new();
    private readonly ConcurrentDictionary<string, (UserSession Session, DateTime CachedAt)> sessionCache;
    private readonly IUserSessionRepository userSessionRepository;
    private readonly IUserRepository userRepository;
    private readonly TimeSpan cacheTtl;
    private readonly int maxCacheCapacity;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public CookieSessionManager(
        IUserSessionRepository userSessionRepository = null,
        TimeSpan? cacheTtl = null,
        ConcurrentDictionary<string, (UserSession Session, DateTime CachedAt)> cache = null,
        IUserRepository userRepository = null,
        int maxCacheCapacity = DefaultMaxCacheCapacity)
    {
        this.userSessionRepository = userSessionRepository;
        this.userRepository = userRepository;
        this.cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(1);
        this.sessionCache = cache ?? SharedCache;
        this.maxCacheCapacity = maxCacheCapacity > 0 ? maxCacheCapacity : DefaultMaxCacheCapacity;
    }

    public CookieSessionManager(
        IUserSessionRepository userSessionRepository,
        IUserRepository userRepository,
        TimeSpan? cacheTtl = null,
        ConcurrentDictionary<string, (UserSession Session, DateTime CachedAt)> cache = null,
        int maxCacheCapacity = DefaultMaxCacheCapacity)
        : this(userSessionRepository, cacheTtl, cache, userRepository, maxCacheCapacity)
    {
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
        var userRepo = this.userRepository ??
                       context.HttpContext?.RequestServices?.GetService<IUserRepository>();

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
                this.SetCache(token, session, now);
            }
        }

        if (session == null || session.IsRevoked || session.Expiry < now || session.AbsoluteExpiry < now)
        {
            this.sessionCache.TryRemove(token, out _);
            this.logger.Warn("Rejecting revoked or expired session '{0}'.", token);
            context.RejectPrincipal();
            await this.SignOutSafelyAsync(context);
            return;
        }

        if (userRepo != null && session.UserId > 0)
        {
            var user = userRepo.Get(session.UserId);
            if (user == null)
            {
                this.sessionCache.TryRemove(token, out _);
                this.logger.Warn("Rejecting session '{0}' for deleted or non-existent user {1}.", token, session.UserId);
                context.RejectPrincipal();
                await this.SignOutSafelyAsync(context);
                return;
            }

            if (!string.IsNullOrWhiteSpace(user.Roles))
            {
                try
                {
                    var userRoles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(user.Roles) ?? new List<string>();
                    var principalRoles = userPrincipal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

                    if (principalRoles.Any(r => !userRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
                    {
                        this.sessionCache.TryRemove(token, out _);
                        this.logger.Warn("Rejecting session '{0}' for user {1} due to role mismatch or demotion.", token, session.UserId);
                        context.RejectPrincipal();
                        await this.SignOutSafelyAsync(context);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Warn(ex, "Failed to parse roles for user {0}", session.UserId);
                }
            }
        }

        var isPersistent = context.Properties?.IsPersistent == true ||
                           (session.Expiry - session.CreatedAt > TimeSpan.FromDays(1)) ||
                           (session.Expiry - now > TimeSpan.FromHours(12));

        var renewalThreshold = isPersistent ? TimeSpan.FromDays(15) : TimeSpan.FromHours(4);

        // Sliding Expiry: If context.ShouldRenew is true, or if session.Expiry - DateTime.UtcNow < renewalThreshold
        if (context.ShouldRenew || (session.Expiry - now < renewalThreshold))
        {
            var calculatedExpiry = isPersistent ? now.AddDays(30) : now.AddHours(8);
            var maxExpiry = session.AbsoluteExpiry;
            var newExpiry = calculatedExpiry > maxExpiry ? maxExpiry : calculatedExpiry;

            if (newExpiry <= now)
            {
                this.sessionCache.TryRemove(token, out _);
                this.logger.Warn("Rejecting session '{0}' exceeding absolute expiry.", token);
                context.RejectPrincipal();
                await this.SignOutSafelyAsync(context);
                return;
            }

            session.Expiry = newExpiry;
            session.LastActivity = now;

            if (context.Properties != null)
            {
                context.Properties.ExpiresUtc = newExpiry;
                context.Properties.IsPersistent = isPersistent;
            }

            context.ShouldRenew = true;

            await repository.UpdateExpiryAndActivityAsync(token, newExpiry, now);
            this.SetCache(token, session, now);
        }
        else if (now - session.LastActivity > TimeSpan.FromMinutes(5))
        {
            // Throttled Activity: If DateTime.UtcNow - session.LastActivity > TimeSpan.FromMinutes(5)
            session.LastActivity = now;
            await repository.UpdateLastActivityAsync(token, now);
            this.SetCache(token, session, now);
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
            if (cached.Session == null || cached.Session.IsRevoked || cached.Session.Expiry < now || cached.Session.AbsoluteExpiry < now)
            {
                return false;
            }

            if (this.userRepository != null && cached.Session.UserId > 0)
            {
                var cachedUser = this.userRepository.Get(cached.Session.UserId);
                if (cachedUser == null)
                {
                    this.sessionCache.TryRemove(token, out _);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(cachedUser.Roles))
                {
                    try
                    {
                        var userRoles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(cachedUser.Roles) ?? new List<string>();
                        var principalRoles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

                        if (principalRoles.Any(r => !userRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
                        {
                            this.sessionCache.TryRemove(token, out _);
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.Trace(ex, "Failed to parse user roles JSON in session validation");
                    }
                }
            }

            return true;
        }

        var session = this.userSessionRepository.FindBySessionToken(token);
        if (session != null)
        {
            this.SetCache(token, session, now);
        }

        if (session == null || session.IsRevoked || session.Expiry < now || session.AbsoluteExpiry < now)
        {
            return false;
        }

        if (this.userRepository != null && session.UserId > 0)
        {
            var user = this.userRepository.Get(session.UserId);
            if (user == null)
            {
                this.sessionCache.TryRemove(token, out _);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(user.Roles))
            {
                try
                {
                    var userRoles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(user.Roles) ?? new List<string>();
                    var principalRoles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

                    if (principalRoles.Any(r => !userRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
                    {
                        this.sessionCache.TryRemove(token, out _);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Trace(ex, "Failed to parse user roles JSON in cached session validation");
                }
            }
        }

        return true;
    }

    public void InvalidateCache(string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            this.sessionCache.TryRemove(token, out _);
        }
    }

    public void Remove(string token)
    {
        this.InvalidateCache(token);
    }

    public void ClearCache()
    {
        this.sessionCache.Clear();
    }

    public void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in this.sessionCache)
        {
            var isTtlExpired = now - kvp.Value.CachedAt >= this.cacheTtl;
            var isSessionExpired = kvp.Value.Session != null &&
                                   (kvp.Value.Session.IsRevoked ||
                                    kvp.Value.Session.Expiry < now ||
                                    kvp.Value.Session.AbsoluteExpiry < now);

            if (isTtlExpired || isSessionExpired)
            {
                this.sessionCache.TryRemove(kvp.Key, out _);
            }
        }
    }

    private void SetCache(string token, UserSession session, DateTime now)
    {
        if (!this.sessionCache.ContainsKey(token) && this.sessionCache.Count >= this.maxCacheCapacity)
        {
            this.PruneExpired();

            if (this.sessionCache.Count >= this.maxCacheCapacity)
            {
                var excess = (this.sessionCache.Count - this.maxCacheCapacity) + 1;
                var oldestKeys = this.sessionCache
                    .OrderBy(kvp => kvp.Value.CachedAt)
                    .Take(excess)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in oldestKeys)
                {
                    this.sessionCache.TryRemove(key, out _);
                }
            }
        }

        this.sessionCache[token] = (session, now);
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
        catch (Exception ex)
        {
            this.logger.Trace(ex, "SignOutAsync failed (authentication service may not be registered in test harness)");
        }
    }
}

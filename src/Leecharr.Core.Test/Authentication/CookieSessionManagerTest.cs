// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class CookieSessionManagerTest
{
    private IUserSessionRepository sessionRepository = null!;
    private ConcurrentDictionary<string, (UserSession Session, DateTime CachedAt)> testCache = null!;
    private CookieSessionManager sessionManager = null!;

    [SetUp]
    public void SetUp()
    {
        this.sessionRepository = Substitute.For<IUserSessionRepository>();
        this.testCache = new ConcurrentDictionary<string, (UserSession Session, DateTime CachedAt)>();
        this.sessionManager = new CookieSessionManager(this.sessionRepository, TimeSpan.FromMinutes(2), this.testCache);
    }

    [Test]
    public async Task ValidatePrincipal_WhenShouldRenewIsTrue_UpdatesDatabaseExpiryAndCookieExpiry()
    {
        const string token = "sliding-session-token-1";
        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            Id = 1,
            UserId = 42,
            SessionToken = token,
            Expiry = now.AddHours(6),
            LastActivity = now,
            IsRevoked = false,
        };
        this.sessionRepository.FindBySessionToken(token).Returns(session);

        var principal = CreatePrincipal(new Claim("SessionId", token));
        var context = CreateContext(principal);
        context.ShouldRenew = true;

        await this.sessionManager.ValidatePrincipal(context);

        context.Principal.Should().NotBeNull();
        context.ShouldRenew.Should().BeTrue();
        context.Properties.ExpiresUtc.Should().NotBeNull();
        context.Properties.ExpiresUtc!.Value.UtcDateTime.Should().BeCloseTo(DateTime.UtcNow.AddHours(8), TimeSpan.FromSeconds(10));

        await this.sessionRepository.Received(1).UpdateExpiryAndActivityAsync(
            token,
            Arg.Is<DateTime>(d => d > now.AddHours(7)),
            Arg.Any<DateTime>());
    }

    [Test]
    public async Task ValidatePrincipal_WhenExpiryNearLessThanFourHours_SlidesExpiryAndSetsShouldRenew()
    {
        const string token = "sliding-session-token-2";
        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            Id = 2,
            UserId = 42,
            SessionToken = token,
            Expiry = now.AddHours(2), // Less than 4 hours remaining
            LastActivity = now,
            IsRevoked = false,
        };
        this.sessionRepository.FindBySessionToken(token).Returns(session);

        var principal = CreatePrincipal(new Claim("SessionId", token));
        var context = CreateContext(principal);
        context.ShouldRenew = false;

        await this.sessionManager.ValidatePrincipal(context);

        context.Principal.Should().NotBeNull();
        context.ShouldRenew.Should().BeTrue();
        context.Properties.ExpiresUtc.Should().NotBeNull();
        context.Properties.ExpiresUtc!.Value.UtcDateTime.Should().BeCloseTo(DateTime.UtcNow.AddHours(8), TimeSpan.FromSeconds(10));

        await this.sessionRepository.Received(1).UpdateExpiryAndActivityAsync(
            token,
            Arg.Is<DateTime>(d => d > now.AddHours(7)),
            Arg.Any<DateTime>());
    }

    [Test]
    public async Task ValidatePrincipal_WhenActivityOlderThanFiveMinutes_UpdatesLastActivityWithThrottling()
    {
        const string token = "activity-session-token-3";
        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            Id = 3,
            UserId = 42,
            SessionToken = token,
            Expiry = now.AddHours(7), // Far enough that sliding expiry is not triggered
            LastActivity = now.AddMinutes(-10), // Older than 5 minutes
            IsRevoked = false,
        };
        this.sessionRepository.FindBySessionToken(token).Returns(session);

        var principal = CreatePrincipal(new Claim("SessionId", token));
        var context = CreateContext(principal);
        context.ShouldRenew = false;

        await this.sessionManager.ValidatePrincipal(context);

        context.Principal.Should().NotBeNull();
        await this.sessionRepository.Received(1).UpdateLastActivityAsync(
            token,
            Arg.Is<DateTime>(d => d >= now.AddSeconds(-2)));

        await this.sessionRepository.DidNotReceive().UpdateExpiryAndActivityAsync(
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>());
    }

    [Test]
    public async Task ValidatePrincipal_WhenActivityRecentWithinFiveMinutes_DoesNotUpdateLastActivity()
    {
        const string token = "activity-session-token-4";
        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            Id = 4,
            UserId = 42,
            SessionToken = token,
            Expiry = now.AddHours(7),
            LastActivity = now.AddMinutes(-2), // Within 5 minutes
            IsRevoked = false,
        };
        this.sessionRepository.FindBySessionToken(token).Returns(session);

        var principal = CreatePrincipal(new Claim("SessionId", token));
        var context = CreateContext(principal);
        context.ShouldRenew = false;

        await this.sessionManager.ValidatePrincipal(context);

        context.Principal.Should().NotBeNull();
        await this.sessionRepository.DidNotReceive().UpdateLastActivityAsync(Arg.Any<string>(), Arg.Any<DateTime>());
        await this.sessionRepository.DidNotReceive().UpdateExpiryAndActivityAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }

    [Test]
    public async Task ValidatePrincipal_InMemoryCache_BypassesRedundantDatabaseQueriesWithinTtl()
    {
        const string token = "cached-session-token-5";
        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            Id = 5,
            UserId = 42,
            SessionToken = token,
            Expiry = now.AddHours(7),
            LastActivity = now,
            IsRevoked = false,
        };
        this.sessionRepository.FindBySessionToken(token).Returns(session);

        var principal = CreatePrincipal(new Claim("SessionId", token));

        // First request: hits database and populates cache
        var context1 = CreateContext(principal);
        await this.sessionManager.ValidatePrincipal(context1);
        this.sessionRepository.Received(1).FindBySessionToken(token);

        // Subsequent requests: hit in-memory cache without querying database
        for (var i = 0; i < 4; i++)
        {
            var contextN = CreateContext(principal);
            await this.sessionManager.ValidatePrincipal(contextN);
        }

        // Repository should still only have received exactly 1 database query
        this.sessionRepository.Received(1).FindBySessionToken(token);
    }

    [Test]
    public async Task ValidatePrincipal_InMemoryCache_QueriesDatabaseAfterTtlExpires()
    {
        const string token = "expired-cache-token-6";
        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            Id = 6,
            UserId = 42,
            SessionToken = token,
            Expiry = now.AddHours(7),
            LastActivity = now,
            IsRevoked = false,
        };
        this.sessionRepository.FindBySessionToken(token).Returns(session);

        var shortTtlManager = new CookieSessionManager(this.sessionRepository, TimeSpan.FromMilliseconds(50), this.testCache);
        var principal = CreatePrincipal(new Claim("SessionId", token));

        // First request: hits database
        var context1 = CreateContext(principal);
        await shortTtlManager.ValidatePrincipal(context1);
        this.sessionRepository.Received(1).FindBySessionToken(token);

        // Wait for cache TTL to expire
        await Task.Delay(100);

        // Second request after TTL: queries database again
        var context2 = CreateContext(principal);
        await shortTtlManager.ValidatePrincipal(context2);
        this.sessionRepository.Received(2).FindBySessionToken(token);
    }

    [Test]
    public async Task ValidatePrincipal_WhenPersistentSessionRenews_ExtendsToThirtyDays()
    {
        const string token = "persistent-session-token";
        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            Id = 10,
            UserId = 42,
            SessionToken = token,
            CreatedAt = now.AddDays(-28),
            Expiry = now.AddDays(2), // 2 days left out of original 30 days
            LastActivity = now.AddDays(-1),
            IsRevoked = false,
        };
        this.sessionRepository.FindBySessionToken(token).Returns(session);

        var principal = CreatePrincipal(new Claim("SessionId", token));
        var context = CreateContext(principal);
        context.Properties.IsPersistent = true;
        context.ShouldRenew = false;

        await this.sessionManager.ValidatePrincipal(context);

        context.Principal.Should().NotBeNull();
        context.ShouldRenew.Should().BeTrue();
        context.Properties.IsPersistent.Should().BeTrue();
        context.Properties.ExpiresUtc.Should().NotBeNull();
        context.Properties.ExpiresUtc!.Value.UtcDateTime.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromSeconds(10));

        await this.sessionRepository.Received(1).UpdateExpiryAndActivityAsync(
            token,
            Arg.Is<DateTime>(d => d > now.AddDays(29)),
            Arg.Any<DateTime>());
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] additionalClaims)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "42"),
            new(ClaimTypes.Name, "testuser"),
            new(ClaimTypes.Role, "Admin"),
        };

        claims.AddRange(additionalClaims);

        var identity = new ClaimsIdentity(claims, "Cookies");
        return new ClaimsPrincipal(identity);
    }

    private static CookieValidatePrincipalContext CreateContext(ClaimsPrincipal principal)
    {
        var httpContext = new DefaultHttpContext();
        var authScheme = new AuthenticationScheme("Cookies", "Cookies", typeof(CookieAuthenticationHandler));
        var options = new CookieAuthenticationOptions();
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), "Cookies");
        return new CookieValidatePrincipalContext(httpContext, authScheme, options, ticket);
    }
}

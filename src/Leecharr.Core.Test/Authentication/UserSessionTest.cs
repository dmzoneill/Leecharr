// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
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
public class UserSessionTest
{
    private IUserSessionRepository sessionRepository;
    private CookieSessionAuthenticationEvents events;
    private CookieSessionManager sessionManager;

    [SetUp]
    public void SetUp()
    {
        this.sessionRepository = Substitute.For<IUserSessionRepository>();
        this.sessionManager = new CookieSessionManager(this.sessionRepository);
        this.events = new CookieSessionAuthenticationEvents(this.sessionRepository, this.sessionManager);
    }

    #region Cookie Principal Validation Tests

    [Test]
    public async Task ValidatePrincipal_WhenSessionIsActive_AcceptsPrincipal()
    {
        const string token = "active-session-token-123";
        var principal = CreatePrincipal(new Claim("SessionId", token));

        this.sessionRepository.FindBySessionToken(token).Returns(new UserSession
        {
            Id = 1,
            UserId = 42,
            SessionToken = token,
            Expiry = DateTime.UtcNow.AddDays(7),
            IsRevoked = false,
        });

        var context = CreateContext(principal);

        await this.events.ValidatePrincipal(context);

        context.Principal.Should().NotBeNull();
        context.Principal.FindFirst("SessionId")?.Value.Should().Be(token);
    }

    [Test]
    public async Task ValidatePrincipal_WhenSessionIsRevokedInDatabase_RejectsPrincipal()
    {
        const string token = "revoked-session-token-456";
        var principal = CreatePrincipal(new Claim("SessionId", token));

        // When a session is revoked, FindBySessionToken returns null (record deleted)
        this.sessionRepository.FindBySessionToken(token).Returns((UserSession)null);

        var context = CreateContext(principal);

        await this.events.ValidatePrincipal(context);

        context.Principal.Should().BeNull();
    }

    [Test]
    public async Task ValidatePrincipal_WhenSessionIsMarkedRevoked_RejectsPrincipal()
    {
        const string token = "revoked-flag-token-789";
        var principal = CreatePrincipal(new Claim("SessionId", token));

        this.sessionRepository.FindBySessionToken(token).Returns(new UserSession
        {
            Id = 2,
            UserId = 42,
            SessionToken = token,
            Expiry = DateTime.UtcNow.AddDays(1),
            IsRevoked = true,
        });

        var context = CreateContext(principal);

        await this.events.ValidatePrincipal(context);

        context.Principal.Should().BeNull();
    }

    [Test]
    public async Task ValidatePrincipal_WhenSessionIsExpired_RejectsPrincipal()
    {
        const string token = "expired-session-token-999";
        var principal = CreatePrincipal(new Claim("SessionId", token));

        this.sessionRepository.FindBySessionToken(token).Returns(new UserSession
        {
            Id = 3,
            UserId = 42,
            SessionToken = token,
            Expiry = DateTime.UtcNow.AddHours(-1),
            IsRevoked = false,
        });

        var context = CreateContext(principal);

        await this.events.ValidatePrincipal(context);

        context.Principal.Should().BeNull();
    }

    [Test]
    public async Task ValidatePrincipal_WhenUsingTicketIdClaim_AcceptsActiveSession()
    {
        const string token = "ticket-id-session-token";
        var principal = CreatePrincipal(new Claim("TicketId", token));

        this.sessionRepository.FindBySessionToken(token).Returns(new UserSession
        {
            Id = 4,
            UserId = 10,
            SessionToken = token,
            Expiry = DateTime.UtcNow.AddHours(2),
            IsRevoked = false,
        });

        var context = CreateContext(principal);

        await this.sessionManager.ValidatePrincipal(context);

        context.Principal.Should().NotBeNull();
    }

    [Test]
    public async Task ValidatePrincipal_WhenClaimsLackSessionIdOrTicketId_RejectsPrincipal()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Name, "testuser"),
        };
        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        var context = CreateContext(principal);

        await this.events.ValidatePrincipal(context);

        context.Principal.Should().BeNull();
    }

    [Test]
    public void ValidateSession_WhenSessionIsActive_ReturnsTrue()
    {
        const string token = "validate-active-token";
        var principal = CreatePrincipal(new Claim("SessionId", token));

        this.sessionRepository.FindBySessionToken(token).Returns(new UserSession
        {
            SessionToken = token,
            Expiry = DateTime.UtcNow.AddDays(1),
            IsRevoked = false,
        });

        this.sessionManager.ValidateSession(principal).Should().BeTrue();
    }

    [Test]
    public void ValidateSession_WhenSessionIsRevokedOrExpired_ReturnsFalse()
    {
        const string token = "validate-expired-token";
        var principal = CreatePrincipal(new Claim("SessionId", token));

        this.sessionRepository.FindBySessionToken(token).Returns(new UserSession
        {
            SessionToken = token,
            Expiry = DateTime.UtcNow.AddMinutes(-5),
            IsRevoked = false,
        });

        this.sessionManager.ValidateSession(principal).Should().BeFalse();
    }

    #endregion

    #region Session Pruning / Cleanup Tests

    [Test]
    public async Task PruneExpiredSessionsAsync_WhenCalled_PurgesExpiredSessionsFromRepository()
    {
        this.sessionRepository.PruneExpiredSessionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(7));

        using var cleanupTask = new SessionCleanupTask(this.sessionRepository);

        var purgedCount = await cleanupTask.PruneExpiredSessionsAsync();

        purgedCount.Should().Be(7);
        await this.sessionRepository.Received(1).PruneExpiredSessionsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenCalled_RunsPruningCycle()
    {
        this.sessionRepository.PruneExpiredSessionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(3));

        using var cleanupTask = new SessionCleanupTask(this.sessionRepository);

        await cleanupTask.ExecuteAsync();

        await this.sessionRepository.Received(1).PruneExpiredSessionsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void UserSession_ExpiresAt_AliasesExpiryPropertyCorrectly()
    {
        var session = new UserSession();
        var now = DateTime.UtcNow.AddDays(5);

        session.ExpiresAt = now;
        session.Expiry.Should().Be(now);

        var later = now.AddDays(2);
        session.Expiry = later;
        session.ExpiresAt.Should().Be(later);
    }

    #endregion

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
        var ticket = new AuthenticationTicket(principal, "Cookies");
        return new CookieValidatePrincipalContext(httpContext, authScheme, options, ticket);
    }
}

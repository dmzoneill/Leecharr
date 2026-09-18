// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class AuthControllerLoginBruteForceTest
{
    private IUserService userService;
    private IIdentityProviderService identityProviderService;
    private IConfigFileProvider configFileProvider;
    private IConfigService configService;
    private IUserSessionRepository sessionRepository;
    private AuthController controller;

    [SetUp]
    public void SetUp()
    {
        AuthController.ResetThrottling();

        this.userService = Substitute.For<IUserService>();
        this.identityProviderService = Substitute.For<IIdentityProviderService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.sessionRepository = Substitute.For<IUserSessionRepository>();

        this.controller = new AuthController(
            this.userService,
            this.identityProviderService,
            this.configFileProvider,
            this.configService,
            this.sessionRepository);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.42");
        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;

        this.controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    [TearDown]
    public void TearDown()
    {
        AuthController.ResetThrottling();
    }

    [Test]
    public async Task Login_WhenFailedAttemptsReachLimit_Returns429TooManyRequests()
    {
        this.userService.Authenticate("admin", "wrongpassword").Returns((User)null!);

        var request = new LoginRequestResource
        {
            Username = "admin",
            Password = "wrongpassword",
        };

        // 5 failed attempts
        for (var i = 0; i < 5; i++)
        {
            var result = await this.controller.Login(request);
            result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        // 6th attempt should be throttled
        var throttledResult = await this.controller.Login(request);
        throttledResult.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)throttledResult.Result;
        objectResult.StatusCode.Should().Be(429);
    }

    [Test]
    public async Task Login_WhenSuccessful_ResetsFailedAttemptCounter()
    {
        this.userService.Authenticate("admin", "wrongpassword").Returns((User)null!);
        this.userService.Authenticate("admin", "correctpassword").Returns(new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = "hash",
            Salt = "salt",
        });

        var failRequest = new LoginRequestResource
        {
            Username = "admin",
            Password = "wrongpassword",
        };

        // 4 failed attempts
        for (var i = 0; i < 4; i++)
        {
            var result = await this.controller.Login(failRequest);
            result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        // Successful login
        var successRequest = new LoginRequestResource
        {
            Username = "admin",
            Password = "correctpassword",
        };
        var successResult = await this.controller.Login(successRequest);
        successResult.Result.Should().BeOfType<OkObjectResult>();

        // Next 4 failed attempts should not trigger throttle
        for (var i = 0; i < 4; i++)
        {
            var result = await this.controller.Login(failRequest);
            result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        }
    }

    [Test]
    public void PruneExpiredLoginAttempts_WhenEntriesExpired_RemovesExpiredAndRetainsActive()
    {
        var now = DateTime.UtcNow;

        // Expired window (no lockout)
        AuthController.RecordFailedLoginAttempt("192.168.1.10", failures: 2, windowStart: now.AddMinutes(-20));

        // Active window (no lockout)
        AuthController.RecordFailedLoginAttempt("192.168.1.20", failures: 2, windowStart: now.AddMinutes(-5));

        // Expired lockout
        AuthController.RecordFailedLoginAttempt("192.168.1.30", failures: 5, windowStart: now.AddMinutes(-30), lockoutUntil: now.AddMinutes(-1));

        // Active lockout
        AuthController.RecordFailedLoginAttempt("192.168.1.40", failures: 5, windowStart: now.AddMinutes(-5), lockoutUntil: now.AddMinutes(10));

        AuthController.TrackedLoginAttemptsCount.Should().Be(4);

        AuthController.PruneExpiredLoginAttempts(now);

        AuthController.TrackedLoginAttemptsCount.Should().Be(2);
    }

    [Test]
    public void PruneExpiredLoginAttempts_WhenExceedingCapacity_EvictsOldestEntries()
    {
        var now = DateTime.UtcNow;

        // Record 10 active entries with staggered timestamps
        for (var i = 0; i < 10; i++)
        {
            AuthController.RecordFailedLoginAttempt($"10.0.0.{i}", failures: 1, windowStart: now.AddMinutes(-10 + i));
        }

        AuthController.TrackedLoginAttemptsCount.Should().Be(10);

        // Cap at 6: should evict oldest entries down to maxTrackedIps / 2 (3 entries)
        AuthController.PruneExpiredLoginAttempts(now, maxTrackedIps: 6);

        AuthController.TrackedLoginAttemptsCount.Should().Be(3);
    }

    [Test]
    public async Task Login_WhenFailedLoginOccurs_AutomaticallyPrunesExpiredRecords()
    {
        var now = DateTime.UtcNow;

        // Populate expired entries
        AuthController.RecordFailedLoginAttempt("192.168.100.1", failures: 1, windowStart: now.AddMinutes(-25));
        AuthController.RecordFailedLoginAttempt("192.168.100.2", failures: 5, windowStart: now.AddMinutes(-40), lockoutUntil: now.AddMinutes(-5));

        AuthController.TrackedLoginAttemptsCount.Should().Be(2);

        this.userService.Authenticate("admin", "wrongpassword").Returns((User)null!);
        var request = new LoginRequestResource
        {
            Username = "admin",
            Password = "wrongpassword",
        };

        var result = await this.controller.Login(request);
        result.Result.Should().BeOfType<UnauthorizedObjectResult>();

        // Expired entries were pruned and current request IP was recorded
        AuthController.TrackedLoginAttemptsCount.Should().Be(1);
    }

    [Test]
    public async Task Login_WhenUntrustedClientSpoofsForwardedFor_IgnoresHeaderAndThrottlesActualRemoteIp()
    {
        this.userService.Authenticate("admin", "wrongpassword").Returns((User)null!);

        var failRequest = new LoginRequestResource
        {
            Username = "admin",
            Password = "wrongpassword",
        };

        // Attacker is at 198.51.100.42 (untrusted) and rotates X-Forwarded-For on each attempt
        for (var i = 1; i <= 5; i++)
        {
            this.controller.ControllerContext.HttpContext.Request.Headers["X-Forwarded-For"] = $"203.0.113.{i}";
            var result = await this.controller.Login(failRequest);
            result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        // 6th attempt with another spoofed header must be throttled because actual remote IP is tracked
        this.controller.ControllerContext.HttpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.99";
        var throttledResult = await this.controller.Login(failRequest);
        throttledResult.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)throttledResult.Result;
        objectResult.StatusCode.Should().Be(429);

        // Actual attacker IP is throttled, but spoofed target IPs are not
        AuthController.IsThrottledForIp("198.51.100.42").Should().BeTrue();
        AuthController.IsThrottledForIp("203.0.113.1").Should().BeFalse();
        AuthController.IsThrottledForIp("203.0.113.99").Should().BeFalse();
    }

    [Test]
    public async Task Login_WhenUntrustedClientSpoofsTargetIp_DoesNotLockOutTargetIp()
    {
        this.userService.Authenticate("admin", "wrongpassword").Returns((User)null!);

        var failRequest = new LoginRequestResource
        {
            Username = "admin",
            Password = "wrongpassword",
        };

        // Attacker at 198.51.100.42 attempts to lock out victim administrator at 192.168.1.100
        this.controller.ControllerContext.HttpContext.Request.Headers["X-Forwarded-For"] = "192.168.1.100";

        for (var i = 0; i < 5; i++)
        {
            var result = await this.controller.Login(failRequest);
            result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        // Target administrator IP must NOT be throttled
        AuthController.IsThrottledForIp("192.168.1.100").Should().BeFalse();

        // Attacker remote IP must be throttled
        AuthController.IsThrottledForIp("198.51.100.42").Should().BeTrue();
    }

    [Test]
    public async Task Login_WhenTrustedProxy_AcceptsForwardedForAndThrottlesClientIp()
    {
        this.configService.GetValue("TrustedProxies", string.Empty).Returns("10.0.0.0/24");
        this.userService.Authenticate("admin", "wrongpassword").Returns((User)null!);

        var proxyController = new AuthController(
            this.userService,
            this.identityProviderService,
            this.configFileProvider,
            this.configService,
            this.sessionRepository);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.55";
        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;

        proxyController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var failRequest = new LoginRequestResource
        {
            Username = "admin",
            Password = "wrongpassword",
        };

        for (var i = 0; i < 5; i++)
        {
            var result = await proxyController.Login(failRequest);
            result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        // 6th attempt from 203.0.113.55 is throttled
        var throttledResult = await proxyController.Login(failRequest);
        throttledResult.Result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)throttledResult.Result).StatusCode.Should().Be(429);

        // Client IP behind trusted proxy is throttled, but proxy itself is not throttled for other clients
        AuthController.IsThrottledForIp("203.0.113.55").Should().BeTrue();
        AuthController.IsThrottledForIp("10.0.0.5").Should().BeFalse();

        // Another client behind the same trusted proxy can still attempt login
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.66";
        var allowedResult = await proxyController.Login(failRequest);
        allowedResult.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Test]
    public async Task Login_WhenTrustedProxy_RejectsSpoofedLoopbackForwardedFor()
    {
        this.configService.GetValue("TrustedProxies", string.Empty).Returns("10.0.0.0/24");
        this.userService.Authenticate("admin", "wrongpassword").Returns((User)null!);

        var proxyController = new AuthController(
            this.userService,
            this.identityProviderService,
            this.configFileProvider,
            this.configService,
            this.sessionRepository);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        httpContext.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;

        proxyController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var failRequest = new LoginRequestResource
        {
            Username = "admin",
            Password = "wrongpassword",
        };

        for (var i = 0; i < 5; i++)
        {
            var result = await proxyController.Login(failRequest);
            result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        // Spoofed loopback is rejected; proxy IP 10.0.0.5 is recorded instead of loopback
        AuthController.IsThrottledForIp("127.0.0.1").Should().BeFalse();
        AuthController.IsThrottledForIp("10.0.0.5").Should().BeTrue();
    }
}

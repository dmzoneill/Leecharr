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
}

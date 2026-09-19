// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Security.Claims;
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
public class AuthControllerTest
{
    private IUserService userService = null!;
    private IIdentityProviderService identityProviderService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IConfigService configService = null!;
    private IUserSessionRepository userSessionRepository = null!;
    private IUserSessionCache userSessionCache = null!;
    private IAuthenticationService authenticationService = null!;
    private AuthController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.userService = Substitute.For<IUserService>();
        this.identityProviderService = Substitute.For<IIdentityProviderService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.userSessionRepository = Substitute.For<IUserSessionRepository>();
        this.userSessionCache = Substitute.For<IUserSessionCache>();
        this.authenticationService = Substitute.For<IAuthenticationService>();

        this.controller = new AuthController(
            this.userService,
            this.identityProviderService,
            this.configFileProvider,
            this.configService,
            userSessionRepository: this.userSessionRepository,
            userSessionCache: this.userSessionCache);

        var httpContext = new DefaultHttpContext();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(this.authenticationService);
        httpContext.RequestServices = serviceProvider;

        this.controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    [Test]
    public async Task Logout_WhenUserHasSession_DeletesSessionFromRepositoryAndInvalidatesCache()
    {
        const string sessionToken = "session-token-xyz";
        var claims = new[] { new Claim("SessionId", sessionToken) };
        this.controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));

        var session = new UserSession
        {
            Id = 42,
            UserId = 1,
            SessionToken = sessionToken,
        };
        this.userSessionRepository.FindBySessionToken(sessionToken).Returns(session);

        var result = await this.controller.Logout();

        result.Should().BeOfType<OkObjectResult>();
        this.userSessionRepository.Received(1).Delete(42);
        this.userSessionCache.Received(1).InvalidateCache(sessionToken);
    }

    [Test]
    public async Task Logout_WhenSessionNotFoundInRepository_StillInvalidatesCache()
    {
        const string sessionToken = "session-token-abc";
        var claims = new[] { new Claim("TicketId", sessionToken) };
        this.controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));

        this.userSessionRepository.FindBySessionToken(sessionToken).Returns((UserSession)null!);

        var result = await this.controller.Logout();

        result.Should().BeOfType<OkObjectResult>();
        this.userSessionRepository.DidNotReceive().Delete(Arg.Any<int>());
        this.userSessionCache.Received(1).InvalidateCache(sessionToken);
    }

    [Test]
    public async Task Logout_WhenNoSessionClaimPresent_DoesNotInvokeRepositoryOrCache()
    {
        this.controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await this.controller.Logout();

        result.Should().BeOfType<OkObjectResult>();
        this.userSessionRepository.DidNotReceive().FindBySessionToken(Arg.Any<string>());
        this.userSessionRepository.DidNotReceive().Delete(Arg.Any<int>());
        this.userSessionCache.DidNotReceive().InvalidateCache(Arg.Any<string>());
    }
}

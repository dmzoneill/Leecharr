// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Terminal;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class PtyTerminalAuthTest
{
    private IConfigFileProvider configFileProvider = null!;
    private IConfigService configService = null!;
    private IPtyTerminalService ptyService = null!;

    [SetUp]
    public void SetUp()
    {
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.TerminalAccessEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("master_secret_api_key");

        this.configService = Substitute.For<IConfigService>();
        this.ptyService = Substitute.For<IPtyTerminalService>();
    }

    [Test]
    public async Task HandleWebSocket_WhenAuthEnabledAndUnauthenticated_RejectsWith401Unauthorized()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Response.Body = new MemoryStream();

        await TerminalWebSocketHandler.HandleWebSocket(context, this.ptyService, this.configService, this.configFileProvider);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        this.ptyService.DidNotReceive().CreateSession(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Test]
    public async Task HandleWebSocket_WhenAuthEnabledAndInvalidApiKey_RejectsWith401Unauthorized()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Request.Headers["X-Api-Key"] = "invalid_api_key";
        context.Response.Body = new MemoryStream();

        await TerminalWebSocketHandler.HandleWebSocket(context, this.ptyService, this.configService, this.configFileProvider);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        this.ptyService.DidNotReceive().CreateSession(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Test]
    public void IsAuthorized_WhenValidApiKeyHeader_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Request.Headers["X-Api-Key"] = "master_secret_api_key";

        var isAuthorized = TerminalWebSocketHandler.IsAuthorized(context, this.configFileProvider);

        isAuthorized.Should().BeTrue();
    }

    [Test]
    public void IsAuthorized_WhenValidApiKeyQueryParameter_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Request.QueryString = new QueryString("?apikey=master_secret_api_key");

        var isAuthorized = TerminalWebSocketHandler.IsAuthorized(context, this.configFileProvider);

        isAuthorized.Should().BeTrue();
    }

    [Test]
    public void IsAuthorized_WhenValidTokenQueryParameter_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Request.QueryString = new QueryString("?token=master_secret_api_key");

        var isAuthorized = TerminalWebSocketHandler.IsAuthorized(context, this.configFileProvider);

        isAuthorized.Should().BeTrue();
    }

    [Test]
    public void IsAuthorized_WhenUserPrincipalAuthenticated_ReturnsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        var identity = new ClaimsIdentity("CookieAuth");
        identity.AddClaim(new Claim(ClaimTypes.Name, "admin"));
        context.User = new ClaimsPrincipal(identity);

        var isAuthorized = TerminalWebSocketHandler.IsAuthorized(context, this.configFileProvider);

        isAuthorized.Should().BeTrue();
    }

    [Test]
    public void IsAuthorized_WhenAuthDisabled_ReturnsTrueWithoutCredentials()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";

        var isAuthorized = TerminalWebSocketHandler.IsAuthorized(context, this.configFileProvider);

        isAuthorized.Should().BeTrue();
    }

    [Test]
    public void PtyTerminalService_CreateSession_WhenTerminalAccessDisabled_ThrowsSecurityException()
    {
        this.configFileProvider.TerminalAccessEnabled.Returns(false);
        var service = new PtyTerminalService(this.configFileProvider);

        Action act = () => service.CreateSession("/tmp", 80, 24);

        act.Should().Throw<System.Security.SecurityException>();
    }

    [Test]
    public void PtyTerminalService_CreateSession_WhenPathContainsNullBytes_ThrowsArgumentException()
    {
        var service = new PtyTerminalService(this.configFileProvider);

        Action act = () => service.CreateSession("/tmp\0malicious", 80, 24);

        act.Should().Throw<ArgumentException>();
    }
}

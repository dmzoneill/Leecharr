// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class BasicAuthenticationHandlerTest
{
    private IConfigFileProvider configFileProvider;
    private BasicAuthenticationHandler handler;
    private DefaultHttpContext httpContext;

    [SetUp]
    public async Task SetUp()
    {
        BasicAuthenticationHandler.ResetThrottling();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        var optionsMonitor = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
        optionsMonitor.Get(BasicAuthenticationOptions.DefaultScheme).Returns(new BasicAuthenticationOptions());

        this.handler = new BasicAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider);

        this.httpContext = new DefaultHttpContext();
        this.httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
        var scheme = new AuthenticationScheme(BasicAuthenticationOptions.DefaultScheme, null, typeof(BasicAuthenticationHandler));
        await this.handler.InitializeAsync(scheme, this.httpContext);
    }

    [TearDown]
    public void TearDown()
    {
        BasicAuthenticationHandler.ResetThrottling();
    }

    [Test]
    public async Task AuthenticateAsync_WithoutAuthHeader_ReturnsNoResult()
    {
        var result = await this.handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithValidBasicCredentials_ReturnsSuccess()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("valid-password");

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:valid-password"));
        this.httpContext.Request.Headers["Authorization"] = "Basic " + encoded;

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithInvalidBasicCredentials_ReturnsFail()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("valid-password");

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrong-password"));
        this.httpContext.Request.Headers["Authorization"] = "Basic " + encoded;

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    private BasicAuthenticationHandler CreateHandler(HttpContext context)
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
        optionsMonitor.Get(BasicAuthenticationOptions.DefaultScheme).Returns(new BasicAuthenticationOptions());

        var h = new BasicAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider);

        var scheme = new AuthenticationScheme(BasicAuthenticationOptions.DefaultScheme, null, typeof(BasicAuthenticationHandler));
        h.InitializeAsync(scheme, context).GetAwaiter().GetResult();
        return h;
    }

    [Test]
    public async Task AuthenticateAsync_WhenFailedAttemptsReachLimit_ReturnsFailWithRateLimitMessage()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("valid-password");

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrong-password"));

        // 5 failed attempts
        for (var i = 0; i < 5; i++)
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
            ctx.Request.Headers["Authorization"] = "Basic " + encoded;
            var h = this.CreateHandler(ctx);
            var failResult = await h.AuthenticateAsync();
            failResult.Succeeded.Should().BeFalse();
            failResult.Failure!.Message.Should().Be("Invalid Basic authentication credentials.");
        }

        // 6th attempt should be throttled
        var throttleCtx = new DefaultHttpContext();
        throttleCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
        throttleCtx.Request.Headers["Authorization"] = "Basic " + encoded;
        var throttleHandler = this.CreateHandler(throttleCtx);
        var throttledResult = await throttleHandler.AuthenticateAsync();
        throttledResult.Succeeded.Should().BeFalse();
        throttledResult.Failure!.Message.Should().Contain("Too many failed authentication attempts");
    }

    [Test]
    public async Task AuthenticateAsync_WhenSuccessful_ResetsFailedAttemptsCounter()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("valid-password");

        var wrongEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrong-password"));
        var validEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:valid-password"));

        // 4 failed attempts
        for (var i = 0; i < 4; i++)
        {
            var failCtx = new DefaultHttpContext();
            failCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
            failCtx.Request.Headers["Authorization"] = "Basic " + wrongEncoded;
            var h = this.CreateHandler(failCtx);
            var failResult = await h.AuthenticateAsync();
            failResult.Succeeded.Should().BeFalse();
        }

        // 1 successful attempt
        var successCtx = new DefaultHttpContext();
        successCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
        successCtx.Request.Headers["Authorization"] = "Basic " + validEncoded;
        var successHandler = this.CreateHandler(successCtx);
        var successResult = await successHandler.AuthenticateAsync();
        successResult.Succeeded.Should().BeTrue();

        // 4 more failed attempts should not trigger rate limit
        for (var i = 0; i < 4; i++)
        {
            var nextCtx = new DefaultHttpContext();
            nextCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
            nextCtx.Request.Headers["Authorization"] = "Basic " + wrongEncoded;
            var h = this.CreateHandler(nextCtx);
            var failResult = await h.AuthenticateAsync();
            failResult.Succeeded.Should().BeFalse();
            failResult.Failure!.Message.Should().Be("Invalid Basic authentication credentials.");
        }
    }

    [Test]
    public async Task AuthenticateAsync_WithDatabaseUserCredentials_ReturnsSuccessWithRoles()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("apikey-not-matching");

        var userService = Substitute.For<NzbDrone.Core.Authentication.IUserService>();
        userService.Authenticate("dbuser", "secretpass").Returns(new NzbDrone.Core.Authentication.User
        {
            Id = 5,
            Username = "dbuser",
            Email = "dbuser@example.com",
            DisplayName = "DB User",
            Roles = "[\"Admin\",\"User\"]",
        });

        var optionsMonitor = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
        optionsMonitor.Get(BasicAuthenticationOptions.DefaultScheme).Returns(new BasicAuthenticationOptions());

        var h = new BasicAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider,
            userService);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("dbuser:secretpass"));
        ctx.Request.Headers["Authorization"] = "Basic " + encoded;

        var scheme = new AuthenticationScheme(BasicAuthenticationOptions.DefaultScheme, null, typeof(BasicAuthenticationHandler));
        await h.InitializeAsync(scheme, ctx);

        var result = await h.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal.Identity.Name.Should().Be("dbuser");
        result.Principal.IsInRole("Admin").Should().BeTrue();
        result.Principal.IsInRole("User").Should().BeTrue();
        result.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)!.Value.Should().Be("dbuser@example.com");
        result.Principal.FindFirst("DisplayName")!.Value.Should().Be("DB User");
        result.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value.Should().Be("5");
    }

    [Test]
    public async Task AuthenticateAsync_WithNonBasicScheme_ReturnsNoResult()
    {
        this.httpContext.Request.Headers["Authorization"] = "Bearer some-token";

        var result = await this.handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithApiKeyAsUsername_ReturnsSuccess()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("my-secret-key");

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("my-secret-key:any-pass"));
        this.httpContext.Request.Headers["Authorization"] = "Basic " + encoded;

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal.IsInRole("Admin").Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithAuthenticationDisabled_ReturnsSuccess()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);
        this.configFileProvider.ApiKey.Returns("any-key");

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("randomuser:randompass"));
        this.httpContext.Request.Headers["Authorization"] = "Basic " + encoded;

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal.Identity.Name.Should().Be("randomuser");
        result.Principal.IsInRole("Admin").Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithDatabaseUser_EmptyOrInvalidRoles_DefaultsToUserRole()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("apikey-mismatch");

        var userService = Substitute.For<NzbDrone.Core.Authentication.IUserService>();
        userService.Authenticate("simpleuser", "pass").Returns(new NzbDrone.Core.Authentication.User
        {
            Id = 10,
            Username = "simpleuser",
            Roles = "not-json",
            DisplayName = null,
            Email = null,
        });

        var optionsMonitor = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
        optionsMonitor.Get(BasicAuthenticationOptions.DefaultScheme).Returns(new BasicAuthenticationOptions());

        var h = new BasicAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider,
            userService);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("simpleuser:pass"));
        ctx.Request.Headers["Authorization"] = "Basic " + encoded;

        var scheme = new AuthenticationScheme(BasicAuthenticationOptions.DefaultScheme, null, typeof(BasicAuthenticationHandler));
        await h.InitializeAsync(scheme, ctx);

        var result = await h.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal.IsInRole("User").Should().BeTrue();
        result.Principal.IsInRole("Admin").Should().BeFalse();
        result.Principal.FindFirst("DisplayName")!.Value.Should().Be("simpleuser");
        result.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email).Should().BeNull();
    }

    [Test]
    public async Task AuthenticateAsync_WithDatabaseUser_InvalidPassword_ReturnsFail()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("apikey-mismatch");

        var userService = Substitute.For<NzbDrone.Core.Authentication.IUserService>();
        userService.Authenticate("dbuser", "wrongpass").Returns((NzbDrone.Core.Authentication.User)null);

        var optionsMonitor = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
        optionsMonitor.Get(BasicAuthenticationOptions.DefaultScheme).Returns(new BasicAuthenticationOptions());

        var h = new BasicAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider,
            userService);

        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("dbuser:wrongpass"));
        ctx.Request.Headers["Authorization"] = "Basic " + encoded;

        var scheme = new AuthenticationScheme(BasicAuthenticationOptions.DefaultScheme, null, typeof(BasicAuthenticationHandler));
        await h.InitializeAsync(scheme, ctx);

        var result = await h.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Be("Invalid Basic authentication credentials.");
    }

    [Test]
    public async Task AuthenticateAsync_WithMalformedBase64_ReturnsFail()
    {
        this.httpContext.Request.Headers["Authorization"] = "Basic @@@not-base64@@@";

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Failed to parse Basic authentication header");
    }

    [Test]
    public async Task AuthenticateAsync_WhenUserServiceResolvedFromRequestServices_AuthenticatesSuccessfully()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("apikey-mismatch");

        var userService = Substitute.For<NzbDrone.Core.Authentication.IUserService>();
        userService.Authenticate("serviceuser", "pass").Returns(new NzbDrone.Core.Authentication.User
        {
            Id = 12,
            Username = "serviceuser",
            Roles = "[\"Operator\"]",
        });

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(NzbDrone.Core.Authentication.IUserService)).Returns(userService);

        var optionsMonitor = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
        optionsMonitor.Get(BasicAuthenticationOptions.DefaultScheme).Returns(new BasicAuthenticationOptions());

        var h = new BasicAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider,
            userService: null);

        var ctx = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
        };
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.2");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("serviceuser:pass"));
        ctx.Request.Headers["Authorization"] = "Basic " + encoded;

        var scheme = new AuthenticationScheme(BasicAuthenticationOptions.DefaultScheme, null, typeof(BasicAuthenticationHandler));
        await h.InitializeAsync(scheme, ctx);

        var result = await h.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal.Identity.Name.Should().Be("serviceuser");
        result.Principal.IsInRole("Operator").Should().BeTrue();
    }
}

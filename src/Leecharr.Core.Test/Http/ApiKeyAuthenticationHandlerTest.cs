// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Security.Claims;
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
public class ApiKeyAuthenticationHandlerTest
{
    private IConfigFileProvider configFileProvider;
    private ApiKeyAuthenticationHandler handler;
    private DefaultHttpContext httpContext;

    [SetUp]
    public async Task SetUp()
    {
        ApiKeyAuthenticationHandler.ResetThrottling();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        var optionsMonitor = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        optionsMonitor.Get(ApiKeyAuthenticationOptions.DefaultScheme).Returns(new ApiKeyAuthenticationOptions());

        this.handler = new ApiKeyAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider);

        this.httpContext = new DefaultHttpContext();
        this.httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.1");
        var scheme = new AuthenticationScheme(ApiKeyAuthenticationOptions.DefaultScheme, null, typeof(ApiKeyAuthenticationHandler));
        await this.handler.InitializeAsync(scheme, this.httpContext);
    }

    [TearDown]
    public void TearDown()
    {
        ApiKeyAuthenticationHandler.ResetThrottling();
    }

    [Test]
    public async Task AuthenticateAsync_WhenAuthDisabled_ReturnsSuccessWithAdminClaims()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.Identity!.Name.Should().Be("Admin");
    }

    [Test]
    public async Task AuthenticateAsync_WithValidApiKeyInHeader_ReturnsSuccess()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-api-key-123");
        this.httpContext.Request.Headers["X-Api-Key"] = "secret-api-key-123";

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithValidApiKeyInQuery_ReturnsSuccess()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("test-secret-key");
        this.httpContext.Request.QueryString = new QueryString("?apikey=test-secret-key");

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithValidAccessTokenInQuery_ReturnsSuccess()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("test-secret-key");
        this.httpContext.Request.QueryString = new QueryString("?access_token=test-secret-key");

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithValidApiKey2InQuery_ReturnsSuccess()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("test-secret-key");
        this.httpContext.Request.QueryString = new QueryString("?api_key=test-secret-key");

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithInvalidApiKey_ReturnsFail()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("test-secret-key");
        this.httpContext.Request.Headers["X-Api-Key"] = "wrong-key";

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Test]
    public async Task AuthenticateAsync_WithNoApiKey_ReturnsNoResult()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("test-secret-key");

        var result = await this.handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithEmptyAccessTokenInQuery_ReturnsNoResult()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("test-secret-key");
        this.httpContext.Request.QueryString = new QueryString("?access_token=");

        var result = await this.handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    private ApiKeyAuthenticationHandler CreateHandler(HttpContext context)
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        optionsMonitor.Get(ApiKeyAuthenticationOptions.DefaultScheme).Returns(new ApiKeyAuthenticationOptions());

        var h = new ApiKeyAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider);

        var scheme = new AuthenticationScheme(ApiKeyAuthenticationOptions.DefaultScheme, null, typeof(ApiKeyAuthenticationHandler));
        h.InitializeAsync(scheme, context).GetAwaiter().GetResult();
        return h;
    }

    [Test]
    public async Task AuthenticateAsync_WhenFailedAttemptsReachLimit_ReturnsFailWithRateLimitMessage()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("correct-key");

        // 5 failed attempts
        for (var i = 0; i < 5; i++)
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.1");
            ctx.Request.Headers["X-Api-Key"] = "wrong-key";
            var h = this.CreateHandler(ctx);
            var failResult = await h.AuthenticateAsync();
            failResult.Succeeded.Should().BeFalse();
            failResult.Failure!.Message.Should().Be("Invalid API Key");
        }

        // 6th attempt should be throttled
        var throttleCtx = new DefaultHttpContext();
        throttleCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.1");
        throttleCtx.Request.Headers["X-Api-Key"] = "wrong-key";
        var throttleHandler = this.CreateHandler(throttleCtx);
        var throttledResult = await throttleHandler.AuthenticateAsync();
        throttledResult.Succeeded.Should().BeFalse();
        throttledResult.Failure!.Message.Should().Contain("Too many failed authentication attempts");
    }

    [Test]
    public async Task AuthenticateAsync_WhenSuccessful_ResetsFailedAttemptsCounter()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("correct-key");

        // 4 failed attempts
        for (var i = 0; i < 4; i++)
        {
            var failCtx = new DefaultHttpContext();
            failCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.1");
            failCtx.Request.Headers["X-Api-Key"] = "wrong-key";
            var h = this.CreateHandler(failCtx);
            var failResult = await h.AuthenticateAsync();
            failResult.Succeeded.Should().BeFalse();
        }

        // 1 successful attempt
        var successCtx = new DefaultHttpContext();
        successCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.1");
        successCtx.Request.Headers["X-Api-Key"] = "correct-key";
        var successHandler = this.CreateHandler(successCtx);
        var successResult = await successHandler.AuthenticateAsync();
        successResult.Succeeded.Should().BeTrue();

        // 4 more failed attempts should not trigger rate limit
        for (var i = 0; i < 4; i++)
        {
            var nextCtx = new DefaultHttpContext();
            nextCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.1");
            nextCtx.Request.Headers["X-Api-Key"] = "wrong-key";
            var h = this.CreateHandler(nextCtx);
            var failResult = await h.AuthenticateAsync();
            failResult.Succeeded.Should().BeFalse();
            failResult.Failure!.Message.Should().Be("Invalid API Key");
        }
    }
}

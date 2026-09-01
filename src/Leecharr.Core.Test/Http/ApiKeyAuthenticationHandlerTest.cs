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
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        var optionsMonitor = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        optionsMonitor.Get(ApiKeyAuthenticationOptions.DefaultScheme).Returns(new ApiKeyAuthenticationOptions());

        this.handler = new ApiKeyAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider);

        this.httpContext = new DefaultHttpContext();
        var scheme = new AuthenticationScheme(ApiKeyAuthenticationOptions.DefaultScheme, null, typeof(ApiKeyAuthenticationHandler));
        await this.handler.InitializeAsync(scheme, this.httpContext);
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
        this.configFileProvider.ApiKey.Returns("secret-api-key-123");
        this.httpContext.Request.QueryString = new QueryString("?apikey=secret-api-key-123");

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
    }

    [Test]
    public async Task AuthenticateAsync_WithInvalidApiKey_ReturnsFail()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-api-key-123");
        this.httpContext.Request.Headers["X-Api-Key"] = "wrong-key";

        var result = await this.handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Test]
    public async Task AuthenticateAsync_WithNoApiKey_ReturnsNoResult()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-api-key-123");

        var result = await this.handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }
}

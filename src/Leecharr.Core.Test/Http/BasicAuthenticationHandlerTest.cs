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
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        var optionsMonitor = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
        optionsMonitor.Get(BasicAuthenticationOptions.DefaultScheme).Returns(new BasicAuthenticationOptions());

        this.handler = new BasicAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.configFileProvider);

        this.httpContext = new DefaultHttpContext();
        var scheme = new AuthenticationScheme(BasicAuthenticationOptions.DefaultScheme, null, typeof(BasicAuthenticationHandler));
        await this.handler.InitializeAsync(scheme, this.httpContext);
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
}

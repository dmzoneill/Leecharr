// Copyright (c) PlaceholderCompany. All rights reserved.

using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class SecurityMiddlewareTest
{
    [TestCase("localhost", true)]
    [TestCase("127.0.0.1", true)]
    [TestCase("::1", true)]
    [TestCase("192.168.1.100", true)]
    [TestCase("10.0.0.5", true)]
    [TestCase("172.20.0.2", true)]
    [TestCase("evil.attacker.com", false)]
    public void HostHeaderValidation_IsHostAllowed_ValidatesCorrectly(string host, bool expectedAllowed)
    {
        var allowed = HostHeaderValidationMiddleware.IsHostAllowed(host, string.Empty);
        allowed.Should().Be(expectedAllowed);
    }

    [Test]
    public void HostHeaderValidation_AllowsExplicitlyConfiguredDomains()
    {
        var allowed = HostHeaderValidationMiddleware.IsHostAllowed("my.customdomain.org", "leecharr.local, my.customdomain.org");
        allowed.Should().BeTrue();
    }

    [Test]
    public async Task HostHeaderValidationMiddleware_BlocksDisallowedHostWhenEnabled()
    {
        var config = Substitute.For<IConfigService>();
        config.HostHeaderValidationEnabled.Returns(true);
        config.AllowedHosts.Returns(string.Empty);

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("malicious.dnsrebind.com");
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new HostHeaderValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task CsrfProtectionMiddleware_AllowsSafeGetMethods()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Host = new HostString("localhost:5000");

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        nextCalled.Should().BeTrue();
    }

    [Test]
    public async Task CsrfProtectionMiddleware_BypassesWhenApiKeyIsProvided()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Headers["X-Api-Key"] = "valid_api_key_123";
        context.Request.Headers["Origin"] = "https://external.cross-origin.com";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        nextCalled.Should().BeTrue();
    }

    [Test]
    public async Task CsrfProtectionMiddleware_BlocksCrossOriginSecFetchSite()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task CsrfProtectionMiddleware_BlocksInvalidOrigin()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("leecharr.local:8080");
        context.Request.Headers["Origin"] = "http://evil-attacker.com";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task CsrfProtectionMiddleware_AllowsMatchingOrigin()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("leecharr.local:8080");
        context.Request.Headers["Origin"] = "http://leecharr.local:8080";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        nextCalled.Should().BeTrue();
    }
}

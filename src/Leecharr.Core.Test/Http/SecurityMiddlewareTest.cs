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

    [Test]
    public async Task CsrfProtectionMiddleware_BlocksCrossPortOrigin()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("localhost:7889");
        context.Request.Headers["Origin"] = "http://localhost:3000";
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
    public async Task CsrfProtectionMiddleware_BlocksMissingOriginAndRefererWhenNoAuthHeader()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("localhost:7889");
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
    public async Task CsrfProtectionMiddleware_AllowsMissingOriginAndRefererWhenExplicitApiKeyHeaderPresent()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("localhost:7889");
        context.Request.Headers["X-Api-Key"] = "secret-api-key";

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
    public async Task CsrfProtectionMiddleware_AllowsMatchingOriginAndPort()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("localhost:7889");
        context.Request.Headers["Origin"] = "http://localhost:7889";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        nextCalled.Should().BeTrue();
    }

    [TestCase("/api/v1/auth/login")]
    [TestCase("/auth/login")]
    [TestCase("/api/v1/auth/callback/saml")]
    [TestCase("/auth/callback")]
    [TestCase("/api/v2/auth/login")]
    [TestCase("/api/auth/authenticate")]
    [TestCase("/nzbvortex/api/v1/auth/login")]
    public async Task CsrfProtectionMiddleware_AllowsAuthEndpointsWithoutOriginOrReferer(string path)
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Request.Host = new HostString("localhost:7889");

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
    public async Task CsrfProtectionMiddleware_BlocksNonAuthEndpointWithoutOriginOrReferer()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/torrents/add";
        context.Request.Host = new HostString("localhost:7889");
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
    public async Task SecurityHeadersMiddleware_EmitsStandardSecurityHeadersOnHttp()
    {
        var context = new DefaultHttpContext();
        context.Request.IsHttps = false;
        context.Request.Scheme = "http";

        var nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        context.Response.Headers["Permissions-Policy"].ToString().Should().Be("geolocation=(), camera=(), microphone=(), payment=()");
        context.Response.Headers["Content-Security-Policy"].ToString().Should().Be("default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https: blob:; font-src 'self' data:; connect-src 'self' ws: wss:; frame-ancestors 'self';");
        context.Response.Headers.ContainsKey("Strict-Transport-Security").Should().BeFalse();
    }

    [Test]
    public async Task SecurityHeadersMiddleware_EmitsHstsOnHttps()
    {
        var context = new DefaultHttpContext();
        context.Request.IsHttps = true;
        context.Request.Scheme = "https";

        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        context.Response.Headers["Content-Security-Policy"].ToString().Should().Be("default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https: blob:; font-src 'self' data:; connect-src 'self' ws: wss:; frame-ancestors 'self';");
        context.Response.Headers["Strict-Transport-Security"].ToString().Should().Be("max-age=31536000; includeSubDomains");
    }

    [Test]
    public async Task SecurityHeadersMiddleware_EmitsHstsOnForwardedProtoHttps()
    {
        var context = new DefaultHttpContext();
        context.Request.IsHttps = false;
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.Headers["Strict-Transport-Security"].ToString().Should().Be("max-age=31536000; includeSubDomains");
    }
}

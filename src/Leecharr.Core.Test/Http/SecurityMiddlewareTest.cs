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
    [TestCase("[::1]", true)]
    [TestCase("192.168.1.100", true)]
    [TestCase("10.0.0.5", true)]
    [TestCase("172.20.0.2", true)]
    [TestCase("169.254.1.1", true)]
    [TestCase("100.64.0.1", true)]
    [TestCase("100.100.50.25", true)]
    [TestCase("100.127.255.254", true)]
    [TestCase("100.63.255.255", false)]
    [TestCase("100.128.0.1", false)]
    [TestCase("fe80::1", true)]
    [TestCase("[fe80::1]", true)]
    [TestCase("[fe80::215:5dff:fe00:402]", true)]
    [TestCase("fd00::1", true)]
    [TestCase("[fd00::1]", true)]
    [TestCase("fc00::1", true)]
    [TestCase("[fc00::1]", true)]
    [TestCase("fec0::1", true)]
    [TestCase("[fec0::1]", true)]
    [TestCase("2001:db8::1", false)]
    [TestCase("[2001:db8::1]", false)]
    [TestCase("8.8.8.8", false)]
    [TestCase("evil.attacker.com", false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    public void HostHeaderValidation_IsHostAllowed_ValidatesCorrectly(string host, bool expectedAllowed)
    {
        var allowed = HostHeaderValidationMiddleware.IsHostAllowed(host, string.Empty);
        allowed.Should().Be(expectedAllowed);
    }

    [TestCase("sub.example.com", "*.example.com", true)]
    [TestCase("deep.sub.example.com", "*.example.com", true)]
    [TestCase("example.com", "*.example.com", false)]
    [TestCase("badexample.com", "*.example.com", false)]
    [TestCase("sub.local", "*.local", true)]
    [TestCase("myhost.lan", "*.lan", true)]
    [TestCase("app.home.arpa", ".home.arpa", true)]
    [TestCase("any.domain.org", "*", true)]
    [TestCase("my.customdomain.org", "leecharr.local, *.customdomain.org", true)]
    public void HostHeaderValidation_WildcardAllowedHosts(string host, string allowedHosts, bool expectedAllowed)
    {
        var allowed = HostHeaderValidationMiddleware.IsHostAllowed(host, allowedHosts);
        allowed.Should().Be(expectedAllowed);
    }

    [Test]
    public void HostHeaderValidation_AllowsExplicitlyConfiguredDomains()
    {
        var allowed = HostHeaderValidationMiddleware.IsHostAllowed("my.customdomain.org", "leecharr.local, my.customdomain.org");
        allowed.Should().BeTrue();
    }

    [TestCase("[fe80::1]")]
    [TestCase("[fd00::1]")]
    [TestCase("100.100.1.1")]
    [TestCase("app.leecharr.lan")]
    public async Task HostHeaderValidationMiddleware_AllowsValidPrivateAndWildcardHostsWhenEnabled(string hostHeader)
    {
        var config = Substitute.For<IConfigService>();
        config.HostHeaderValidationEnabled.Returns(true);
        config.AllowedHosts.Returns("*.leecharr.lan");

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(hostHeader);
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new HostHeaderValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
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

    [TestCase("/api/v1/torrents/add")]
    [TestCase("/api/v1/download/auth/login/custom")]
    [TestCase("/api/v1/torrents/auth/login")]
    [TestCase("/api/v1/settings/auth/authenticate")]
    [TestCase("/fake/auth/login/action")]
    [TestCase("/api/v1/config/custom/auth/login")]
    [TestCase("/api/v1/tags/delete/auth/login")]
    [TestCase("/api/v1/user/update/auth/callback")]
    public async Task CsrfProtectionMiddleware_BlocksNonAuthEndpointWithoutOriginOrReferer(string path)
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
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

    [TestCase("/auth/login", true)]
    [TestCase("/auth/login/subpath", true)]
    [TestCase("/api/v1/auth/login", true)]
    [TestCase("/api/v1/auth/callback", true)]
    [TestCase("/api/v1/auth/callback/saml", true)]
    [TestCase("/api/v1/torrents/add", false)]
    [TestCase("/api/v1/something/auth/login", false)]
    [TestCase("/api/v1/config/custom/auth/login", false)]
    [TestCase("/api/v1/tags/delete/auth/login", false)]
    [TestCase("/api/v1/user/update/auth/callback", false)]
    [TestCase("/api/v1/torrents/pause/auth/authenticate", false)]
    [TestCase("/api/v1/auth/login-fake", false)]
    [TestCase("/fake/auth/login", false)]
    [TestCase("/not-auth/login", false)]
    [TestCase("/custom/auth/login", false)]
    public void CsrfProtectionMiddleware_IsAuthPath_StrictMatching(string path, bool expected)
    {
        CsrfProtectionMiddleware.IsAuthPath(path).Should().Be(expected);
    }

    [TestCase("/leecharr/api/v1/auth/login", "/leecharr", true)]
    [TestCase("/leecharr/auth/login", "/leecharr", true)]
    [TestCase("/leecharr/auth/login/subpath", "/leecharr", true)]
    [TestCase("/leecharr/auth/login", "/leecharr/", true)]
    [TestCase("/leecharr/api/v1/config/custom/auth/login", "/leecharr", false)]
    [TestCase("/leecharr/api/v1/torrents", "/leecharr", false)]
    [TestCase("/leecharr", "/leecharr", false)]
    [TestCase("", "/leecharr", false)]
    public void CsrfProtectionMiddleware_IsAuthPath_WithUrlBase(string path, string urlBase, bool expected)
    {
        CsrfProtectionMiddleware.IsAuthPath(path, urlBase).Should().Be(expected);
    }

    [TestCase("/json", true)]
    [TestCase("/json/rpc", true)]
    [TestCase("/gui", true)]
    [TestCase("/gui/token.html", true)]
    [TestCase("/jsonrpc", true)]
    [TestCase("/api/v2/torrents/info", true)]
    [TestCase("/transmission/rpc", true)]
    [TestCase("/webapi/DownloadStation/task.cgi", true)]
    [TestCase("/rpc", true)]
    [TestCase("/RPC2", true)]
    [TestCase("/RPC1", true)]
    [TestCase("/nzbget", true)]
    [TestCase("/hadouken", true)]
    [TestCase("/aria2", true)]
    [TestCase("/api/v1/torrents", false)]
    [TestCase("/custom/route", false)]
    public void CsrfProtectionMiddleware_IsRpcPath_StrictMatching(string path, bool expected)
    {
        CsrfProtectionMiddleware.IsRpcPath(path).Should().Be(expected);
    }

    [TestCase("/leecharr/json", "/leecharr", true)]
    [TestCase("/leecharr/transmission/rpc", "/leecharr", true)]
    [TestCase("/leecharr/api/v1/torrents", "/leecharr", false)]
    [TestCase("/leecharr", "/leecharr", false)]
    [TestCase("", "/leecharr", false)]
    public void CsrfProtectionMiddleware_IsRpcPath_WithUrlBase(string path, string urlBase, bool expected)
    {
        CsrfProtectionMiddleware.IsRpcPath(path, urlBase).Should().Be(expected);
    }

    [TestCase("/json")]
    [TestCase("/gui")]
    [TestCase("/jsonrpc")]
    [TestCase("/api/v2/torrents/add")]
    [TestCase("/transmission/rpc")]
    [TestCase("/webapi/DownloadStation/task.cgi")]
    public async Task CsrfProtectionMiddleware_AllowsRpcEndpointsWithoutOriginOrReferer(string path)
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

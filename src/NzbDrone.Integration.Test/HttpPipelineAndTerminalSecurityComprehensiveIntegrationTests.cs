// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Authentication;
using Leecharr.Http.Ping;
using Leecharr.Http.REST;
using Leecharr.Http.Security;
using Leecharr.Http.Terminal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NLog;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.SignalR;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class HttpPipelineAndTerminalSecurityComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task PingEndpoint_GetRequest_ReturnsOkWithStatusPayload()
    {
        var response = await this.Client.GetAsync("/ping");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("status").GetString().Should().Be("OK");
    }

    [Test]
    public void CsrfProtectionMiddleware_IsAuthPathAndIsRpcPath_ValidatesAllBypassPrefixes()
    {
        // 1. Auth paths
        CsrfProtectionMiddleware.IsAuthPath(null).Should().BeFalse();
        CsrfProtectionMiddleware.IsAuthPath(string.Empty).Should().BeFalse();
        CsrfProtectionMiddleware.IsAuthPath("/auth/login").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/auth/login/subpath").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/auth/callback").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/auth/authenticate").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/api/v1/auth/login").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/api/v1/auth/callback").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/api/v2/auth/login").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/api/auth/authenticate").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/nzbvortex/api/v1/auth/login").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/api/v1/torrents").Should().BeFalse();

        // Auth path with urlBase
        CsrfProtectionMiddleware.IsAuthPath("/leecharr/auth/login", "/leecharr").Should().BeTrue();
        CsrfProtectionMiddleware.IsAuthPath("/leecharr/api/v1/torrents", "/leecharr").Should().BeFalse();

        // 2. RPC paths
        CsrfProtectionMiddleware.IsRpcPath(null).Should().BeFalse();
        CsrfProtectionMiddleware.IsRpcPath(string.Empty).Should().BeFalse();
        CsrfProtectionMiddleware.IsRpcPath("/json").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/gui").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/jsonrpc").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/api/v2").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/transmission/rpc").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/webapi").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/rpc").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/RPC2").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/RPC1").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/nzbget").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/hadouken").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/aria2").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/api/v1/other").Should().BeFalse();

        // RPC path with urlBase
        CsrfProtectionMiddleware.IsRpcPath("/sub/transmission/rpc", "/sub").Should().BeTrue();
        CsrfProtectionMiddleware.IsRpcPath("/sub/api/v1/other", "/sub").Should().BeFalse();
    }

    [Test]
    public void CsrfProtectionMiddleware_IsOriginAllowed_ValidatesHostsPortsAndLoopbacks()
    {
        var requestHost = new HostString("localhost", 7889);

        // Same host and port
        CsrfProtectionMiddleware.IsOriginAllowed("http://localhost:7889", requestHost).Should().BeTrue();
        CsrfProtectionMiddleware.IsOriginAllowed("http://localhost:7889/some/path", requestHost).Should().BeTrue();

        // Loopback mapping (127.0.0.1 to localhost)
        CsrfProtectionMiddleware.IsOriginAllowed("http://127.0.0.1:7889", requestHost).Should().BeTrue();
        CsrfProtectionMiddleware.IsOriginAllowed("http://[::1]:7889", requestHost).Should().BeTrue();

        // Mismatched port
        CsrfProtectionMiddleware.IsOriginAllowed("http://localhost:3000", requestHost).Should().BeFalse();

        // External / attacker host
        CsrfProtectionMiddleware.IsOriginAllowed("http://attacker.com:7889", requestHost).Should().BeFalse();

        // Invalid URI
        CsrfProtectionMiddleware.IsOriginAllowed("not_a_valid_uri", requestHost).Should().BeFalse();
    }

    [Test]
    public async Task CsrfProtectionMiddleware_SafeHttpMethodsAndBypassHeaders_PassesThrough()
    {
        var configService = Substitute.For<IConfigService>();
        configService.CsrfProtectionEnabled.Returns(true);

        // Safe methods (GET, HEAD, OPTIONS, TRACE)
        foreach (var method in new[] { "GET", "HEAD", "OPTIONS", "TRACE" })
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = "/api/v1/torrents";

            var nextInvoked = false;
            var middleware = new CsrfProtectionMiddleware(_ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, configService);
            nextInvoked.Should().BeTrue($"safe method {method} must bypass CSRF check");
        }

        // Unsafe methods with explicit auth headers bypass CSRF
        var authHeaderTestCases = new[]
        {
            ("X-Api-Key", "my_secret_key"),
            ("ApiKey", "my_secret_key"),
            ("Authorization", "Bearer valid_jwt_token"),
            ("Authorization", "Basic dXNlcjpwYXNz"),
            ("X-Transmission-Session-Id", "session_token_123"),
        };

        foreach (var (headerName, headerValue) in authHeaderTestCases)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/api/v1/torrents/action";
            context.Request.Headers[headerName] = headerValue;

            var nextInvoked = false;
            var middleware = new CsrfProtectionMiddleware(_ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, configService);
            nextInvoked.Should().BeTrue($"header {headerName} must bypass CSRF validation");
        }
    }

    [Test]
    public async Task CsrfProtectionMiddleware_CrossSiteSecFetchSiteAndMissingHeaders_BlocksWithForbidden()
    {
        var configService = Substitute.For<IConfigService>();
        configService.CsrfProtectionEnabled.Returns(true);

        // 1. Cross-site Sec-Fetch-Site blocked
        var context1 = new DefaultHttpContext();
        context1.Request.Method = "POST";
        context1.Request.Path = "/api/v1/torrents";
        context1.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        context1.Response.Body = new MemoryStream();

        var middleware = new CsrfProtectionMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context1, configService);

        context1.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 2. Missing both Origin and Referer blocked
        var context2 = new DefaultHttpContext();
        context2.Request.Method = "POST";
        context2.Request.Path = "/api/v1/torrents";
        context2.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context2, configService);
        context2.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 3. Invalid Origin blocked
        var context3 = new DefaultHttpContext();
        context3.Request.Method = "POST";
        context3.Request.Path = "/api/v1/torrents";
        context3.Request.Host = new HostString("myserver.lan:8080");
        context3.Request.Headers["Origin"] = "http://evil-attacker.com:8080";
        context3.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context3, configService);
        context3.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 4. Invalid Referer blocked
        var context4 = new DefaultHttpContext();
        context4.Request.Method = "POST";
        context4.Request.Path = "/api/v1/torrents";
        context4.Request.Host = new HostString("myserver.lan:8080");
        context4.Request.Headers["Referer"] = "http://evil-attacker.com/malicious";
        context4.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context4, configService);
        context4.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 5. Valid Origin allowed
        var context5 = new DefaultHttpContext();
        context5.Request.Method = "POST";
        context5.Request.Path = "/api/v1/torrents";
        context5.Request.Host = new HostString("myserver.lan:8080");
        context5.Request.Headers["Origin"] = "http://myserver.lan:8080";

        var nextCalled = false;
        var middlewareAllow = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middlewareAllow.InvokeAsync(context5, configService);
        nextCalled.Should().BeTrue();
    }

    [Test]
    public async Task SecurityHeadersMiddleware_StandardAndHttps_AppliesCorrectHeaders()
    {
        // 1. Plain HTTP request
        var httpContext = new DefaultHttpContext();
        var nextExecuted = false;
        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            nextExecuted = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);
        nextExecuted.Should().BeTrue();

        httpContext.Response.Headers[SecurityHeadersMiddleware.XFrameOptionsHeader].ToString().Should().Be("SAMEORIGIN");
        httpContext.Response.Headers[SecurityHeadersMiddleware.XContentTypeOptionsHeader].ToString().Should().Be("nosniff");
        httpContext.Response.Headers[SecurityHeadersMiddleware.ReferrerPolicyHeader].ToString().Should().Be("strict-origin-when-cross-origin");
        httpContext.Response.Headers[SecurityHeadersMiddleware.PermissionsPolicyHeader].ToString().Should().Contain("geolocation=()");
        httpContext.Response.Headers[SecurityHeadersMiddleware.ContentSecurityPolicyHeader].ToString().Should().Contain("default-src 'self'");
        httpContext.Response.Headers.ContainsKey(SecurityHeadersMiddleware.StrictTransportSecurityHeader).Should().BeFalse();

        // 2. HTTPS request applies HSTS
        var httpsContext = new DefaultHttpContext();
        httpsContext.Request.Scheme = "https";

        await middleware.InvokeAsync(httpsContext);
        httpsContext.Response.Headers[SecurityHeadersMiddleware.StrictTransportSecurityHeader].ToString().Should().Be("max-age=31536000; includeSubDomains");

        // 3. X-Forwarded-Proto applies HSTS
        var fwdContext = new DefaultHttpContext();
        fwdContext.Request.Headers["X-Forwarded-Proto"] = "https";

        await middleware.InvokeAsync(fwdContext);
        fwdContext.Response.Headers[SecurityHeadersMiddleware.StrictTransportSecurityHeader].ToString().Should().Be("max-age=31536000; includeSubDomains");
    }

    [Test]
    public void FacebookAuthHelper_GenerateAppSecretProof_ComputesHmacSha256Hex()
    {
        var accessToken = "EAABwzLixnjYBAK...";
        var appSecret = "0123456789abcdef0123456789abcdef";

        var proof = FacebookAuthHelper.GenerateAppSecretProof(accessToken, appSecret);

        proof.Should().NotBeNullOrWhiteSpace();
        proof.Length.Should().Be(64); // 32 bytes hex encoded = 64 characters

        // Verify independent computation matches
        var expectedHash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(accessToken))).ToLowerInvariant();
        proof.Should().Be(expectedHash);
    }

    [Test]
    public void TerminalEnvironmentSanitizer_SensitiveKeyIdentificationAndSanitization_CleansEnvironment()
    {
        // 1. Sensitive key detection
        TerminalEnvironmentSanitizer.IsSensitiveKey("PASSWORD").Should().BeTrue();
        TerminalEnvironmentSanitizer.IsSensitiveKey("MY_SECRET_TOKEN").Should().BeTrue();
        TerminalEnvironmentSanitizer.IsSensitiveKey("POSTGRES_DB").Should().BeTrue();
        TerminalEnvironmentSanitizer.IsSensitiveKey("API_KEY_VALUE").Should().BeTrue();
        TerminalEnvironmentSanitizer.IsSensitiveKey("DATABASE_URL").Should().BeTrue();
        TerminalEnvironmentSanitizer.IsSensitiveKey("JWT_SECRET").Should().BeTrue();
        TerminalEnvironmentSanitizer.IsSensitiveKey("BASIC_AUTH").Should().BeTrue();
        TerminalEnvironmentSanitizer.IsSensitiveKey("USER_CREDENTIAL").Should().BeTrue();
        TerminalEnvironmentSanitizer.IsSensitiveKey("SSH_PRIVATE_KEY").Should().BeTrue();
        TerminalEnvironmentSanitizer.IsSensitiveKey("LEECHARR_DOWNLOAD_DIR").Should().BeTrue();

        // Safe keys
        TerminalEnvironmentSanitizer.IsSensitiveKey(null).Should().BeFalse();
        TerminalEnvironmentSanitizer.IsSensitiveKey(string.Empty).Should().BeFalse();
        TerminalEnvironmentSanitizer.IsSensitiveKey("PATH").Should().BeFalse();
        TerminalEnvironmentSanitizer.IsSensitiveKey("HOME").Should().BeFalse();
        TerminalEnvironmentSanitizer.IsSensitiveKey("TERM").Should().BeFalse();

        // 2. Strip sensitive keys from ProcessStartInfo
        var startInfo = new ProcessStartInfo();
        startInfo.EnvironmentVariables["MY_API_KEY"] = "super_secret";
        startInfo.EnvironmentVariables["SAFE_SETTING"] = "hello";

        TerminalEnvironmentSanitizer.StripSensitiveKeys(startInfo);
        startInfo.EnvironmentVariables.ContainsKey("MY_API_KEY").Should().BeFalse();
        startInfo.EnvironmentVariables.ContainsKey("SAFE_SETTING").Should().BeTrue();

        // 3. Full sanitization
        TerminalEnvironmentSanitizer.Sanitize(startInfo);
        startInfo.Environment.ContainsKey("PATH").Should().BeTrue();
        startInfo.Environment.ContainsKey("TERM").Should().BeTrue();
        startInfo.Environment["TERM"].Should().Be("xterm-256color");
        startInfo.Environment["COLORTERM"].Should().Be("truecolor");
    }

    [Test]
    public async Task FallbackProcessSession_StartReadWriteResizeDispose_OperatesNormally()
    {
        var session = FallbackProcessSession.Start(Directory.GetCurrentDirectory(), 80, 24);
        try
        {
            session.ProcessId.Should().BeGreaterThan(0);
            session.IsActive.Should().BeTrue();

            session.Resize(100, 30);

            // Write an echo command
            var cmdBytes = Encoding.UTF8.GetBytes("echo 'FallbackSessionTestEcho'\n");
            await session.WriteAsync(cmdBytes, CancellationToken.None);

            // Read output
            var buffer = new byte[2048];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var bytesRead = await session.ReadAsync(buffer, cts.Token);
            bytesRead.Should().BeGreaterThan(0);
        }
        finally
        {
            await session.DisposeAsync();
            session.IsActive.Should().BeFalse();
        }
    }

    [Test]
    public async Task PtyTerminalService_AccessRestrictionsAndBoundClamping_EnforcesSecurity()
    {
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.TerminalAccessEnabled.Returns(false);

        var service = new PtyTerminalService(configFileProvider);
        service.IsTerminalAccessPermitted().Should().BeFalse();

        // Attempting to create session when disabled throws SecurityException
        var actDisabled = () => service.CreateSession(Directory.GetCurrentDirectory(), 80, 24);
        actDisabled.Should().Throw<SecurityException>();

        // When permitted, invalid cwd throws ArgumentException
        configFileProvider.TerminalAccessEnabled.Returns(true);
        service.IsTerminalAccessPermitted().Should().BeTrue();

        var actNullChar = () => service.CreateSession("path_with_\0_null", 80, 24);
        actNullChar.Should().Throw<ArgumentException>();

        // Valid session creation clamps cols/rows and initializes
        var validSession = service.CreateSession(Directory.GetCurrentDirectory(), 5, 2);
        try
        {
            validSession.Should().NotBeNull();
            validSession.ProcessId.Should().BeGreaterThan(0);
            validSession.IsActive.Should().BeTrue();
        }
        finally
        {
            await validSession.DisposeAsync();
        }
    }

    [Test]
    public async Task CookieSessionAuthenticationEvents_RedirectToLogin_DifferentiatesApiFromWeb()
    {
        var events = new CookieSessionAuthenticationEvents();

        // 1. API paths return 401 Unauthorized
        var apiPaths = new[]
        {
            "/api/v1/system/status",
            "/signalr/connect",
            "/transmission/rpc",
            "/jsonrpc",
            "/aria2",
            "/nzbget",
            "/hadouken",
            "/sabnzbd",
            "/gui",
        };

        foreach (var path in apiPaths)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = path;
            var redirectContext = new RedirectContext<CookieAuthenticationOptions>(
                httpContext,
                new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler)),
                new CookieAuthenticationOptions(),
                new AuthenticationProperties(),
                "http://localhost:7889/login");

            await events.RedirectToLogin(redirectContext);
            httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized, $"path {path} must return 401");
        }

        // 2. Web UI paths redirect to login URL
        var webContext = new DefaultHttpContext();
        webContext.Request.Path = "/settings/general";
        var webRedirectContext = new RedirectContext<CookieAuthenticationOptions>(
            webContext,
            new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationProperties(),
            "http://localhost:7889/login");

        await events.RedirectToLogin(webRedirectContext);
        webContext.Response.Headers.Location.ToString().Should().Be("http://localhost:7889/login");
    }

    [Test]
    public async Task CookieSessionManager_ValidatePrincipalAndSessionLifecycle_TracksAndEvicts()
    {
        var sessionRepo = Substitute.For<IUserSessionRepository>();
        var userRepo = Substitute.For<IUserRepository>();

        var manager = new CookieSessionManager(sessionRepo, userRepo, TimeSpan.FromMinutes(5), maxCacheCapacity: 3);

        // 1. Null context throws
        var actNull = () => manager.ValidatePrincipal(null);
        await actNull.Should().ThrowAsync<ArgumentNullException>();

        // 2. Principal without session claim rejected
        var httpContext1 = new DefaultHttpContext();
        var principalWithoutClaim = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Admin") }, "Cookies"));
        var validateContext1 = new CookieValidatePrincipalContext(
            httpContext1,
            new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(principalWithoutClaim, "Cookies"));

        await manager.ValidatePrincipal(validateContext1);
        validateContext1.Principal.Should().BeNull(); // Rejected

        // 3. Valid session lifecycle
        var validToken = "valid_session_token_123";
        var validSession = new UserSession
        {
            Id = 1,
            UserId = 10,
            SessionToken = validToken,
            Expiry = DateTime.UtcNow.AddDays(7),
            LastActivity = DateTime.UtcNow,
        };

        var validUser = new User
        {
            Id = 10,
            Username = "ValidUser",
        };

        sessionRepo.FindBySessionToken(validToken).Returns(validSession);
        userRepo.Get(10).Returns(validUser);

        var validClaims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "10"),
            new Claim(ClaimTypes.Name, "ValidUser"),
            new Claim("SessionId", validToken),
        };
        var validPrincipal = new ClaimsPrincipal(new ClaimsIdentity(validClaims, "Cookies"));
        var httpContext2 = new DefaultHttpContext();
        var validateContext2 = new CookieValidatePrincipalContext(
            httpContext2,
            new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(validPrincipal, "Cookies"));

        await manager.ValidatePrincipal(validateContext2);
        validateContext2.Principal.Should().NotBeNull();

        // 4. Session eviction and removal
        manager.Remove(validToken);
        manager.InvalidateCache(validToken);
        manager.ClearCache();
    }

    [Test]
    public async Task ForwardAuthHandler_AuthenticationFlow_HandlesUntrustedAndValidProxies()
    {
        var trustedService = new TrustedNetworkService();
        var jitProvisioning = Substitute.For<IJitUserProvisioningService>();
        var configService = Substitute.For<IConfigService>();

        configService.GetValue("ForwardAuthTrustedProxies", string.Empty).Returns("10.0.0.0/8,127.0.0.1");

        var optionsMonitor = Substitute.For<IOptionsMonitor<ForwardAuthOptions>>();
        optionsMonitor.Get(ForwardAuthOptions.DefaultScheme).Returns(new ForwardAuthOptions());

        ForwardAuthHandler CreateHandler(HttpContext context)
        {
            var h = new ForwardAuthHandler(
                optionsMonitor,
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                trustedService,
                jitProvisioning,
                configService);

            var scheme = new AuthenticationScheme(ForwardAuthOptions.DefaultScheme, null, typeof(ForwardAuthHandler));
            h.InitializeAsync(scheme, context).GetAwaiter().GetResult();
            return h;
        }

        // 1. Untrusted proxy returns NoResult
        var untrustedContext = new DefaultHttpContext();
        untrustedContext.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        untrustedContext.Request.Headers["X-authentik-username"] = "attacker";

        var untrustedHandler = CreateHandler(untrustedContext);
        var untrustedResult = await untrustedHandler.AuthenticateAsync();
        untrustedResult.None.Should().BeTrue();

        // 2. Trusted proxy with missing username returns NoResult
        var missingUserContext = new DefaultHttpContext();
        missingUserContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        var missingUserHandler = CreateHandler(missingUserContext);
        var missingUserResult = await missingUserHandler.AuthenticateAsync();
        missingUserResult.None.Should().BeTrue();

        // 3. Trusted proxy with valid forward auth headers succeeds and provisions user
        var validContext = new DefaultHttpContext();
        validContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        validContext.Request.Headers["X-authentik-username"] = "john_doe";
        validContext.Request.Headers["X-authentik-email"] = "john@example.com";
        validContext.Request.Headers["X-authentik-name"] = "John Doe";
        validContext.Request.Headers["X-authentik-groups"] = "Admin,PowerUser";

        var provisionedUser = new User
        {
            Id = 5,
            Username = "john_doe",
            DisplayName = "John Doe",
            Email = "john@example.com",
            Roles = "[\"Admin\",\"PowerUser\"]",
        };

        jitProvisioning.ProvisionOrUpdateUser(Arg.Any<ExternalUserProfile>()).Returns(provisionedUser);

        var validHandler = CreateHandler(validContext);
        var successResult = await validHandler.AuthenticateAsync();

        successResult.Succeeded.Should().BeTrue();
        successResult.Principal.Should().NotBeNull();
        successResult.Principal.Identity.Name.Should().Be("john_doe");
        successResult.Principal.IsInRole("Admin").Should().BeTrue();
    }

    [Test]
    public async Task DynamicAuthSchemeManager_RegisterOidcAndExecuteTokenValidated_ProvisionsUserAndSession()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var providerRepo = Substitute.For<IIdentityProviderRepository>();
        var jitProvisioning = Substitute.For<IJitUserProvisioningService>();
        var sessionRepo = Substitute.For<IUserSessionRepository>();
        var schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
        var oidcCache = Substitute.For<IOptionsMonitorCache<OpenIdConnectOptions>>();

        serviceProvider.GetService(typeof(IAuthenticationSchemeProvider)).Returns(schemeProvider);
        serviceProvider.GetService(typeof(IOptionsMonitorCache<OpenIdConnectOptions>)).Returns(oidcCache);
        serviceProvider.GetService(typeof(IUserSessionRepository)).Returns(sessionRepo);

        var manager = new DynamicAuthSchemeManager(serviceProvider, providerRepo, jitProvisioning, LogManager.GetCurrentClassLogger());

        var providerDef = new IdentityProviderDefinition
        {
            ProviderId = "test_oidc_provider",
            Name = "Corporate Keycloak",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://keycloak.corp.local/auth/realms/master",
            ClientId = "leecharr_client",
            ClientSecretEncrypted = "client_secret_xyz",
        };

        providerRepo.GetEnabled().Returns(new[] { providerDef });

        OpenIdConnectOptions capturedOptions = null;
        oidcCache.TryAdd(Arg.Any<string>(), Arg.Do<OpenIdConnectOptions>(opt => capturedOptions = opt)).Returns(true);

        // 1. Initialize configured providers
        await manager.InitializeConfiguredProvidersAsync();

        capturedOptions.Should().NotBeNull();
        capturedOptions.Authority.Should().Be("https://keycloak.corp.local/auth/realms/master");
        capturedOptions.ClientId.Should().Be("leecharr_client");

        // 2. Execute OnTokenValidated callback
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = serviceProvider;
        var tokenClaims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "sub_12345"),
            new Claim(ClaimTypes.Name, "corp_user"),
            new Claim(ClaimTypes.Email, "corp_user@corp.local"),
            new Claim("roles", "Admin"),
        };

        var tokenPrincipal = new ClaimsPrincipal(new ClaimsIdentity(tokenClaims, "OpenIdConnect"));
        var tokenContext = new TokenValidatedContext(
            httpContext,
            new AuthenticationScheme("Oidc_test_oidc_provider", null, typeof(OpenIdConnectHandler)),
            capturedOptions,
            tokenPrincipal,
            new AuthenticationProperties());

        var user = new User
        {
            Id = 99,
            Username = "corp_user",
            DisplayName = "Corporate User",
            Email = "corp_user@corp.local",
            Roles = "[\"Admin\"]",
        };

        jitProvisioning.ProvisionOrUpdateUser(Arg.Any<ExternalUserProfile>()).Returns(user);

        await capturedOptions.Events.OnTokenValidated(tokenContext);

        tokenContext.Principal.Should().NotBeNull();
        tokenContext.Principal.Identity.Name.Should().Be("corp_user");
        tokenContext.Principal.IsInRole("Admin").Should().BeTrue();
        sessionRepo.Received(1).Insert(Arg.Any<UserSession>());

        // 3. Remove scheme
        await manager.RemoveProviderSchemeAsync(providerDef.ProviderId);
        schemeProvider.Received(1).RemoveScheme("Oidc_test_oidc_provider");
        oidcCache.Received(2).TryRemove("Oidc_test_oidc_provider");
    }

    [Test]
    public void RpcAuthenticationHelper_IsAuthenticated_EvaluatesAllCredentials()
    {
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.AuthenticationEnabled.Returns(true);
        configFileProvider.ApiKey.Returns("master_rpc_secret_key");

        // 1. FixedTimeEquals helper
        RpcAuthenticationHelper.FixedTimeEquals(null, null).Should().BeTrue();
        RpcAuthenticationHelper.FixedTimeEquals("abc", "abc").Should().BeTrue();
        RpcAuthenticationHelper.FixedTimeEquals("abc", "def").Should().BeFalse();
        RpcAuthenticationHelper.FixedTimeEquals(null, "abc").Should().BeFalse();

        // 2. Disabled auth allows everything
        configFileProvider.AuthenticationEnabled.Returns(false);
        var disabledContext = new DefaultHttpContext();
        RpcAuthenticationHelper.IsAuthenticated(disabledContext, configFileProvider).Should().BeTrue();
        configFileProvider.AuthenticationEnabled.Returns(true);

        // 3. Authenticated User principal allows
        var userContext = new DefaultHttpContext();
        userContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Admin") }, "Cookies"));
        RpcAuthenticationHelper.IsAuthenticated(userContext, configFileProvider).Should().BeTrue();

        // 4. Header X-Api-Key matching
        var xApiKeyContext = new DefaultHttpContext();
        xApiKeyContext.Request.Headers["X-Api-Key"] = "master_rpc_secret_key";
        RpcAuthenticationHelper.IsAuthenticated(xApiKeyContext, configFileProvider).Should().BeTrue();

        // 5. Header ApiKey matching
        var apiKeyContext = new DefaultHttpContext();
        apiKeyContext.Request.Headers["ApiKey"] = "master_rpc_secret_key";
        RpcAuthenticationHelper.IsAuthenticated(apiKeyContext, configFileProvider).Should().BeTrue();

        // 6. Query parameters
        var queryParams = new[] { "apikey", "access_token", "api_key", "token" };
        foreach (var param in queryParams)
        {
            var queryContext = new DefaultHttpContext();
            queryContext.Request.QueryString = new QueryString($"?{param}=master_rpc_secret_key");
            RpcAuthenticationHelper.IsAuthenticated(queryContext, configFileProvider).Should().BeTrue($"query param {param} must authenticate");
        }

        // 7. Authorization: Bearer
        var bearerContext = new DefaultHttpContext();
        bearerContext.Request.Headers["Authorization"] = "Bearer master_rpc_secret_key";
        RpcAuthenticationHelper.IsAuthenticated(bearerContext, configFileProvider).Should().BeTrue();

        // 8. Authorization: Basic
        var basicValid1 = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:master_rpc_secret_key"));
        var basicContext1 = new DefaultHttpContext();
        basicContext1.Request.Headers["Authorization"] = $"Basic {basicValid1}";
        RpcAuthenticationHelper.IsAuthenticated(basicContext1, configFileProvider).Should().BeTrue();

        var basicValid2 = Convert.ToBase64String(Encoding.UTF8.GetBytes("master_rpc_secret_key:password"));
        var basicContext2 = new DefaultHttpContext();
        basicContext2.Request.Headers["Authorization"] = $"Basic {basicValid2}";
        RpcAuthenticationHelper.IsAuthenticated(basicContext2, configFileProvider).Should().BeTrue();

        // 9. Invalid credentials reject
        var invalidContext = new DefaultHttpContext();
        invalidContext.Request.Headers["X-Api-Key"] = "wrong_key";
        RpcAuthenticationHelper.IsAuthenticated(invalidContext, configFileProvider).Should().BeFalse();
    }

    [Test]
    public async Task BasicAuthenticationHandler_HandleAuthenticate_ValidatesAllBranches()
    {
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.AuthenticationEnabled.Returns(true);
        configFileProvider.ApiKey.Returns("master_api_secret");

        var userService = Substitute.For<IUserService>();
        using var rateLimiter = new AuthRateLimiter(maxFailedAttempts: 2, attemptWindow: TimeSpan.FromMinutes(1), lockoutDuration: TimeSpan.FromMinutes(5));

        BasicAuthenticationHandler CreateBasicHandler(HttpContext ctx)
        {
            var opts = Substitute.For<IOptionsMonitor<BasicAuthenticationOptions>>();
            opts.Get(BasicAuthenticationOptions.DefaultScheme).Returns(new BasicAuthenticationOptions());
            var h = new BasicAuthenticationHandler(
                opts,
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                configFileProvider,
                userService,
                rateLimiter);

            var scheme = new AuthenticationScheme(BasicAuthenticationOptions.DefaultScheme, null, typeof(BasicAuthenticationHandler));
            h.InitializeAsync(scheme, ctx).GetAwaiter().GetResult();
            return h;
        }

        // 1. Missing Authorization header returns NoResult
        var ctx1 = new DefaultHttpContext();
        var res1 = await CreateBasicHandler(ctx1).AuthenticateAsync();
        res1.None.Should().BeTrue();

        // 2. Non-Basic scheme returns NoResult
        var ctx2 = new DefaultHttpContext();
        ctx2.Request.Headers["Authorization"] = "Bearer some_token";
        var res2 = await CreateBasicHandler(ctx2).AuthenticateAsync();
        res2.None.Should().BeTrue();

        // 3. Basic auth matching master API key succeeds
        var ctx3 = new DefaultHttpContext();
        var creds3 = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:master_api_secret"));
        ctx3.Request.Headers["Authorization"] = $"Basic {creds3}";
        var res3 = await CreateBasicHandler(ctx3).AuthenticateAsync();
        res3.Succeeded.Should().BeTrue();
        res3.Principal.Identity.Name.Should().Be("admin");
        res3.Principal.IsInRole("Admin").Should().BeTrue();

        // 4. Basic auth matching user in database succeeds
        var ctx4 = new DefaultHttpContext();
        var creds4 = Convert.ToBase64String(Encoding.UTF8.GetBytes("db_user:password123"));
        ctx4.Request.Headers["Authorization"] = $"Basic {creds4}";

        var dbUser = new User
        {
            Id = 42,
            Username = "db_user",
            Roles = "[\"User\"]",
        };
        userService.Authenticate("db_user", "password123").Returns(dbUser);

        var res4 = await CreateBasicHandler(ctx4).AuthenticateAsync();
        res4.Succeeded.Should().BeTrue();
        res4.Principal.Identity.Name.Should().Be("db_user");
        res4.Principal.IsInRole("User").Should().BeTrue();

        // 5. Invalid password fails and records failure
        var ctx5 = new DefaultHttpContext();
        ctx5.Connection.RemoteIpAddress = IPAddress.Parse("192.168.10.5");
        var creds5 = Convert.ToBase64String(Encoding.UTF8.GetBytes("db_user:wrong_pass"));
        ctx5.Request.Headers["Authorization"] = $"Basic {creds5}";
        userService.Authenticate("db_user", "wrong_pass").Returns((User)null);

        var res5 = await CreateBasicHandler(ctx5).AuthenticateAsync();
        res5.Succeeded.Should().BeFalse();
        res5.Failure.Message.Should().Contain("Invalid Basic authentication credentials");

        // 6. Second failure triggers rate limiting
        var ctx6 = new DefaultHttpContext();
        ctx6.Connection.RemoteIpAddress = IPAddress.Parse("192.168.10.5");
        ctx6.Request.Headers["Authorization"] = $"Basic {creds5}";

        var res6 = await CreateBasicHandler(ctx6).AuthenticateAsync();
        res6.Succeeded.Should().BeFalse();

        // 7. Throttled attempt
        var ctx7 = new DefaultHttpContext();
        ctx7.Connection.RemoteIpAddress = IPAddress.Parse("192.168.10.5");
        ctx7.Request.Headers["Authorization"] = $"Basic {creds5}";

        var res7 = await CreateBasicHandler(ctx7).AuthenticateAsync();
        res7.Succeeded.Should().BeFalse();
        res7.Failure.Message.Should().Contain("Too many failed authentication attempts");
    }

    [Test]
    public async Task ApiKeyAuthenticationHandler_HandleAuthenticate_ValidatesAllBranches()
    {
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.AuthenticationEnabled.Returns(true);
        configFileProvider.ApiKey.Returns("master_api_secret");

        using var rateLimiter = new AuthRateLimiter(maxFailedAttempts: 2, attemptWindow: TimeSpan.FromMinutes(1), lockoutDuration: TimeSpan.FromMinutes(5));

        ApiKeyAuthenticationHandler CreateApiKeyHandler(HttpContext ctx)
        {
            var opts = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
            opts.Get(ApiKeyAuthenticationOptions.DefaultScheme).Returns(new ApiKeyAuthenticationOptions());
            var h = new ApiKeyAuthenticationHandler(
                opts,
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                configFileProvider,
                rateLimiter);

            var scheme = new AuthenticationScheme(ApiKeyAuthenticationOptions.DefaultScheme, null, typeof(ApiKeyAuthenticationHandler));
            h.InitializeAsync(scheme, ctx).GetAwaiter().GetResult();
            return h;
        }

        // 1. Auth disabled returns Success
        configFileProvider.AuthenticationEnabled.Returns(false);
        var ctx1 = new DefaultHttpContext();
        var res1 = await CreateApiKeyHandler(ctx1).AuthenticateAsync();
        res1.Succeeded.Should().BeTrue();
        res1.Principal.Identity.Name.Should().Be("Admin");
        configFileProvider.AuthenticationEnabled.Returns(true);

        // 2. Missing API key returns NoResult
        var ctx2 = new DefaultHttpContext();
        var res2 = await CreateApiKeyHandler(ctx2).AuthenticateAsync();
        res2.None.Should().BeTrue();

        // 3. Valid key via X-Api-Key
        var ctx3 = new DefaultHttpContext();
        ctx3.Request.Headers["X-Api-Key"] = "master_api_secret";
        var res3 = await CreateApiKeyHandler(ctx3).AuthenticateAsync();
        res3.Succeeded.Should().BeTrue();
        res3.Principal.Identity.Name.Should().Be("MasterApiKey");

        // 4. Valid key via query param
        var queryParams = new[] { "apikey", "access_token", "api_key" };
        foreach (var qp in queryParams)
        {
            var ctxQ = new DefaultHttpContext();
            ctxQ.Request.QueryString = new QueryString($"?{qp}=master_api_secret");
            var resQ = await CreateApiKeyHandler(ctxQ).AuthenticateAsync();
            resQ.Succeeded.Should().BeTrue($"query parameter {qp} must succeed");
        }

        // 5. Valid key via Bearer header
        var ctxBearer = new DefaultHttpContext();
        ctxBearer.Request.Headers["Authorization"] = "Bearer master_api_secret";
        var resBearer = await CreateApiKeyHandler(ctxBearer).AuthenticateAsync();
        resBearer.Succeeded.Should().BeTrue();

        // 6. Invalid key fails
        var ctxInvalid = new DefaultHttpContext();
        ctxInvalid.Connection.RemoteIpAddress = IPAddress.Parse("10.50.60.70");
        ctxInvalid.Request.Headers["X-Api-Key"] = "invalid_secret";
        var resInvalid = await CreateApiKeyHandler(ctxInvalid).AuthenticateAsync();
        resInvalid.Succeeded.Should().BeFalse();
        resInvalid.Failure.Message.Should().Contain("Invalid API Key");

        // 7. Second invalid triggers throttling
        var resInvalid2 = await CreateApiKeyHandler(ctxInvalid).AuthenticateAsync();
        resInvalid2.Succeeded.Should().BeFalse();

        var resThrottled = await CreateApiKeyHandler(ctxInvalid).AuthenticateAsync();
        resThrottled.Succeeded.Should().BeFalse();
        resThrottled.Failure.Message.Should().Contain("Too many failed authentication attempts");
    }

    [Test]
    public async Task CookieSessionAuthenticationEvents_ValidatePrincipal_ExecutesConfiguredManager()
    {
        var manager = Substitute.For<ICookieSessionManager>();
        var events = new CookieSessionAuthenticationEvents(cookieSessionManager: manager);

        var httpContext = new DefaultHttpContext();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Admin") }, "Cookies"));
        var validateContext = new CookieValidatePrincipalContext(
            httpContext,
            new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(principal, "Cookies"));

        await events.ValidatePrincipal(validateContext);
        await manager.Received(1).ValidatePrincipal(validateContext);
    }

    [Test]
    public void RestControllerWithSignalR_HandleModelEvent_BroadcastsSignalRMessage()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(true);

        var controller = new TestRestControllerWithSignalR(broadcaster);

        // Created event
        var model = new TestModel { Id = 42 };
        controller.Handle(new ModelEvent<TestModel>(model, ModelAction.Created));
        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TestItemAdded" && m.Action == ModelAction.Created));

        // Updated event
        controller.Handle(new ModelEvent<TestModel>(model, ModelAction.Updated));
        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TestItemUpdated" && m.Action == ModelAction.Updated));

        // Deleted event
        controller.Handle(new ModelEvent<TestModel>(model, ModelAction.Deleted));
        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TestItemDeleted" && m.Action == ModelAction.Deleted));

        // Null message or disconnected broadcaster does nothing
        broadcaster.ClearReceivedCalls();
        controller.Handle(null);
        broadcaster.DidNotReceiveWithAnyArgs().BroadcastMessage(default);

        broadcaster.IsConnected.Returns(false);
        controller.Handle(new ModelEvent<TestModel>(model, ModelAction.Created));
        broadcaster.DidNotReceiveWithAnyArgs().BroadcastMessage(default);
    }

    private class TestRestResource : RestResource
    {
        public override string ResourceName => "TestItem";
    }

    private class TestModel : ModelBase
    {
    }

    private class TestRestControllerWithSignalR : RestControllerWithSignalR<TestRestResource, TestModel>
    {
        public TestRestControllerWithSignalR(IBroadcastSignalRMessage signalRBroadcaster)
            : base(signalRBroadcaster)
        {
        }

        protected override TestRestResource GetResourceById(TestModel model)
        {
            return new TestRestResource { Id = model.Id };
        }
    }
}

// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Leecharr.Http.Authentication;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class HttpSecurityAndAuthenticationComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public void HostHeaderValidation_IsHostAllowed_ValidatesHostsCorrectly()
    {
        // 1. Loopback hosts always allowed
        HostHeaderValidationMiddleware.IsHostAllowed("localhost", "example.com").Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("127.0.0.1", "example.com").Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("::1", "example.com").Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("[::1]", "example.com").Should().BeTrue();

        // 2. Explicit allowed hosts
        var config = "app.local,media.server.org,*.wildcard.io";
        HostHeaderValidationMiddleware.IsHostAllowed("app.local", config).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("media.server.org", config).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("sub.wildcard.io", config).Should().BeTrue();

        // 3. Allowed hosts with whitespace or wildcard
        HostHeaderValidationMiddleware.IsHostAllowed("  app.local  ", config).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("deep.sub.wildcard.io", config).Should().BeTrue();

        // 4. Disallowed hosts
        HostHeaderValidationMiddleware.IsHostAllowed("evil.attacker.com", config).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("other.io", config).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("", config).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("   ", config).Should().BeFalse();
    }

    [Test]
    public void ClientIpResolver_ResolveClientIp_ResolvesProxiesAndHeaders()
    {
        var trustedService = Substitute.For<ITrustedNetworkService>();
        var configService = Substitute.For<IConfigService>();

        // 1. Null context returns loopback
        ClientIpResolver.ResolveClientIp(null).Should().Be("127.0.0.1");

        // 2. Untrusted proxy returns remote IP directly
        var context1 = new DefaultHttpContext();
        context1.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        context1.Request.Headers["X-Forwarded-For"] = "203.0.113.195";
        trustedService.IsTrustedProxy(Arg.Any<IPAddress>(), Arg.Any<string>()).Returns(false);

        var resolvedDirect = ClientIpResolver.ResolveClientIp(context1, trustedService, configService);
        resolvedDirect.Should().Be("198.51.100.1");

        // 3. Trusted proxy resolves X-Forwarded-For client IP
        var context2 = new DefaultHttpContext();
        context2.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context2.Request.Headers["X-Forwarded-For"] = "203.0.113.50, 10.0.0.1";
        trustedService.IsTrustedProxy(Arg.Any<IPAddress>(), Arg.Any<string>()).Returns(true);

        var resolvedForwarded = ClientIpResolver.ResolveClientIp(context2, trustedService, configService);
        resolvedForwarded.Should().Be("203.0.113.50");

        // 4. Trusted proxy resolves X-Real-IP
        var context3 = new DefaultHttpContext();
        context3.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context3.Request.Headers["X-Real-IP"] = "192.0.2.77";

        var resolvedReal = ClientIpResolver.ResolveClientIp(context3, trustedService, configService);
        resolvedReal.Should().Be("192.0.2.77");
    }

    [Test]
    public void RpcSessionStore_ManageSessions_TracksExpiryAndPrunesCleanly()
    {
        var store = new RpcSessionStore(maxCapacity: 100);

        // 1. Set session
        var token = "rpc_token_abc_123";
        store.SetSession(token, DateTime.UtcNow.AddMinutes(30));
        store.IsValid(token).Should().BeTrue();
        store.Count.Should().Be(1);

        // 2. Expired session
        var expiredToken = "rpc_token_expired";
        store.SetSession(expiredToken, DateTime.UtcNow.AddMinutes(-5));
        store.IsValid(expiredToken).Should().BeFalse();

        // 3. Clear and Invalidate
        store.Clear();
        store.Count.Should().Be(0);
        store.IsValid(token).Should().BeFalse();

        // 4. InvalidateAllSessions
        var token2 = "rpc_token_static_test";
        store.SetSession(token2, DateTime.UtcNow.AddHours(1));
        RpcSessionStore.InvalidateAllSessions();
        store.IsValid(token2).Should().BeFalse();
    }

    [Test]
    public void AuthRateLimiter_RateLimitingLifecycle_ThrottlesAndResets()
    {
        using var limiter = new AuthRateLimiter(
            maxFailedAttempts: 3,
            attemptWindow: TimeSpan.FromMinutes(1),
            lockoutDuration: TimeSpan.FromMinutes(5));

        var key = "192.168.1.50";

        // 1. Initially not throttled
        limiter.IsThrottled(key).Should().BeFalse();

        // 2. Record failures up to threshold
        limiter.RecordFailure(key);
        limiter.IsThrottled(key).Should().BeFalse();

        limiter.RecordFailure(key);
        limiter.IsThrottled(key).Should().BeFalse();

        limiter.RecordFailure(key);
        limiter.IsThrottled(key).Should().BeTrue();
        limiter.TrackedIpCount.Should().Be(1);

        // 3. Reset resets the throttle
        limiter.Reset(key);
        limiter.IsThrottled(key).Should().BeFalse();

        // 4. ResetAll clears all
        limiter.RecordFailure(key);
        limiter.RecordFailure(key);
        limiter.RecordFailure(key);
        limiter.IsThrottled(key).Should().BeTrue();
        limiter.ResetAll();
        limiter.IsThrottled(key).Should().BeFalse();
        limiter.TrackedIpCount.Should().Be(0);
    }

    [Test]
    public void AppleClientSecretGenerator_GenerateClientSecret_ProducesValidJwt()
    {
        // 1. Generate an ECDsa P-256 private key
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportPkcs8PrivateKeyPem();

        // 2. Generate Apple Client Secret JWT
        var token = AppleClientSecretGenerator.GenerateClientSecret(
            teamId: "DEF1234567",
            clientId: "com.leecharr.web",
            keyId: "KEY890ABCD",
            privateKeyPem: pem,
            expirationMinutes: 60);

        token.Should().NotBeNullOrWhiteSpace();
        var parts = token.Split('.');
        parts.Should().HaveCount(3);

        // 3. Validate header
        var headerBytes = Convert.FromBase64String(PadBase64(parts[0]));
        using var headerDoc = JsonDocument.Parse(headerBytes);
        headerDoc.RootElement.GetProperty("alg").GetString().Should().Be("ES256");
        headerDoc.RootElement.GetProperty("kid").GetString().Should().Be("KEY890ABCD");

        // 4. Validate payload
        var payloadBytes = Convert.FromBase64String(PadBase64(parts[1]));
        using var payloadDoc = JsonDocument.Parse(payloadBytes);
        payloadDoc.RootElement.GetProperty("iss").GetString().Should().Be("DEF1234567");
        payloadDoc.RootElement.GetProperty("sub").GetString().Should().Be("com.leecharr.web");
        payloadDoc.RootElement.GetProperty("aud").GetString().Should().Be("https://appleid.apple.com");
    }

    private static string PadBase64(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }

        return output;
    }
}

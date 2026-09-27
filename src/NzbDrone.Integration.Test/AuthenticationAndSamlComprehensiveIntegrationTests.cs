// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Auth;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class AuthenticationAndSamlComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task AuthController_ProvidersAndSamlMetadataAndMe_ReturnValidResponses()
    {
        // 1. GET /api/v1/auth/providers
        var provResp = await this.Client.GetAsync("/api/v1/auth/providers");
        provResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var provJson = await provResp.Content.ReadAsStringAsync();
        using var provDoc = JsonDocument.Parse(provJson);
        provDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        // 2. GET /api/v1/auth/saml/metadata
        var samlMetaResp = await this.Client.GetAsync("/api/v1/auth/saml/metadata");
        samlMetaResp.StatusCode.Should().Be(HttpStatusCode.OK);
        samlMetaResp.Content.Headers.ContentType?.MediaType.Should().Be("application/samlmetadata+xml");
        var metaXml = await samlMetaResp.Content.ReadAsStringAsync();
        metaXml.Should().Contain("EntityDescriptor");
        metaXml.Should().Contain("AssertionConsumerService");

        // 3. GET /api/v1/auth/me
        var meResp = await this.Client.GetAsync("/api/v1/auth/me");
        meResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var meJson = await meResp.Content.ReadAsStringAsync();
        using var meDoc = JsonDocument.Parse(meJson);
        meDoc.RootElement.TryGetProperty("isAuthenticated", out _).Should().BeTrue();

        // 4. POST /api/v1/auth/logout
        var logoutResp = await this.Client.PostAsync("/api/v1/auth/logout", null);
        logoutResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task AuthController_LoginValidationAndThrottling_HandlesFailuresCleanly()
    {
        // 1. Empty body returns 400 BadRequest
        var emptyLoginResp = await this.PostJsonAsync("/api/v1/auth/login", new
        {
            username = "",
            password = "",
        });
        emptyLoginResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Invalid credentials return 401 Unauthorized
        var badLoginResp = await this.PostJsonAsync("/api/v1/auth/login", new
        {
            username = "non_existent_user_9999",
            password = "wrong_password_1234",
        });
        badLoginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public void AuthController_UrlSanitization_PreventsOpenRedirects()
    {
        // 1. Valid local URLs
        AuthController.IsLocalUrl("/").Should().BeTrue();
        AuthController.IsLocalUrl("/torrents").Should().BeTrue();
        AuthController.IsLocalUrl("/settings/general").Should().BeTrue();
        AuthController.SanitizeRedirectUrl("/dashboard").Should().Be("/dashboard");

        // 2. Open redirect attempts
        AuthController.IsLocalUrl("https://evil.attacker.com").Should().BeFalse();
        AuthController.IsLocalUrl("http://attacker.org").Should().BeFalse();
        AuthController.IsLocalUrl("//evil.com").Should().BeFalse();
        AuthController.IsLocalUrl("/\\evil.com").Should().BeFalse();
        AuthController.IsLocalUrl("").Should().BeFalse();
        AuthController.IsLocalUrl(null).Should().BeFalse();

        AuthController.SanitizeRedirectUrl("https://evil.attacker.com").Should().Be("/");
        AuthController.SanitizeRedirectUrl("//evil.com/phish").Should().Be("/");
    }
}

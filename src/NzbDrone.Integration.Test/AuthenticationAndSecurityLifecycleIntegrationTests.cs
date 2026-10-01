// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class AuthenticationAndSecurityLifecycleIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Auth_ProvidersAndMeEndpoints_ReturnConfiguredData()
    {
        // 1. GET /api/v1/auth/providers
        var provResp = await this.Client.GetAsync("/api/v1/auth/providers");
        provResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var provDoc = JsonDocument.Parse(await provResp.Content.ReadAsStringAsync());
        provDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        // 2. GET /api/v1/auth/me
        var meResp = await this.Client.GetAsync("/api/v1/auth/me");
        meResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Auth_LoginAndLogout_HandlesAuthenticationLifecycle()
    {
        // 1. Invalid login credentials returns 401 Unauthorized or BadRequest
        var invalidLogin = new
        {
            username = "non_existent_admin",
            password = "wrong_password_12345",
        };
        var loginResp = await this.PostJsonAsync("/api/v1/auth/login", invalidLogin);
        loginResp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);

        // 2. Query SAML metadata endpoint
        var samlMetaResp = await this.Client.GetAsync("/api/v1/auth/saml/metadata");
        samlMetaResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.BadRequest);

        // 3. Query SAML login for non-existent provider returns NotFound
        var unknownLoginResp = await this.Client.GetAsync("/api/v1/auth/login/saml/non_existent_provider");
        unknownLoginResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 4. Logout endpoint succeeds cleanly
        var logoutResp = await this.PostJsonAsync("/api/v1/auth/logout", new { });
        logoutResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

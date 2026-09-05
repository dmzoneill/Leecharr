// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NLog;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class DynamicAuthSchemeManagerTest
{
    private IServiceProvider serviceProvider = null!;
    private IIdentityProviderRepository identityProviderRepository = null!;
    private IJitUserProvisioningService jitUserProvisioning = null!;
    private IUserSessionRepository userSessionRepository = null!;
    private IOptionsMonitorCache<OpenIdConnectOptions> oidcOptionsCache = null!;
    private IAuthenticationSchemeProvider schemeProvider = null!;
    private DynamicAuthSchemeManager manager = null!;

    [SetUp]
    public void SetUp()
    {
        this.serviceProvider = Substitute.For<IServiceProvider>();
        this.identityProviderRepository = Substitute.For<IIdentityProviderRepository>();
        this.jitUserProvisioning = Substitute.For<IJitUserProvisioningService>();
        this.userSessionRepository = Substitute.For<IUserSessionRepository>();
        this.oidcOptionsCache = Substitute.For<IOptionsMonitorCache<OpenIdConnectOptions>>();
        this.schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();

        this.serviceProvider.GetService(typeof(IOptionsMonitorCache<OpenIdConnectOptions>)).Returns(this.oidcOptionsCache);
        this.serviceProvider.GetService(typeof(IAuthenticationSchemeProvider)).Returns(this.schemeProvider);
        this.serviceProvider.GetService(typeof(IUserSessionRepository)).Returns(this.userSessionRepository);

        this.manager = new DynamicAuthSchemeManager(
            this.serviceProvider,
            this.identityProviderRepository,
            this.jitUserProvisioning,
            LogManager.GetCurrentClassLogger());
    }

    [Test]
    public async Task RegisterOrUpdateOidcProviderAsync_WhenTokenValidated_BuildsSessionClaimsAndPersistsUserSession()
    {
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test-oidc",
            Name = "Test OIDC",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://auth.example.com",
            ClientId = "test-client-id",
        };

        OpenIdConnectOptions capturedOptions = null!;
        this.oidcOptionsCache.TryAdd(Arg.Any<string>(), Arg.Do<OpenIdConnectOptions>(opt => capturedOptions = opt)).Returns(true);

        await this.manager.RegisterOrUpdateOidcProviderAsync(provider);

        capturedOptions.Should().NotBeNull();
        capturedOptions.Events.Should().NotBeNull();
        capturedOptions.Events.OnTokenValidated.Should().NotBeNull();

        var provisionedUser = new User
        {
            Id = 42,
            Username = "oidcuser",
            DisplayName = "OIDC Test User",
            Email = "oidc@example.com",
            Roles = "[\"Admin\",\"User\"]",
        };

        this.jitUserProvisioning.ProvisionOrUpdateUser(Arg.Any<ExternalUserProfile>()).Returns(provisionedUser);

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = this.serviceProvider;

        var principalIdentity = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "sub-12345"),
                new Claim("preferred_username", "oidcuser"),
                new Claim("email", "oidc@example.com"),
            },
            "OIDC"));

        var tokenValidatedContext = new TokenValidatedContext(
            httpContext,
            new AuthenticationScheme("Oidc_test-oidc", "Test OIDC", typeof(OpenIdConnectHandler)),
            capturedOptions,
            principalIdentity,
            new AuthenticationProperties());

        await capturedOptions.Events.OnTokenValidated(tokenValidatedContext);

        tokenValidatedContext.Principal.Should().NotBeNull();
        var principal = tokenValidatedContext.Principal!;
        principal.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be("42");
        principal.FindFirst(ClaimTypes.Name)?.Value.Should().Be("oidcuser");
        principal.FindFirst("DisplayName")?.Value.Should().Be("OIDC Test User");
        principal.FindFirst("SessionId")?.Value.Should().NotBeNullOrWhiteSpace();
        principal.FindFirst("TicketId")?.Value.Should().NotBeNullOrWhiteSpace();
        principal.FindFirst("SessionToken")?.Value.Should().NotBeNullOrWhiteSpace();
        principal.FindAll(ClaimTypes.Role).Select(r => r.Value).Should().Contain("Admin").And.Contain("User");

        var sessionToken = principal.FindFirst("SessionToken")!.Value;
        this.userSessionRepository.Received(1).Insert(Arg.Is<UserSession>(s =>
            s.UserId == 42 &&
            s.SessionToken == sessionToken));
    }
}

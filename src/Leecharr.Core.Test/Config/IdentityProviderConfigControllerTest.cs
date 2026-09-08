// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Config;
using Leecharr.Http.Authentication;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace Leecharr.Core.Test.Config;

[TestFixture]
public class IdentityProviderConfigControllerTest
{
    private IIdentityProviderService providerService = null!;
    private IDynamicAuthSchemeManager dynamicAuthManager = null!;
    private IdentityProviderConfigController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.providerService = Substitute.For<IIdentityProviderService>();
        this.dynamicAuthManager = Substitute.For<IDynamicAuthSchemeManager>();
        this.controller = new IdentityProviderConfigController(this.providerService, this.dynamicAuthManager);
    }

    [Test]
    public void GetAll_ReturnsMappedResourcesWithMaskedSecrets()
    {
        var providers = new List<IdentityProviderDefinition>
        {
            new()
            {
                Id = 1,
                ProviderId = "entra",
                Name = "Microsoft Entra ID",
                ProviderType = IdentityProviderType.Oidc,
                IsEnabled = true,
                ClientId = "entra-client-id",
                ClientSecretEncrypted = "real-secret-123",
            },
            new()
            {
                Id = 2,
                ProviderId = "auth0",
                Name = "Auth0",
                ProviderType = IdentityProviderType.Oidc,
                IsEnabled = false,
                ClientId = "auth0-client-id",
                ClientSecretEncrypted = null,
            },
        };

        this.providerService.GetAll().Returns(providers);

        var result = this.controller.GetAll();

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var list = ok.Value as List<IdentityProviderResource>;
        list.Should().NotBeNull();
        list!.Count.Should().Be(2);

        list[0].ProviderId.Should().Be("entra");
        list[0].ClientSecret.Should().Be("********");

        list[1].ProviderId.Should().Be("auth0");
        list[1].ClientSecret.Should().BeNull();
    }

    [Test]
    public void GetById_WhenExists_ReturnsResourceWithMaskedSecret()
    {
        var provider = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "keycloak",
            Name = "Keycloak",
            ClientSecretEncrypted = "secret-with*asterisk",
        };

        this.providerService.GetById(1).Returns(provider);

        var result = this.controller.GetById(1);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var res = ok.Value as IdentityProviderResource;
        res.Should().NotBeNull();
        res!.ClientSecret.Should().Be("********");
    }

    [Test]
    public void GetById_WhenNotFound_ReturnsNotFound()
    {
        this.providerService.GetById(99).Returns((IdentityProviderDefinition)null);

        var result = this.controller.GetById(99);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Create_WhenValid_AddsProviderAndRegistersScheme()
    {
        var resource = new IdentityProviderResource
        {
            ProviderId = "okta",
            Name = "Okta SSO",
            IsEnabled = true,
            ClientId = "okta-client",
            ClientSecret = "secret*with*asterisks!123",
        };

        this.providerService.Add(Arg.Any<IdentityProviderDefinition>()).Returns(callInfo =>
        {
            var def = callInfo.Arg<IdentityProviderDefinition>();
            def.Id = 42;
            return def;
        });

        var result = this.controller.Create(resource);

        result.Result.Should().BeOfType<CreatedResult>();
        var created = (CreatedResult)result.Result!;
        created.Location.Should().Be("/api/v1/config/auth/providers/42");

        this.providerService.Received(1).Add(Arg.Is<IdentityProviderDefinition>(p =>
            p.ProviderId == "okta" &&
            p.ClientSecretEncrypted == "secret*with*asterisks!123"));

        _ = this.dynamicAuthManager.Received(1).RegisterOrUpdateOidcProviderAsync(Arg.Is<IdentityProviderDefinition>(p => p.Id == 42));
    }

    [Test]
    public void Create_WhenNull_ReturnsBadRequest()
    {
        var result = this.controller.Create(null!);

        result.Result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public void Update_WhenNull_ReturnsBadRequest()
    {
        var result = this.controller.Update(1, null!);

        result.Result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public void Update_WhenNotFound_ReturnsNotFound()
    {
        this.providerService.GetById(99).Returns((IdentityProviderDefinition)null);

        var result = this.controller.Update(99, new IdentityProviderResource { ProviderId = "test", Name = "Test" });

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Update_WhenSecretIsMaskedWithEightAsterisks_PreservesExistingSecret()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "authentik",
            Name = "Authentik",
            ClientSecretEncrypted = "existing-real-secret",
            IsEnabled = true,
        };

        this.providerService.GetById(1).Returns(existing);
        this.providerService.Update(Arg.Any<IdentityProviderDefinition>()).Returns(x => x.Arg<IdentityProviderDefinition>());

        var resource = new IdentityProviderResource
        {
            ProviderId = "authentik",
            Name = "Authentik Updated",
            IsEnabled = true,
            ClientSecret = "********",
        };

        var result = this.controller.Update(1, resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.providerService.Received(1).Update(Arg.Is<IdentityProviderDefinition>(p =>
            p.Id == 1 &&
            p.ClientSecretEncrypted == "existing-real-secret" &&
            p.Name == "Authentik Updated"));
    }

    [Test]
    public void Update_WhenSecretIsMaskedWithSixAsterisks_PreservesExistingSecret()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "authentik",
            Name = "Authentik",
            ClientSecretEncrypted = "existing-real-secret",
            IsEnabled = true,
        };

        this.providerService.GetById(1).Returns(existing);
        this.providerService.Update(Arg.Any<IdentityProviderDefinition>()).Returns(x => x.Arg<IdentityProviderDefinition>());

        var resource = new IdentityProviderResource
        {
            ProviderId = "authentik",
            Name = "Authentik Updated",
            IsEnabled = true,
            ClientSecret = "******",
        };

        var result = this.controller.Update(1, resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.providerService.Received(1).Update(Arg.Is<IdentityProviderDefinition>(p =>
            p.Id == 1 &&
            p.ClientSecretEncrypted == "existing-real-secret"));
    }

    [Test]
    public void Update_WhenSecretIsNullOrEmpty_PreservesExistingSecret()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "authentik",
            Name = "Authentik",
            ClientSecretEncrypted = "existing-real-secret",
            IsEnabled = true,
        };

        this.providerService.GetById(1).Returns(existing);
        this.providerService.Update(Arg.Any<IdentityProviderDefinition>()).Returns(x => x.Arg<IdentityProviderDefinition>());

        var resource = new IdentityProviderResource
        {
            ProviderId = "authentik",
            Name = "Authentik Updated",
            IsEnabled = true,
            ClientSecret = string.Empty,
        };

        var result = this.controller.Update(1, resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.providerService.Received(1).Update(Arg.Is<IdentityProviderDefinition>(p =>
            p.Id == 1 &&
            p.ClientSecretEncrypted == "existing-real-secret"));
    }

    [Test]
    public void Update_WhenValidSecretContainsAsterisks_SavesNewSecretProperly()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "azure-entra",
            Name = "Microsoft Entra ID",
            ClientSecretEncrypted = "old-secret-value",
            IsEnabled = true,
        };

        this.providerService.GetById(1).Returns(existing);
        this.providerService.Update(Arg.Any<IdentityProviderDefinition>()).Returns(x => x.Arg<IdentityProviderDefinition>());

        var resource = new IdentityProviderResource
        {
            ProviderId = "azure-entra",
            Name = "Microsoft Entra ID",
            IsEnabled = true,
            ClientSecret = "abc*123*xyz!~*key",
        };

        var result = this.controller.Update(1, resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.providerService.Received(1).Update(Arg.Is<IdentityProviderDefinition>(p =>
            p.Id == 1 &&
            p.ClientSecretEncrypted == "abc*123*xyz!~*key"));
    }

    [Test]
    public void Update_WhenDisabled_RemovesProviderScheme()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test-provider",
            Name = "Test Provider",
            IsEnabled = true,
        };

        this.providerService.GetById(1).Returns(existing);
        this.providerService.Update(Arg.Any<IdentityProviderDefinition>()).Returns(x => x.Arg<IdentityProviderDefinition>());

        var resource = new IdentityProviderResource
        {
            ProviderId = "test-provider",
            Name = "Test Provider",
            IsEnabled = false,
        };

        var result = this.controller.Update(1, resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        _ = this.dynamicAuthManager.Received(1).RemoveProviderSchemeAsync("test-provider");
    }

    [Test]
    public void Delete_WhenFound_DeletesProviderAndRemovesScheme()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test-provider",
            Name = "Test Provider",
        };

        this.providerService.GetById(1).Returns(existing);

        var result = this.controller.Delete(1);

        result.Should().BeOfType<NoContentResult>();
        this.providerService.Received(1).Delete(1);
        _ = this.dynamicAuthManager.Received(1).RemoveProviderSchemeAsync("test-provider");
    }

    [Test]
    public void Delete_WhenNotFound_ReturnsNotFound()
    {
        this.providerService.GetById(99).Returns((IdentityProviderDefinition)null);

        var result = this.controller.Delete(99);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task TestConnection_WhenNull_ReturnsBadRequest()
    {
        var result = await this.controller.TestConnection(null!);

        result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public async Task TestConnection_WhenSecretIsMaskedAndExistingProviderFound_PreservesExistingSecret()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 5,
            ProviderId = "oidc-test",
            Name = "OIDC Test",
            ClientSecretEncrypted = "real-underlying-secret",
        };

        this.providerService.GetById(5).Returns(existing);
        this.providerService.TestConnectionAsync(Arg.Any<IdentityProviderDefinition>()).Returns(Task.FromResult(true));

        var resource = new IdentityProviderResource
        {
            Id = 5,
            ProviderId = "oidc-test",
            Name = "OIDC Test",
            ClientSecret = "********",
        };

        var result = await this.controller.TestConnection(resource);

        result.Should().BeOfType<OkObjectResult>();
        await this.providerService.Received(1).TestConnectionAsync(Arg.Is<IdentityProviderDefinition>(p =>
            p.ClientSecretEncrypted == "real-underlying-secret"));
    }

    [Test]
    public async Task TestConnection_WhenNewSecretWithAsterisksProvided_UsesNewSecret()
    {
        this.providerService.TestConnectionAsync(Arg.Any<IdentityProviderDefinition>()).Returns(Task.FromResult(true));

        var resource = new IdentityProviderResource
        {
            Id = 5,
            ProviderId = "oidc-test",
            Name = "OIDC Test",
            ClientSecret = "new*secret*with*stars",
        };

        var result = await this.controller.TestConnection(resource);

        result.Should().BeOfType<OkObjectResult>();
        await this.providerService.Received(1).TestConnectionAsync(Arg.Is<IdentityProviderDefinition>(p =>
            p.ClientSecretEncrypted == "new*secret*with*stars"));
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Http;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class IdentityProviderServiceTest
{
    private IIdentityProviderRepository repository = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private Logger logger = null!;
    private IdentityProviderService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<IIdentityProviderRepository>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();
        this.logger = LogManager.GetCurrentClassLogger();
        this.service = new IdentityProviderService(this.repository, this.safeHttpClientService, this.logger);
    }

    [Test]
    public async Task TestConnectionAsync_WhenOidcProvider_ValidatesUrlAndDownloadsConfiguration()
    {
        var provider = new IdentityProviderDefinition
        {
            Name = "OIDC Provider",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://auth.example.com",
        };

        var expectedUrl = "https://auth.example.com/.well-known/openid-configuration";
        this.safeHttpClientService.DownloadStringAsync(expectedUrl, Arg.Any<TimeSpan?>()).Returns("{\"issuer\": \"https://auth.example.com\"}");

        var result = await this.service.TestConnectionAsync(provider);

        result.Should().BeTrue();
        this.safeHttpClientService.Received(1).ValidateUrl(expectedUrl);
        await this.safeHttpClientService.Received(1).DownloadStringAsync(expectedUrl, timeout: Arg.Is<TimeSpan?>(t => t.HasValue && t.Value.TotalSeconds == 10));
    }

    [Test]
    public async Task TestConnectionAsync_WhenSamlProvider_ValidatesMetadataUrl()
    {
        var provider = new IdentityProviderDefinition
        {
            Name = "SAML Provider",
            ProviderType = IdentityProviderType.Saml,
            MetadataUrl = "https://auth.example.com/saml/metadata",
        };

        this.safeHttpClientService.DownloadStringAsync("https://auth.example.com/saml/metadata", Arg.Any<TimeSpan?>()).Returns("<xml>metadata</xml>");

        var result = await this.service.TestConnectionAsync(provider);

        result.Should().BeTrue();
        this.safeHttpClientService.Received(1).ValidateUrl("https://auth.example.com/saml/metadata");
    }

    [Test]
    public async Task TestConnectionAsync_WhenSsrfValidationFails_ReturnsFalse()
    {
        var provider = new IdentityProviderDefinition
        {
            Name = "Internal Target",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "http://169.254.169.254",
        };

        this.safeHttpClientService.When(x => x.ValidateUrl(Arg.Any<string>())).Throw(new InvalidOperationException("SSRF blocked"));

        var result = await this.service.TestConnectionAsync(provider);

        result.Should().BeFalse();
    }

    [Test]
    public async Task TestConnectionAsync_WhenTargetUrlIsEmpty_ReturnsTrue()
    {
        var provider = new IdentityProviderDefinition
        {
            Name = "Empty URL Provider",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = null,
        };

        var result = await this.service.TestConnectionAsync(provider);

        result.Should().BeTrue();
        this.safeHttpClientService.DidNotReceive().ValidateUrl(Arg.Any<string>());
    }

    [Test]
    public void CrudOperations_DelegateToRepository()
    {
        var provider = new IdentityProviderDefinition { Id = 1, Name = "Test" };
        this.repository.All().Returns(new List<IdentityProviderDefinition> { provider });
        this.repository.GetEnabled().Returns(new List<IdentityProviderDefinition> { provider });
        this.repository.Get(1).Returns(provider);
        this.repository.FindByProviderId("entra").Returns(provider);
        this.repository.Insert(provider).Returns(provider);

        this.service.GetAll().Should().ContainSingle();
        this.service.GetEnabled().Should().ContainSingle();
        this.service.GetById(1).Should().Be(provider);
        this.service.GetByProviderId("entra").Should().Be(provider);
        this.service.Add(provider).Should().Be(provider);
        this.service.Update(provider).Should().Be(provider);

        this.service.Delete(1);
        this.repository.Received(1).Delete(1);
    }
}

// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using Leecharr.Api.V1.Config;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Config;

[TestFixture]
public class GeneralConfigResourceMapperTest
{
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
    }

    [Test]
    public void ToResource_WhenDatabaseCorsOriginsEmpty_UsesConfigFileProvider()
    {
        this.configService.AllowedCorsOrigins.Returns(string.Empty);
        this.configFileProvider.AllowedCorsOrigins.Returns("http://localhost:3000, https://dashboard.lan");

        var resource = GeneralConfigResourceMapper.ToResource(this.configService, this.configFileProvider);

        resource.AllowedCorsOrigins.Should().Be("http://localhost:3000, https://dashboard.lan");
    }

    [Test]
    public void ToResource_WhenDatabaseCorsOriginsSet_PrefersDatabase()
    {
        this.configService.AllowedCorsOrigins.Returns("https://override.example");
        this.configFileProvider.AllowedCorsOrigins.Returns("http://localhost:3000");

        var resource = GeneralConfigResourceMapper.ToResource(this.configService, this.configFileProvider);

        resource.AllowedCorsOrigins.Should().Be("https://override.example");
    }

    [Test]
    public void ToResource_WhenDatabaseSsrfAllowlistsEmpty_UsesConfigFileProvider()
    {
        this.configService.AllowedSsrfHostnames.Returns(string.Empty);
        this.configService.AllowedSsrfSubnets.Returns("   ");
        this.configFileProvider.AllowedSsrfHostnames.Returns("prowlarr, *.local");
        this.configFileProvider.AllowedSsrfSubnets.Returns("10.0.0.0/8");

        var resource = GeneralConfigResourceMapper.ToResource(this.configService, this.configFileProvider);

        resource.AllowedSsrfHostnames.Should().Be("prowlarr, *.local");
        resource.AllowedSsrfSubnets.Should().Be("10.0.0.0/8");
    }

    [Test]
    public void ToResource_WhenAllowPrivateNetworkRequestsUnsetInDatabase_UsesConfigFileProvider()
    {
        this.configService.GetValue("AllowPrivateNetworkRequests", string.Empty).Returns(string.Empty);
        this.configFileProvider.AllowPrivateNetworkRequests.Returns(false);

        var resource = GeneralConfigResourceMapper.ToResource(this.configService, this.configFileProvider);

        resource.AllowPrivateNetworkRequests.Should().BeFalse();
    }

    [Test]
    public void ToResource_WhenAllowPrivateNetworkRequestsSetInDatabase_PrefersDatabase()
    {
        this.configService.GetValue("AllowPrivateNetworkRequests", string.Empty).Returns("true");
        this.configService.GetValueBoolean("AllowPrivateNetworkRequests", true).Returns(true);
        this.configFileProvider.AllowPrivateNetworkRequests.Returns(false);

        var resource = GeneralConfigResourceMapper.ToResource(this.configService, this.configFileProvider);

        resource.AllowPrivateNetworkRequests.Should().BeTrue();
    }

    [Test]
    public void IsMaskedApiKey_WhenSubmittedMatchesUiMask_ReturnsTrue()
    {
        GeneralConfigResourceMapper.IsMaskedApiKey("************cdef", "1234567890abcdef").Should().BeTrue();
    }

    [Test]
    public void IsMaskedApiKey_WhenSubmittedContainsAsteriskButIsNewValue_ReturnsFalse()
    {
        GeneralConfigResourceMapper.IsMaskedApiKey("my*key", "old-key").Should().BeFalse();
    }
}

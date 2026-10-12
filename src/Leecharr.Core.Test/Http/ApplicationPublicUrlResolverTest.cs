// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using Leecharr.Http.Security;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class ApplicationPublicUrlResolverTest
{
    private string priorLeecharrUrl;
    private string priorSeedarrUrl;
    private string priorLeecharrHost;

    [SetUp]
    public void SetUp()
    {
        this.priorLeecharrUrl = Environment.GetEnvironmentVariable("LEECHARR_URL");
        this.priorSeedarrUrl = Environment.GetEnvironmentVariable("SEEDARR_URL");
        this.priorLeecharrHost = Environment.GetEnvironmentVariable("LEECHARR_HOST");
        Environment.SetEnvironmentVariable("LEECHARR_URL", null);
        Environment.SetEnvironmentVariable("SEEDARR_URL", null);
        Environment.SetEnvironmentVariable("LEECHARR_HOST", null);
        Environment.SetEnvironmentVariable("SEEDARR_HOST", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("LEECHARR_URL", this.priorLeecharrUrl);
        Environment.SetEnvironmentVariable("SEEDARR_URL", this.priorSeedarrUrl);
        Environment.SetEnvironmentVariable("LEECHARR_HOST", this.priorLeecharrHost);
        Environment.SetEnvironmentVariable("SEEDARR_HOST", null);
    }

    [Test]
    public void TryResolvePublicBaseUrl_WhenLeecharrUrlSet_ReturnsConfiguredUrl()
    {
        Environment.SetEnvironmentVariable("LEECHARR_URL", "https://leecharr.example.com");
        var config = Substitute.For<IConfigFileProvider>();
        config.UrlBase.Returns("/apps/leecharr");

        ApplicationPublicUrlResolver.TryResolvePublicBaseUrl(config, out var baseUrl).Should().BeTrue();
        baseUrl.Should().Be("https://leecharr.example.com/apps/leecharr");
    }

    [Test]
    public void TryResolvePublicBaseUrl_WhenBindAddressWildcard_UsesLoopbackNotRequestHost()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.BindAddress.Returns("*");
        config.Port.Returns(7889);
        config.EnableSsl.Returns(false);

        ApplicationPublicUrlResolver.TryResolvePublicBaseUrl(config, out var baseUrl).Should().BeTrue();
        baseUrl.Should().Be("http://127.0.0.1:7889");
    }

    [Test]
    public void TryResolvePublicBaseUrl_WhenLeecharrHostSet_AppendsUrlBase()
    {
        Environment.SetEnvironmentVariable("LEECHARR_HOST", "media.home.lan");
        var config = Substitute.For<IConfigFileProvider>();
        config.Port.Returns(443);
        config.EnableSsl.Returns(true);
        config.SslPort.Returns(443);
        config.UrlBase.Returns("/leecharr");

        ApplicationPublicUrlResolver.TryResolvePublicBaseUrl(config, out var baseUrl).Should().BeTrue();
        baseUrl.Should().Be("https://media.home.lan/leecharr");
    }
}

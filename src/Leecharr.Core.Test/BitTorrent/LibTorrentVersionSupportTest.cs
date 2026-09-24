// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class LibTorrentVersionSupportTest
{
    private IConfigService configService = null!;
    private IStoragePathService storagePathService = null!;
    private ICategoryService categoryService = null!;
    private IDiskProvider diskProvider = null!;
    private IEventAggregator eventAggregator = null!;
    private HttpClient httpClient = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.storagePathService = Substitute.For<IStoragePathService>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.httpClient = new HttpClient();
    }

    [TearDown]
    public void TearDown()
    {
        this.httpClient?.Dispose();
    }

    private LibTorrentDownloadEngine CreateEngine(string configuredVersion = null)
    {
        this.configService.ActiveTorrentEngineVersion.Returns(configuredVersion);
        return new LibTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            this.httpClient);
    }

    [Test]
    public void SupportedVersions_ExposesVersion1And2()
    {
        using var engine = this.CreateEngine();

        engine.SupportedVersions.Should().NotBeNull();
        engine.SupportedVersions.Should().Contain("1.2.20");
        engine.SupportedVersions.Should().Contain("2.1.1");
    }

    [Test]
    public void ActiveVersion_WhenConfigUnset_DefaultsToVersion2()
    {
        using var engine = this.CreateEngine(null);

        engine.ActiveVersion.Should().Be("2.1.1");
        engine.DisplayName.Should().Contain("2.1.1");
        engine.Version.Should().Contain("2.1.1");
    }

    [Test]
    public void ActiveVersion_WhenConfigSetToVersion1_InitializesToVersion1()
    {
        using var engine = this.CreateEngine("1.2.20");

        engine.ActiveVersion.Should().Be("1.2.20");
        engine.DisplayName.Should().Contain("1.2.20");
        engine.Version.Should().Contain("1.2.20");
    }

    [TestCase("1.2", "1.2.20")]
    [TestCase("1.2.20", "1.2.20")]
    [TestCase("v1.2", "1.2.20")]
    [TestCase("1.2.19", "1.2.20")]
    [TestCase("2.1", "2.1.1")]
    [TestCase("2.1.1", "2.1.1")]
    [TestCase("v2.1", "2.1.1")]
    [TestCase("2.0.10", "2.1.1")]
    [TestCase("", "2.1.1")]
    [TestCase(null, "2.1.1")]
    public void NormalizeVersion_MapsVariantsToCanonicalReleases(string input, string expected)
    {
        var result = LibTorrentDownloadEngine.NormalizeVersion(input);
        result.Should().Be(expected);
    }

    [Test]
    public void GetCapabilitiesForVersion_Version1_DisablesV2AndMemoryMappedIo()
    {
        using var engine = this.CreateEngine();

        var caps = engine.GetCapabilitiesForVersion("1.2.20");

        caps.SupportsUtp.Should().BeTrue();
        caps.SupportsDht.Should().BeTrue();
        caps.SupportsPex.Should().BeTrue();
        caps.SupportsV2Torrents.Should().BeFalse();
        caps.SupportsMemoryMappedIo.Should().BeFalse();
    }

    [Test]
    public void GetCapabilitiesForVersion_Version2_EnablesV2AndMemoryMappedIo()
    {
        using var engine = this.CreateEngine();

        var caps = engine.GetCapabilitiesForVersion("2.1.1");

        caps.SupportsUtp.Should().BeTrue();
        caps.SupportsDht.Should().BeTrue();
        caps.SupportsPex.Should().BeTrue();
        caps.SupportsV2Torrents.Should().BeTrue();
        caps.SupportsMemoryMappedIo.Should().BeTrue();
    }

    [Test]
    public void Capabilities_ReflectsActiveVersionDynamically()
    {
        using var engine = this.CreateEngine("1.2.20");
        engine.Capabilities.SupportsV2Torrents.Should().BeFalse();
        engine.Capabilities.SupportsMemoryMappedIo.Should().BeFalse();

        using var engineV2 = this.CreateEngine("2.1.1");
        engineV2.Capabilities.SupportsV2Torrents.Should().BeTrue();
        engineV2.Capabilities.SupportsMemoryMappedIo.Should().BeTrue();
    }

    [Test]
    public async Task SwitchVersionAsync_ValidVersion_SwitchesActiveVersionAndCapabilities()
    {
        using var engine = this.CreateEngine("2.1.1");
        engine.ActiveVersion.Should().Be("2.1.1");

        var switched = await engine.SwitchVersionAsync("1.2.20");

        switched.Should().BeTrue();
        engine.ActiveVersion.Should().Be("1.2.20");
        engine.Capabilities.SupportsV2Torrents.Should().BeFalse();
        engine.Capabilities.SupportsMemoryMappedIo.Should().BeFalse();
    }

    [Test]
    public async Task SwitchVersionAsync_SameVersion_ReturnsTrueWithoutRestart()
    {
        using var engine = this.CreateEngine("2.1.1");

        var switched = await engine.SwitchVersionAsync("2.1.1");

        switched.Should().BeTrue();
        engine.ActiveVersion.Should().Be("2.1.1");
    }

    [Test]
    public async Task SwitchVersionAsync_InvalidOrUnsupportedVersion_ReturnsFalse()
    {
        using var engine = this.CreateEngine("2.1.1");

        var switchedEmpty = await engine.SwitchVersionAsync(string.Empty);
        switchedEmpty.Should().BeFalse();

        var switchedNull = await engine.SwitchVersionAsync(null!);
        switchedNull.Should().BeFalse();
    }
}

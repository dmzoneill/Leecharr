// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
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

    [Test]
    public void LibTorrentDownloadTask_StoresAndReportsLiveTelemetry()
    {
        var task = new LibTorrentDownloadTask(42, "aabbccddeeff00112233445566778899aabbccdd", "Test Torrent", 1000000000L, "movies")
        {
            Progress = 0.75,
            DownloadedBytes = 750000000L,
            UploadedBytes = 50000000L,
            DownloadSpeed = 15000000L,
            UploadSpeed = 500000L,
            ConnectedSeeders = 40,
            ConnectedLeechers = 10,
            Status = TorrentStatus.Downloading,
        };

        task.TorrentId.Should().Be(42);
        task.InfoHash.Should().Be("aabbccddeeff00112233445566778899aabbccdd");
        task.Name.Should().Be("Test Torrent");
        task.TotalSize.Should().Be(1000000000L);
        task.TotalBytes.Should().Be(1000000000L);
        task.Progress.Should().Be(0.75);
        task.DownloadedBytes.Should().Be(750000000L);
        task.UploadedBytes.Should().Be(50000000L);
        task.DownloadSpeed.Should().Be(15000000L);
        task.UploadSpeed.Should().Be(500000L);
        task.ConnectedSeeders.Should().Be(40);
        task.ConnectedLeechers.Should().Be(10);
        task.Status.Should().Be(TorrentStatus.Downloading);

        var metrics = task.GetResourceMetrics();
        metrics.Should().NotBeNull();
        metrics.TorrentId.Should().Be(42);
        metrics.Progress.Should().Be(0.75);
        metrics.ConnectedPeers.Should().Be(50);
    }

    [Test]
    public void LibTorrentDownloadTask_WhenPaused_ReportsZeroSpeedsAndPeers()
    {
        var task = new LibTorrentDownloadTask(43, "aabbccddeeff00112233445566778899aabbccde", "Paused Torrent", 500000000L)
        {
            DownloadSpeed = 10000000L,
            UploadSpeed = 200000L,
            ConnectedSeeders = 20,
            ConnectedLeechers = 5,
            Status = TorrentStatus.Paused,
        };

        task.DownloadSpeed.Should().Be(0L);
        task.UploadSpeed.Should().Be(0L);
        task.ConnectedSeeders.Should().Be(0);
        task.ConnectedLeechers.Should().Be(0);
    }

    [Test]
    public void LibTorrentDownloadTask_SetPeers_UpdatesPeerList()
    {
        var task = new LibTorrentDownloadTask(44, "aabbccddeeff00112233445566778899aabbccdf", "Peer List Torrent", 200000000L);
        task.GetPeers().Should().BeEmpty();

        var peers = new List<PeerInfo>
        {
            new() { Ip = "1.2.3.4", Port = 51413, Client = "Transmission 4.0", Progress = 1.0 },
            new() { Ip = "5.6.7.8", Port = 6881, Client = "qBittorrent 5.0", Progress = 0.5 },
        };

        task.SetPeers(peers);
        task.GetPeers().Should().HaveCount(2);
        task.GetPeers()[0].Client.Should().Be("Transmission 4.0");
        task.GetPeers()[1].Client.Should().Be("qBittorrent 5.0");
    }
}

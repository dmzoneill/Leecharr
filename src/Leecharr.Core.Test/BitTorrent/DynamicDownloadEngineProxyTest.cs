// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class DynamicDownloadEngineProxyTest
{
    private ITorrentEngine monoTorrentEngine = null!;
    private ITorrentEngine libTorrentEngine = null!;
    private ITorrentEngine transmissionEngine = null!;
    private IConfigService configService = null!;
    private ITorrentRepository torrentRepository = null!;
    private IEventAggregator eventAggregator = null!;
    private DynamicDownloadEngineProxy proxy = null!;

    [SetUp]
    public void SetUp()
    {
        this.monoTorrentEngine = Substitute.For<ITorrentEngine>();
        this.monoTorrentEngine.EngineId.Returns("MonoTorrent");
        this.monoTorrentEngine.DisplayName.Returns("MonoTorrent (Pure .NET)");
        this.monoTorrentEngine.ProtocolName.Returns("BitTorrent");
        this.monoTorrentEngine.IsAvailable.Returns(true);
        this.monoTorrentEngine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        this.libTorrentEngine = Substitute.For<ITorrentEngine>();
        this.libTorrentEngine.EngineId.Returns("LibTorrent");
        this.libTorrentEngine.DisplayName.Returns("libtorrent (Rasterbar C++)");
        this.libTorrentEngine.ProtocolName.Returns("BitTorrent");
        this.libTorrentEngine.IsAvailable.Returns(true);
        this.libTorrentEngine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        this.transmissionEngine = Substitute.For<ITorrentEngine>();
        this.transmissionEngine.EngineId.Returns("Transmission");
        this.transmissionEngine.DisplayName.Returns("Transmission Daemon (Sidecar)");
        this.transmissionEngine.ProtocolName.Returns("BitTorrent");
        this.transmissionEngine.IsAvailable.Returns(true);
        this.transmissionEngine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        this.configService = Substitute.For<IConfigService>();
        this.configService.ActiveTorrentEngine.Returns("MonoTorrent");

        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.torrentRepository.All().Returns(new List<Torrent>
        {
            new() { Id = 1, Name = "Ubuntu ISO", InfoHash = "0123456789ABCDEF0123456789ABCDEF01234567", Status = TorrentStatus.Downloading },
            new() { Id = 2, Name = "Debian ISO", InfoHash = "FEDCBA9876543210FEDCBA9876543210FEDCBA98", Status = TorrentStatus.Paused },
        });

        this.eventAggregator = Substitute.For<IEventAggregator>();

        var engines = new List<ITorrentEngine> { this.monoTorrentEngine, this.libTorrentEngine, this.transmissionEngine };

        this.proxy = new DynamicDownloadEngineProxy(
            engines,
            this.configService,
            this.torrentRepository,
            this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredEngine()
    {
        this.proxy.ActiveEngineId.Should().Be("MonoTorrent");
        this.proxy.ActiveEngine.Should().BeSameAs(this.monoTorrentEngine);
        this.proxy.ProtocolName.Should().Be("BitTorrent");
    }

    [Test]
    public void Constructor_WhenConfiguredEngineUnavailable_FallsBackToAvailableEngine()
    {
        var configSvc = Substitute.For<IConfigService>();
        configSvc.ActiveTorrentEngine.Returns("Transmission");

        this.transmissionEngine.IsAvailable.Returns(false);

        using var testProxy = new DynamicDownloadEngineProxy(
            new List<ITorrentEngine> { this.transmissionEngine, this.monoTorrentEngine },
            configSvc,
            this.torrentRepository,
            this.eventAggregator);

        testProxy.ActiveEngineId.Should().Be("MonoTorrent");
        testProxy.ActiveEngine.Should().BeSameAs(this.monoTorrentEngine);
    }

    [Test]
    public void GetEngines_ReturnsAllRegisteredEngines()
    {
        var engines = this.proxy.GetEngines().ToList();
        engines.Should().HaveCount(3);
        engines.Select(e => e.EngineId).Should().Contain(new[] { "MonoTorrent", "LibTorrent", "Transmission" });
    }

    [Test]
    public void GetEngine_WithValidId_ReturnsMatchingEngine()
    {
        var engine = this.proxy.GetEngine("libtorrent");
        engine.Should().NotBeNull();
        engine!.EngineId.Should().Be("LibTorrent");
    }

    [Test]
    public void GetEngine_WithInvalidId_ReturnsNull()
    {
        var engine = this.proxy.GetEngine("NonExistentEngine");
        engine.Should().BeNull();
    }

    [Test]
    public async Task ProbeEngineAsync_WithValidEngine_ReturnsHealthResult()
    {
        var probe = await this.proxy.ProbeEngineAsync("LibTorrent");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeEngineAsync_WithInvalidEngine_ReturnsUnhealthy()
    {
        var probe = await this.proxy.ProbeEngineAsync("InvalidEngine");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchEngineAsync_SwitchesActiveEngineAndMigratesTorrents()
    {
        var result = await this.proxy.SwitchEngineAsync("LibTorrent", preserveTransfers: true);

        result.Success.Should().BeTrue();
        result.PreviousEngine.Should().Be("MonoTorrent");
        result.ActiveEngine.Should().Be("LibTorrent");
        result.TorrentsMigrated.Should().Be(2);

        this.proxy.ActiveEngineId.Should().Be("LibTorrent");
        this.proxy.ActiveEngine.Should().BeSameAs(this.libTorrentEngine);

        await this.monoTorrentEngine.Received(1).StopAsync();
        await this.libTorrentEngine.Received(1).StartAsync();
        await this.libTorrentEngine.Received(2).AddTorrentAsync(Arg.Any<Torrent>(), null, Arg.Any<string>());
        await this.libTorrentEngine.Received(1).PauseTorrentAsync(2);

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveTorrentEngine"] == "LibTorrent"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentEngineSwitchedEvent>(e => e.PreviousEngine == "MonoTorrent" && e.NewEngine == "LibTorrent" && e.TorrentsMigrated == 2));
    }

    [Test]
    public async Task SwitchEngineAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await this.proxy.SwitchEngineAsync("MonoTorrent");

        result.Success.Should().BeTrue();
        result.ActiveEngine.Should().Be("MonoTorrent");
        result.TorrentsMigrated.Should().Be(0);

        await this.monoTorrentEngine.DidNotReceive().StopAsync();
    }

    [Test]
    public async Task SwitchEngineAsync_WithUnknownEngine_ReturnsFailure()
    {
        var result = await this.proxy.SwitchEngineAsync("UnknownEngine");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not registered");
        this.proxy.ActiveEngineId.Should().Be("MonoTorrent");
    }

    [Test]
    public async Task SwitchEngineAsync_WhenTargetUnavailable_AbortsSwitch()
    {
        this.libTorrentEngine.IsAvailable.Returns(false);

        var result = await this.proxy.SwitchEngineAsync("LibTorrent");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not available");
        this.proxy.ActiveEngineId.Should().Be("MonoTorrent");
        await this.monoTorrentEngine.DidNotReceive().StopAsync();
    }

    [Test]
    public async Task SwitchEngineAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        this.libTorrentEngine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "Missing native shared library",
        }));

        var result = await this.proxy.SwitchEngineAsync("LibTorrent");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        this.proxy.ActiveEngineId.Should().Be("MonoTorrent");
        await this.monoTorrentEngine.DidNotReceive().StopAsync();
    }

    [Test]
    public async Task Delegation_ForwardsCallsToActiveEngine()
    {
        await this.proxy.StartAsync();
        await this.monoTorrentEngine.Received(1).StartAsync();

        await this.proxy.StopAsync();
        await this.monoTorrentEngine.Received(1).StopAsync();

        var torrent = new Torrent { Id = 10, InfoHash = "HASH" };
        await this.proxy.AddTorrentAsync(torrent, null, "magnet:?");
        await this.monoTorrentEngine.Received(1).AddTorrentAsync(torrent, null, "magnet:?");

        await this.proxy.PauseTorrentAsync(10);
        await this.monoTorrentEngine.Received(1).PauseTorrentAsync(10);

        await this.proxy.ResumeTorrentAsync(10);
        await this.monoTorrentEngine.Received(1).ResumeTorrentAsync(10);

        await this.proxy.ForceRecheckAsync(10);
        await this.monoTorrentEngine.Received(1).ForceRecheckAsync(10);

        await this.proxy.ForceAnnounceAsync(10);
        await this.monoTorrentEngine.Received(1).ForceAnnounceAsync(10);

        await this.proxy.RemoveTorrentAsync(10, true);
        await this.monoTorrentEngine.Received(1).RemoveTorrentAsync(10, true);

        await this.proxy.RenameFileAsync(10, "old.mkv", "new.mkv");
        await this.monoTorrentEngine.Received(1).RenameFileAsync(10, "old.mkv", "new.mkv");

        await this.proxy.RenameFolderAsync(10, "old_folder", "new_folder");
        await this.monoTorrentEngine.Received(1).RenameFolderAsync(10, "old_folder", "new_folder");

        await this.proxy.SetTorrentPrivateStatusAsync(10, true);
        await this.monoTorrentEngine.Received(1).SetTorrentPrivateStatusAsync(10, true);

        await this.proxy.SetSuperSeedingAsync(10, true);
        await this.monoTorrentEngine.Received(1).SetSuperSeedingAsync(10, true);

        await this.proxy.MoveTorrentFilesAsync(10, "/new/save/path", false);
        await this.monoTorrentEngine.Received(1).MoveTorrentFilesAsync(10, "/new/save/path", false);

        await this.proxy.ProbeHealthAsync();
        await this.monoTorrentEngine.Received(1).ProbeHealthAsync();
    }

    [Test]
    public async Task SwitchEngineAsync_WhenTargetStartFails_RollsBackAndRestartsPreviousEngine()
    {
        this.libTorrentEngine.StartAsync().ThrowsAsync(new InvalidOperationException("Failed to bind port"));

        var result = await this.proxy.SwitchEngineAsync("LibTorrent");

        result.Success.Should().BeFalse();
        result.PreviousEngine.Should().Be("MonoTorrent");
        result.ActiveEngine.Should().Be("MonoTorrent");
        result.Error.Should().Contain("Hot-swap failed");
        this.proxy.ActiveEngineId.Should().Be("MonoTorrent");

        await this.monoTorrentEngine.Received(1).StopAsync();
        await this.libTorrentEngine.Received(1).StartAsync();
        await this.libTorrentEngine.Received(1).StopAsync();
        await this.monoTorrentEngine.Received(1).StartAsync();
    }

    [Test]
    public async Task SwitchEngineAsync_WhenPreservingTransfers_LoadsCachedTorrentBytesFromDisk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr-test-" + Guid.NewGuid().ToString("N"));
        var torrentsDir = Path.Combine(tempDir, "Torrents");
        Directory.CreateDirectory(torrentsDir);

        try
        {
            var expectedBytes = new byte[] { 0x64, 0x31, 0x30, 0x65 };
            var hash = "0123456789abcdef0123456789abcdef01234567";
            var filePath = Path.Combine(torrentsDir, $"{hash}.torrent");
            await File.WriteAllBytesAsync(filePath, expectedBytes);

            var appFolderInfo = Substitute.For<IAppFolderInfo>();
            appFolderInfo.AppDataFolder.Returns(tempDir);

            using var testProxy = new DynamicDownloadEngineProxy(
                new List<ITorrentEngine> { this.monoTorrentEngine, this.libTorrentEngine },
                this.configService,
                this.torrentRepository,
                this.eventAggregator,
                appFolderInfo: appFolderInfo);

            var result = await testProxy.SwitchEngineAsync("LibTorrent", preserveTransfers: true);

            result.Success.Should().BeTrue();
            result.TorrentsMigrated.Should().Be(2);

            await this.libTorrentEngine.Received(1).AddTorrentAsync(
                Arg.Is<Torrent>(t => t.Id == 1),
                Arg.Is<byte[]>(b => b != null && b.SequenceEqual(expectedBytes)),
                Arg.Any<string>());

            await this.libTorrentEngine.Received(1).AddTorrentAsync(
                Arg.Is<Torrent>(t => t.Id == 2),
                null,
                Arg.Any<string>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task SwitchEngineAsync_WhenPreservingTransfers_ReappliesLimitsSuperSeedingPrivateStatusAndTrackers()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Special ISO",
            InfoHash = "1111222233334444555566667777888899990000",
            Status = TorrentStatus.Downloading,
            DownloadLimit = 5000,
            UploadLimit = 2000,
            InitialSeeding = true,
            IsPrivate = true,
            TrackerUrl = "http://tracker1.com/announce",
        };

        this.torrentRepository.All().Returns(new List<Torrent> { torrent });

        var trackerRepo = Substitute.For<NzbDrone.Core.Trackers.ITrackerEntryRepository>();
        trackerRepo.GetByTorrentId(42).Returns(new List<NzbDrone.Core.Trackers.TrackerEntry>
        {
            new() { TorrentId = 42, Url = "http://tracker1.com/announce" },
            new() { TorrentId = 42, Url = "http://tracker2.com/announce" },
        });

        using var testProxy = new DynamicDownloadEngineProxy(
            new List<ITorrentEngine> { this.monoTorrentEngine, this.libTorrentEngine },
            this.configService,
            this.torrentRepository,
            this.eventAggregator,
            trackerEntryRepository: trackerRepo);

        var result = await testProxy.SwitchEngineAsync("LibTorrent", preserveTransfers: true);

        result.Success.Should().BeTrue();
        await this.libTorrentEngine.Received(1).SetTorrentRateLimitsAsync(42, 5000, 2000);
        await this.libTorrentEngine.Received(1).SetSuperSeedingAsync(42, true);
        await this.libTorrentEngine.Received(1).SetTorrentPrivateStatusAsync(42, true);
        await this.libTorrentEngine.Received(1).AddTrackersAsync(42, Arg.Is<List<string>>(list => list.Contains("http://tracker2.com/announce")));
    }
}

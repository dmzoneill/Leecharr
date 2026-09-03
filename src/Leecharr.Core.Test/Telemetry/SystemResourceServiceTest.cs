// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Telemetry;

namespace Leecharr.Core.Test.Telemetry;

[TestFixture]
public class SystemResourceServiceTest
{
    private ITorrentEngineManager torrentEngineManager = null!;
    private ITorrentEngine activeEngine = null!;
    private IArchiveExtractorManager extractorManager = null!;
    private IMediaInspectorManager mediaInspectorManager = null!;
    private IGeoIpManager geoIpManager = null!;
    private IBlocklistManager blocklistManager = null!;
    private INetworkBindingManager networkBindingManager = null!;
    private IMediaMetadataManager mediaMetadataManager = null!;
    private IHttpTransportManager httpTransportManager = null!;
    private IAiManager aiManager = null!;
    private IConfigService configService = null!;
    private SystemResourceService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.activeEngine = Substitute.For<ITorrentEngine>();
        this.activeEngine.EngineId.Returns("MonoTorrent");
        this.activeEngine.DisplayName.Returns("MonoTorrent (Pure .NET)");
        this.activeEngine.Version.Returns("3.0.2");
        this.activeEngine.GetEngineMetrics().Returns(new TorrentEngineMetrics
        {
            EngineId = "MonoTorrent",
            DisplayName = "MonoTorrent (Pure .NET)",
            Version = "3.0.2",
            IsRunning = true,
            ActiveTorrents = 2,
            TotalDownloadSpeed = 1024000,
            TotalUploadSpeed = 512000,
            DhtNodeCount = 120,
        });

        this.activeEngine.GetAllTorrentResourceMetrics().Returns(new List<TorrentResourceMetrics>
        {
            new()
            {
                TorrentId = 1,
                InfoHash = "0123456789abcdef0123456789abcdef01234567",
                Name = "Big Buck Bunny",
                Status = "Downloading",
                Progress = 0.5,
                PayloadDownloadSpeed = 1024000,
                ConnectedPeers = 15,
            },
        });

        this.activeEngine.GetTorrentResourceMetrics(1).Returns(new TorrentResourceMetrics
        {
            TorrentId = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Name = "Big Buck Bunny",
            Status = "Downloading",
            Progress = 0.5,
            PayloadDownloadSpeed = 1024000,
            ConnectedPeers = 15,
        });

        this.torrentEngineManager = Substitute.For<ITorrentEngineManager>();
        this.torrentEngineManager.ActiveEngine.Returns(this.activeEngine);
        this.torrentEngineManager.ActiveEngineId.Returns("MonoTorrent");

        this.extractorManager = Substitute.For<IArchiveExtractorManager>();
        this.extractorManager.ActiveProviderId.Returns("SharpCompress");

        this.mediaInspectorManager = Substitute.For<IMediaInspectorManager>();
        this.mediaInspectorManager.ActiveProviderId.Returns("EbmlPure");

        this.geoIpManager = Substitute.For<IGeoIpManager>();
        this.geoIpManager.ActiveProviderId.Returns("MaxMind");

        this.blocklistManager = Substitute.For<IBlocklistManager>();
        this.blocklistManager.ActiveProviderId.Returns("BuiltIn");

        this.networkBindingManager = Substitute.For<INetworkBindingManager>();
        this.networkBindingManager.ActiveProviderId.Returns("Loopback");

        this.mediaMetadataManager = Substitute.For<IMediaMetadataManager>();
        this.mediaMetadataManager.ActiveProviderId.Returns("ServarrEcosystem");

        this.httpTransportManager = Substitute.For<IHttpTransportManager>();
        this.httpTransportManager.ActiveProviderId.Returns("SocketsHttp");

        this.aiManager = Substitute.For<IAiManager>();
        this.aiManager.ActiveProviderId.Returns("LocalHeuristic");

        this.configService = Substitute.For<IConfigService>();
        this.configService.NetworkInterfaceBinding.Returns("tun0");

        this.service = new SystemResourceService(
            this.torrentEngineManager,
            this.extractorManager,
            this.mediaInspectorManager,
            this.geoIpManager,
            this.blocklistManager,
            this.networkBindingManager,
            this.mediaMetadataManager,
            this.httpTransportManager,
            this.aiManager,
            this.configService);
    }

    [Test]
    public void GetHostMetrics_ReturnsPopulatedHostMetrics()
    {
        var host = this.service.GetHostMetrics();

        host.Should().NotBeNull();
        host.CpuCores.Should().BeGreaterThan(0);
        host.WorkingSetBytes.Should().BeGreaterThan(0);
        host.ManagedHeapBytes.Should().BeGreaterThan(0);
        host.ThreadCount.Should().BeGreaterThan(0);
        host.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public void GetTorrentEngineMetrics_ReturnsMetricsFromActiveEngine()
    {
        var metrics = this.service.GetTorrentEngineMetrics();

        metrics.Should().NotBeNull();
        metrics.EngineId.Should().Be("MonoTorrent");
        metrics.IsRunning.Should().BeTrue();
        metrics.TotalDownloadSpeed.Should().Be(1024000);
        metrics.DhtNodeCount.Should().Be(120);
    }

    [Test]
    public void GetPerTorrentMetrics_ReturnsListOfTorrentMetrics()
    {
        var list = this.service.GetPerTorrentMetrics();

        list.Should().NotBeNull();
        list.Should().HaveCount(1);
        list[0].TorrentId.Should().Be(1);
        list[0].Name.Should().Be("Big Buck Bunny");
        list[0].ConnectedPeers.Should().Be(15);
    }

    [Test]
    public void GetTorrentMetrics_WhenTorrentExists_ReturnsTorrentMetrics()
    {
        var metrics = this.service.GetTorrentMetrics(1);

        metrics.Should().NotBeNull();
        metrics!.TorrentId.Should().Be(1);
        metrics.Name.Should().Be("Big Buck Bunny");
    }

    [Test]
    public void GetTorrentMetrics_WhenTorrentNotFound_ReturnsNull()
    {
        this.activeEngine.GetTorrentResourceMetrics(999).Returns((TorrentResourceMetrics)null!);

        var metrics = this.service.GetTorrentMetrics(999);

        metrics.Should().BeNull();
    }

    [Test]
    public void GetSubsystemTelemetry_ReturnsAllNineSubsystems()
    {
        var reports = this.service.GetSubsystemTelemetry();

        reports.Should().NotBeNull();
        reports.Should().HaveCount(9);

        reports.Should().Contain(r => r.SubsystemId == "bittorrent" && r.Status == "Healthy");
        reports.Should().Contain(r => r.SubsystemId == "extractor");
        reports.Should().Contain(r => r.SubsystemId == "mediainspector");
        reports.Should().Contain(r => r.SubsystemId == "geoip");
        reports.Should().Contain(r => r.SubsystemId == "blocklist");
        reports.Should().Contain(r => r.SubsystemId == "networkbinding");
        reports.Should().Contain(r => r.SubsystemId == "mediametadata");
        reports.Should().Contain(r => r.SubsystemId == "httptransport");
        reports.Should().Contain(r => r.SubsystemId == "ai");
    }

    [Test]
    public void GetFullTelemetrySnapshot_ReturnsUnifiedSnapshot()
    {
        var snapshot = this.service.GetFullTelemetrySnapshot();

        snapshot.Should().NotBeNull();
        snapshot.Host.Should().NotBeNull();
        snapshot.TorrentEngine.Should().NotBeNull();
        snapshot.PerTorrent.Should().NotBeNull();
        snapshot.Subsystems.Should().HaveCount(9);
        snapshot.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}

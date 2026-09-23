// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.System;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Telemetry;

namespace Leecharr.Core.Test.Telemetry;

[TestFixture]
public class SystemResourcesControllerTest
{
    private ISystemResourceService resourceService = null!;
    private SystemResourcesController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.resourceService = Substitute.For<ISystemResourceService>();
        this.controller = new SystemResourcesController(this.resourceService);
    }

    [Test]
    public async Task GetFullSnapshot_CallsResourceService_ReturnsOkWithSnapshot()
    {
        var expectedSnapshot = new SystemResourceTelemetrySnapshot
        {
            Host = new HostProcessResourceMetrics
            {
                CpuProcessPercent = 14.5,
                CpuCores = 8,
                WorkingSetBytes = 150_000_000L,
                ThreadCount = 24,
            },
            TorrentEngine = new TorrentEngineMetrics
            {
                EngineId = "MonoTorrent",
                DisplayName = "MonoTorrent (Pure .NET)",
                Version = "3.0.2",
                IsRunning = true,
                ActiveTorrents = 5,
                TotalDownloadSpeed = 5_000_000L,
                TotalUploadSpeed = 1_000_000L,
            },
            PerTorrent = new List<TorrentResourceMetrics>
            {
                new TorrentResourceMetrics
                {
                    TorrentId = 1,
                    Name = "Ubuntu Linux 24.04 ISO",
                    Status = "Downloading",
                    Progress = 0.65,
                },
            },
            Subsystems = new List<SubsystemTelemetryReport>
            {
                new SubsystemTelemetryReport
                {
                    SubsystemId = "vpn",
                    SubsystemName = "VPN Killswitch",
                    Status = "Healthy",
                },
            },
            Timestamp = DateTime.UtcNow,
        };

        this.resourceService.GetFullTelemetrySnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedSnapshot));

        var result = await this.controller.GetFullSnapshot();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var snapshot = okResult.Value as SystemResourceTelemetrySnapshot;

        snapshot.Should().NotBeNull();
        snapshot!.Host.CpuProcessPercent.Should().Be(14.5);
        snapshot.Host.CpuCores.Should().Be(8);
        snapshot.TorrentEngine.EngineId.Should().Be("MonoTorrent");
        snapshot.TorrentEngine.ActiveTorrents.Should().Be(5);
        snapshot.PerTorrent.Should().HaveCount(1);
        snapshot.PerTorrent[0].Name.Should().Be("Ubuntu Linux 24.04 ISO");
        snapshot.Subsystems.Should().HaveCount(1);
        snapshot.Subsystems[0].SubsystemId.Should().Be("vpn");
    }

    [Test]
    public async Task GetFullSnapshot_PassesCancellationTokenToService()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        this.resourceService.GetFullTelemetrySnapshotAsync(token)
            .Returns(Task.FromResult(new SystemResourceTelemetrySnapshot()));

        var result = await this.controller.GetFullSnapshot(token);

        result.Result.Should().BeOfType<OkObjectResult>();
        await this.resourceService.Received(1).GetFullTelemetrySnapshotAsync(token);
    }

    [Test]
    public void GetHostMetrics_CallsResourceService_ReturnsOkWithMetrics()
    {
        var expectedMetrics = new HostProcessResourceMetrics
        {
            CpuProcessPercent = 8.2,
            CpuCores = 16,
            WorkingSetBytes = 120_000_000L,
            PrivateMemoryBytes = 110_000_000L,
            ThreadCount = 18,
            HandleCount = 450,
            UptimeSeconds = 3600,
        };

        this.resourceService.GetHostMetrics().Returns(expectedMetrics);

        var result = this.controller.GetHostMetrics();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var metrics = okResult.Value as HostProcessResourceMetrics;

        metrics.Should().NotBeNull();
        metrics!.CpuProcessPercent.Should().Be(8.2);
        metrics.CpuCores.Should().Be(16);
        metrics.WorkingSetBytes.Should().Be(120_000_000L);
        metrics.ThreadCount.Should().Be(18);
        metrics.UptimeSeconds.Should().Be(3600);
    }

    [Test]
    public void GetEngineMetrics_CallsResourceService_ReturnsOkWithEngineMetrics()
    {
        var expectedMetrics = new TorrentEngineMetrics
        {
            EngineId = "LibTorrent",
            DisplayName = "libtorrent (Rasterbar)",
            Version = "2.0.9",
            IsRunning = true,
            ActiveTorrents = 12,
            TotalDownloadSpeed = 15_000_000L,
            TotalUploadSpeed = 3_000_000L,
            DhtNodeCount = 280,
        };

        this.resourceService.GetTorrentEngineMetrics().Returns(expectedMetrics);

        var result = this.controller.GetEngineMetrics();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var metrics = okResult.Value as TorrentEngineMetrics;

        metrics.Should().NotBeNull();
        metrics!.EngineId.Should().Be("LibTorrent");
        metrics.DisplayName.Should().Be("libtorrent (Rasterbar)");
        metrics.Version.Should().Be("2.0.9");
        metrics.IsRunning.Should().BeTrue();
        metrics.ActiveTorrents.Should().Be(12);
        metrics.TotalDownloadSpeed.Should().Be(15_000_000L);
        metrics.TotalUploadSpeed.Should().Be(3_000_000L);
        metrics.DhtNodeCount.Should().Be(280);
    }

    [Test]
    public async Task GetSubsystemsTelemetry_CallsResourceService_ReturnsOkWithList()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        var expectedList = new List<SubsystemTelemetryReport>
        {
            new SubsystemTelemetryReport
            {
                SubsystemId = "blocklist",
                SubsystemName = "IP Blocklist",
                Status = "Healthy",
                ResourceLoad = "Low",
            },
            new SubsystemTelemetryReport
            {
                SubsystemId = "media_enrichment",
                SubsystemName = "Media Metadata Enrichment",
                Status = "Healthy",
                ResourceLoad = "Nominal",
            },
        };

        this.resourceService.GetSubsystemTelemetryAsync(null, token)
            .Returns(Task.FromResult(expectedList));

        var result = await this.controller.GetSubsystemsTelemetry(token);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var reports = okResult.Value as List<SubsystemTelemetryReport>;

        reports.Should().NotBeNull();
        reports!.Count.Should().Be(2);
        reports[0].SubsystemId.Should().Be("blocklist");
        reports[1].SubsystemId.Should().Be("media_enrichment");
    }

    [Test]
    public void GetPerTorrentMetrics_CallsResourceService_ReturnsOkWithList()
    {
        var expectedList = new List<TorrentResourceMetrics>
        {
            new TorrentResourceMetrics
            {
                TorrentId = 10,
                Name = "Debian 12 NetInst",
                Status = "Downloading",
                Progress = 0.45,
                PayloadDownloadSpeed = 2_000_000L,
                PayloadUploadSpeed = 500_000L,
            },
            new TorrentResourceMetrics
            {
                TorrentId = 11,
                Name = "Fedora Workstation 40",
                Status = "Seeding",
                Progress = 1.0,
                PayloadDownloadSpeed = 0L,
                PayloadUploadSpeed = 1_500_000L,
            },
        };

        this.resourceService.GetPerTorrentMetrics().Returns(expectedList);

        var result = this.controller.GetPerTorrentMetrics();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var metrics = okResult.Value as IReadOnlyList<TorrentResourceMetrics>;

        metrics.Should().NotBeNull();
        metrics!.Count.Should().Be(2);
        metrics[0].TorrentId.Should().Be(10);
        metrics[1].TorrentId.Should().Be(11);
    }

    [Test]
    public void GetTorrentMetrics_WhenTorrentExists_ReturnsOkWithMetrics()
    {
        var expectedMetric = new TorrentResourceMetrics
        {
            TorrentId = 42,
            Name = "Arch Linux ISO",
            Status = "Downloading",
            Progress = 0.72,
            ConnectedPeers = 35,
        };

        this.resourceService.GetTorrentMetrics(42).Returns(expectedMetric);

        var result = this.controller.GetTorrentMetrics(42);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var metric = okResult.Value as TorrentResourceMetrics;

        metric.Should().NotBeNull();
        metric!.TorrentId.Should().Be(42);
        metric.Name.Should().Be("Arch Linux ISO");
        metric.ConnectedPeers.Should().Be(35);
    }

    [Test]
    public void GetTorrentMetrics_WhenTorrentNotFound_ReturnsNotFound()
    {
        this.resourceService.GetTorrentMetrics(999).Returns((TorrentResourceMetrics)null!);

        var result = this.controller.GetTorrentMetrics(999);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        var notFoundResult = (NotFoundObjectResult)result.Result!;
        notFoundResult.StatusCode.Should().Be(404);

        var errorProp = notFoundResult.Value!.GetType().GetProperty("error")?.GetValue(notFoundResult.Value);
        errorProp.Should().Be("Torrent id 999 was not found in active engine session.");
    }

    [Test]
    public void SystemResourcesController_Attributes_AreDecoratedCorrectly()
    {
        var type = typeof(SystemResourcesController);
        var apiAttribute = type.GetCustomAttribute<V1ApiControllerAttribute>();

        apiAttribute.Should().NotBeNull();
        apiAttribute!.Template.Should().Be("api/v1/system/resources");

        var snapshotMethod = type.GetMethod(nameof(SystemResourcesController.GetFullSnapshot));
        snapshotMethod.Should().NotBeNull();
        snapshotMethod!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();

        var hostMethod = type.GetMethod(nameof(SystemResourcesController.GetHostMetrics));
        hostMethod.Should().NotBeNull();
        hostMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("host");

        var engineMethod = type.GetMethod(nameof(SystemResourcesController.GetEngineMetrics));
        engineMethod.Should().NotBeNull();
        engineMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("engine");

        var subsystemsMethod = type.GetMethod(nameof(SystemResourcesController.GetSubsystemsTelemetry));
        subsystemsMethod.Should().NotBeNull();
        subsystemsMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("subsystems");

        var perTorrentMethod = type.GetMethod(nameof(SystemResourcesController.GetPerTorrentMetrics));
        perTorrentMethod.Should().NotBeNull();
        perTorrentMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("torrents");

        var torrentByIdMethod = type.GetMethod(nameof(SystemResourcesController.GetTorrentMetrics));
        torrentByIdMethod.Should().NotBeNull();
        torrentByIdMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("torrents/{id:int}");
    }
}

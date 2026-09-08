// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Leecharr.Api.V1.Subsystems;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Telemetry;

namespace Leecharr.Core.Test.Subsystems;

[TestFixture]
public class SubsystemsControllerTest
{
    private ITorrentEngineManager torrentEngineManager = null!;
    private IArchiveExtractorManager extractorManager = null!;
    private IMediaInspectorManager mediaInspectorManager = null!;
    private IGeoIpManager geoIpManager = null!;
    private IBlocklistManager blocklistManager = null!;
    private INetworkBindingManager networkBindingManager = null!;
    private IMediaMetadataManager mediaMetadataManager = null!;
    private IHttpTransportManager httpTransportManager = null!;
    private IAiManager aiManager = null!;
    private ISystemResourceService resourceService = null!;
    private SubsystemsController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentEngineManager = Substitute.For<ITorrentEngineManager>();
        this.extractorManager = Substitute.For<IArchiveExtractorManager>();
        this.mediaInspectorManager = Substitute.For<IMediaInspectorManager>();
        this.geoIpManager = Substitute.For<IGeoIpManager>();
        this.blocklistManager = Substitute.For<IBlocklistManager>();
        this.networkBindingManager = Substitute.For<INetworkBindingManager>();
        this.mediaMetadataManager = Substitute.For<IMediaMetadataManager>();
        this.httpTransportManager = Substitute.For<IHttpTransportManager>();
        this.aiManager = Substitute.For<IAiManager>();
        this.resourceService = Substitute.For<ISystemResourceService>();

        this.resourceService.GetSubsystemTelemetry().Returns(new List<SubsystemTelemetryReport>
        {
            new() { SubsystemId = "bittorrent", SubsystemName = "BitTorrent Engine", Status = "Healthy" },
            new() { SubsystemId = "mediainspector", SubsystemName = "Media Container & Stream Inspector", Status = "Healthy" },
            new() { SubsystemId = "networkbinding", SubsystemName = "Network Interface Binding", Status = "Healthy" },
        });

        this.controller = new SubsystemsController(
            this.torrentEngineManager,
            this.extractorManager,
            this.mediaInspectorManager,
            this.geoIpManager,
            this.blocklistManager,
            this.networkBindingManager,
            this.mediaMetadataManager,
            this.httpTransportManager,
            this.aiManager,
            this.resourceService);
    }

    [Test]
    public void GetSubsystemMetrics_WithExactId_ReturnsReport()
    {
        var actionResult = this.controller.GetSubsystemMetrics("bittorrent");
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var report = okResult!.Value as SubsystemTelemetryReport;
        report.Should().NotBeNull();
        report!.SubsystemId.Should().Be("bittorrent");
    }

    [Test]
    public void GetSubsystemMetrics_WithAlias_ReturnsReport()
    {
        var actionResult = this.controller.GetSubsystemMetrics("torrentengine");
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var report = okResult!.Value as SubsystemTelemetryReport;
        report.Should().NotBeNull();
        report!.SubsystemId.Should().Be("bittorrent");
    }

    [Test]
    public void GetSubsystemMetrics_CaseInsensitive_ReturnsReport()
    {
        var actionResult = this.controller.GetSubsystemMetrics("BitTorrent");
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var report = okResult!.Value as SubsystemTelemetryReport;
        report.Should().NotBeNull();
        report!.SubsystemId.Should().Be("bittorrent");

        var aliasResult = this.controller.GetSubsystemMetrics("INSPECTOR");
        var aliasOkResult = aliasResult.Result as OkObjectResult;
        aliasOkResult.Should().NotBeNull();
        var aliasReport = aliasOkResult!.Value as SubsystemTelemetryReport;
        aliasReport.Should().NotBeNull();
        aliasReport!.SubsystemId.Should().Be("mediainspector");
    }

    [Test]
    public void GetSubsystemMetrics_WithUnknownSubsystem_ReturnsNotFound()
    {
        var actionResult = this.controller.GetSubsystemMetrics("unknown_subsystem");
        actionResult.Result.Should().BeOfType<NotFoundObjectResult>();
    }
}

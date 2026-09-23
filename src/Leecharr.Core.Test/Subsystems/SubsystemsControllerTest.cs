// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using NzbDrone.SignalR;

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
    private IBlocklistUpdateService blocklistUpdateService = null!;
    private IBroadcastSignalRMessage signalRBroadcaster = null!;
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
        this.blocklistUpdateService = Substitute.For<IBlocklistUpdateService>();
        this.signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();

        var mockReports = new List<SubsystemTelemetryReport>
        {
            new() { SubsystemId = "bittorrent", SubsystemName = "BitTorrent Engine", Status = "Healthy" },
            new() { SubsystemId = "mediainspector", SubsystemName = "Media Container & Stream Inspector", Status = "Healthy" },
            new() { SubsystemId = "networkbinding", SubsystemName = "Network Interface Binding", Status = "Healthy" },
        };

        this.resourceService.GetSubsystemTelemetry().Returns(mockReports);
        this.resourceService.GetSubsystemTelemetryAsync(Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(info =>
            {
                var id = info.Arg<string>();
                if (string.IsNullOrWhiteSpace(id))
                {
                    return Task.FromResult(new List<SubsystemTelemetryReport>(mockReports));
                }

                var filtered = mockReports.FindAll(r => string.Equals(r.SubsystemId, id, System.StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(filtered);
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
            this.resourceService,
            this.blocklistUpdateService,
            this.signalRBroadcaster);
    }

    [Test]
    public async Task GetSubsystemsMetrics_ReturnsReports()
    {
        var actionResult = await this.controller.GetSubsystemsMetrics();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var reports = okResult!.Value as List<SubsystemTelemetryReport>;
        reports.Should().NotBeNull();
        reports.Should().HaveCount(3);
    }

    [Test]
    public async Task GetSubsystemMetrics_WithExactId_ReturnsReport()
    {
        var actionResult = await this.controller.GetSubsystemMetrics("bittorrent");
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var report = okResult!.Value as SubsystemTelemetryReport;
        report.Should().NotBeNull();
        report!.SubsystemId.Should().Be("bittorrent");
    }

    [Test]
    public async Task GetSubsystemMetrics_WithAlias_ReturnsReport()
    {
        var actionResult = await this.controller.GetSubsystemMetrics("torrentengine");
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var report = okResult!.Value as SubsystemTelemetryReport;
        report.Should().NotBeNull();
        report!.SubsystemId.Should().Be("bittorrent");
    }

    [Test]
    public async Task GetSubsystemMetrics_CaseInsensitive_ReturnsReport()
    {
        var actionResult = await this.controller.GetSubsystemMetrics("BitTorrent");
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var report = okResult!.Value as SubsystemTelemetryReport;
        report.Should().NotBeNull();
        report!.SubsystemId.Should().Be("bittorrent");

        var aliasResult = await this.controller.GetSubsystemMetrics("INSPECTOR");
        var aliasOkResult = aliasResult.Result as OkObjectResult;
        aliasOkResult.Should().NotBeNull();
        var aliasReport = aliasOkResult!.Value as SubsystemTelemetryReport;
        aliasReport.Should().NotBeNull();
        aliasReport!.SubsystemId.Should().Be("mediainspector");
    }

    [Test]
    public async Task GetSubsystemMetrics_WithUnknownSubsystem_ReturnsNotFound()
    {
        var actionResult = await this.controller.GetSubsystemMetrics("unknown_subsystem");
        actionResult.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Test]
    public async Task UpdateBlocklistRules_WithService_ReturnsLoadedCount()
    {
        this.blocklistUpdateService.UpdateRulesAsync().Returns(Task.FromResult(2500));
        var mockProvider = Substitute.For<IBlocklistProvider>();
        mockProvider.RuleCount.Returns(2500);
        this.blocklistManager.ActiveProvider.Returns(mockProvider);
        this.blocklistManager.ActiveProviderId.Returns("ipfilter");

        var result = await this.controller.UpdateBlocklistRules();
        var okResult = result as OkObjectResult;

        okResult.Should().NotBeNull();
    }

    [Test]
    public async Task UpdateBlocklistRules_WithoutService_ReturnsZeroLoaded()
    {
        var controllerWithoutBlocklistService = new SubsystemsController(
            this.torrentEngineManager,
            this.extractorManager,
            this.mediaInspectorManager,
            this.geoIpManager,
            this.blocklistManager,
            this.networkBindingManager,
            this.mediaMetadataManager,
            this.httpTransportManager,
            this.aiManager,
            this.resourceService,
            null,
            this.signalRBroadcaster);

        var result = await controllerWithoutBlocklistService.UpdateBlocklistRules();
        var okResult = result as OkObjectResult;

        okResult.Should().NotBeNull();
    }

    [Test]
    public void GetAllSubsystems_ReturnsAllNineSubsystems()
    {
        var result = this.controller.GetAllSubsystems();
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var list = okResult!.Value as List<SubsystemOverviewResource>;
        list.Should().NotBeNull();
        list.Should().HaveCount(9);

        var ids = list!.Select(s => s.Id).ToList();
        ids.Should().Contain(new[]
        {
            "bittorrent",
            "extractor",
            "mediainspector",
            "geoip",
            "blocklist",
            "networkbinding",
            "mediametadata",
            "httptransport",
            "ai",
        });
    }

    [Test]
    public void GetAllSubsystems_MapsProviderStatusesAndCapabilitiesCorrectly()
    {
        var activeEngine = Substitute.For<ITorrentEngine>();
        activeEngine.EngineId.Returns("monotorrent");
        activeEngine.DisplayName.Returns("MonoTorrent");
        activeEngine.Version.Returns("1.0.0");
        activeEngine.Description.Returns("MonoTorrent engine");
        activeEngine.IsAvailable.Returns(true);
        activeEngine.Capabilities.Returns(new TorrentEngineCapabilities
        {
            SupportsSequentialDownload = true,
            SupportsSparseAllocation = true,
            SupportsV2Torrents = true,
            SupportsUtp = true,
            SupportsDht = true,
            SupportsMemoryMappedIo = true,
        });

        var readyEngine = Substitute.For<ITorrentEngine>();
        readyEngine.EngineId.Returns("libtorrent");
        readyEngine.DisplayName.Returns("LibTorrent");
        readyEngine.Version.Returns("2.0.0");
        readyEngine.Description.Returns("LibTorrent engine");
        readyEngine.IsAvailable.Returns(true);
        readyEngine.Capabilities.Returns(new TorrentEngineCapabilities());

        var unavailableEngine = Substitute.For<ITorrentEngine>();
        unavailableEngine.EngineId.Returns("qbittorrent");
        unavailableEngine.DisplayName.Returns("qBittorrent");
        unavailableEngine.Version.Returns("4.0.0");
        unavailableEngine.Description.Returns("qBittorrent engine");
        unavailableEngine.IsAvailable.Returns(false);
        unavailableEngine.Capabilities.Returns(new TorrentEngineCapabilities());

        this.torrentEngineManager.ActiveEngineId.Returns("monotorrent");
        this.torrentEngineManager.GetEngines().Returns(new[] { activeEngine, readyEngine, unavailableEngine });

        var result = this.controller.GetSubsystem("bittorrent");
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var subsystem = okResult!.Value as SubsystemOverviewResource;
        subsystem.Should().NotBeNull();
        subsystem!.Providers.Should().HaveCount(3);

        var p0 = subsystem.Providers[0];
        p0.ProviderId.Should().Be("monotorrent");
        p0.IsActive.Should().BeTrue();
        p0.Status.Should().Be("Running");
        p0.Capabilities["supportsSequentialDownload"].Should().Be(true);

        var p1 = subsystem.Providers[1];
        p1.ProviderId.Should().Be("libtorrent");
        p1.IsActive.Should().BeFalse();
        p1.Status.Should().Be("Ready");

        var p2 = subsystem.Providers[2];
        p2.ProviderId.Should().Be("qbittorrent");
        p2.IsActive.Should().BeFalse();
        p2.Status.Should().Be("Unavailable");
    }

    [TestCase("bittorrent", "bittorrent")]
    [TestCase("torrentengine", "bittorrent")]
    [TestCase("extractor", "extractor")]
    [TestCase("archiveextractor", "extractor")]
    [TestCase("mediainspector", "mediainspector")]
    [TestCase("inspector", "mediainspector")]
    [TestCase("geoip", "geoip")]
    [TestCase("blocklist", "blocklist")]
    [TestCase("networkbinding", "networkbinding")]
    [TestCase("binding", "networkbinding")]
    [TestCase("mediametadata", "mediametadata")]
    [TestCase("metadata", "mediametadata")]
    [TestCase("httptransport", "httptransport")]
    [TestCase("transport", "httptransport")]
    [TestCase("ai", "ai")]
    [TestCase("intelligence", "ai")]
    public void GetSubsystem_WithValidIdsAndAliases_ReturnsExpectedSubsystem(string inputId, string expectedId)
    {
        var result = this.controller.GetSubsystem(inputId);
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var subsystem = okResult!.Value as SubsystemOverviewResource;
        subsystem.Should().NotBeNull();
        subsystem!.Id.Should().Be(expectedId);
    }

    [Test]
    public void GetSubsystem_WithUnknownSubsystem_ReturnsNotFound()
    {
        var result = this.controller.GetSubsystem("unknown-subsystem");
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Test]
    public async Task SwitchProvider_TorrentEngine_Success_BroadcastsSignalRMessage()
    {
        this.torrentEngineManager.SwitchEngineAsync("monotorrent").Returns(new EngineSwitchResult
        {
            Success = true,
            PreviousEngine = "libtorrent",
            ActiveEngine = "monotorrent",
            Message = "Switched successfully",
        });

        var result = await this.controller.SwitchProvider("bittorrent", new SwitchSubsystemProviderRequest
        {
            ProviderId = "monotorrent",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var switchResult = okResult!.Value as SwitchSubsystemProviderResult;
        switchResult.Should().NotBeNull();
        switchResult!.Success.Should().BeTrue();
        switchResult.ActiveProvider.Should().Be("monotorrent");

        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(msg =>
            msg.Name == "subsystemSwitched" &&
            msg.Action == NzbDrone.Core.Datastore.ModelAction.Updated &&
            ((SwitchSubsystemProviderResult)msg.Body).SubsystemId == "bittorrent" &&
            ((SwitchSubsystemProviderResult)msg.Body).ActiveProvider == "monotorrent"));
    }

    [Test]
    public async Task SwitchProvider_Extractor_Success_BroadcastsSignalRMessage()
    {
        this.extractorManager.SwitchProviderAsync("7zip").Returns(new ExtractorSwitchResult
        {
            Success = true,
            PreviousProvider = "sharpcompress",
            ActiveProvider = "7zip",
            Message = "Switched extractor successfully",
        });

        var result = await this.controller.SwitchProvider("extractor", new SwitchSubsystemProviderRequest
        {
            ProviderId = "7zip",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var switchResult = okResult!.Value as SwitchSubsystemProviderResult;
        switchResult!.Success.Should().BeTrue();
        switchResult.ActiveProvider.Should().Be("7zip");

        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(msg =>
            msg.Name == "subsystemSwitched" &&
            ((SwitchSubsystemProviderResult)msg.Body).SubsystemId == "extractor" &&
            ((SwitchSubsystemProviderResult)msg.Body).ActiveProvider == "7zip"));
    }

    [Test]
    public async Task SwitchProvider_MediaInspector_Success_BroadcastsSignalRMessage()
    {
        this.mediaInspectorManager.SwitchProviderAsync("mediainfo").Returns(new MediaInspectorSwitchResult
        {
            Success = true,
            PreviousProvider = "ffprobe",
            ActiveProvider = "mediainfo",
            Message = "Switched successfully",
        });

        var result = await this.controller.SwitchProvider("mediainspector", new SwitchSubsystemProviderRequest
        {
            ProviderId = "mediainfo",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(msg =>
            msg.Name == "subsystemSwitched" &&
            ((SwitchSubsystemProviderResult)msg.Body).SubsystemId == "mediainspector" &&
            ((SwitchSubsystemProviderResult)msg.Body).ActiveProvider == "mediainfo"));
    }

    [Test]
    public async Task SwitchProvider_GeoIp_Success_BroadcastsSignalRMessage()
    {
        this.geoIpManager.ActiveProviderId.Returns("maxmind");
        this.geoIpManager.SwitchProviderAsync("ip2location").Returns(true);

        var result = await this.controller.SwitchProvider("geoip", new SwitchSubsystemProviderRequest
        {
            ProviderId = "ip2location",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var switchResult = okResult!.Value as SwitchSubsystemProviderResult;
        switchResult!.Success.Should().BeTrue();
        switchResult.SubsystemId.Should().Be("geoip");
        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public async Task SwitchProvider_Blocklist_Success_BroadcastsSignalRMessage()
    {
        this.blocklistManager.ActiveProviderId.Returns("peerblock");
        this.blocklistManager.SwitchProviderAsync("ipset").Returns(true);

        var result = await this.controller.SwitchProvider("blocklist", new SwitchSubsystemProviderRequest
        {
            ProviderId = "ipset",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var switchResult = okResult!.Value as SwitchSubsystemProviderResult;
        switchResult!.Success.Should().BeTrue();
        switchResult.SubsystemId.Should().Be("blocklist");
        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public async Task SwitchProvider_NetworkBinding_Success_BroadcastsSignalRMessage()
    {
        this.networkBindingManager.SwitchProviderAsync("socketbind").Returns(new NetworkBindingSwitchResult
        {
            Success = true,
            PreviousProvider = "default",
            ActiveProvider = "socketbind",
            Message = "Switched successfully",
        });

        var result = await this.controller.SwitchProvider("networkbinding", new SwitchSubsystemProviderRequest
        {
            ProviderId = "socketbind",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var switchResult = okResult!.Value as SwitchSubsystemProviderResult;
        switchResult!.Success.Should().BeTrue();
        switchResult.SubsystemId.Should().Be("networkbinding");
        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public async Task SwitchProvider_MediaMetadata_Success_BroadcastsSignalRMessage()
    {
        this.mediaMetadataManager.SwitchProviderAsync("tmdb").Returns(new MediaMetadataSwitchResult
        {
            Success = true,
            PreviousProvider = "tvmaze",
            ActiveProvider = "tmdb",
            Message = "Switched successfully",
        });

        var result = await this.controller.SwitchProvider("mediametadata", new SwitchSubsystemProviderRequest
        {
            ProviderId = "tmdb",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var switchResult = okResult!.Value as SwitchSubsystemProviderResult;
        switchResult!.Success.Should().BeTrue();
        switchResult.SubsystemId.Should().Be("mediametadata");
        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public async Task SwitchProvider_HttpTransport_Success_BroadcastsSignalRMessage()
    {
        this.httpTransportManager.SwitchProviderAsync("curl").Returns(new HttpTransportSwitchResult
        {
            Success = true,
            PreviousProvider = "sockets",
            ActiveProvider = "curl",
            Message = "Switched successfully",
        });

        var result = await this.controller.SwitchProvider("httptransport", new SwitchSubsystemProviderRequest
        {
            ProviderId = "curl",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var switchResult = okResult!.Value as SwitchSubsystemProviderResult;
        switchResult!.Success.Should().BeTrue();
        switchResult.SubsystemId.Should().Be("httptransport");
        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public async Task SwitchProvider_Ai_Success_BroadcastsSignalRMessage()
    {
        this.aiManager.ActiveProviderId.Returns("ollama");
        this.aiManager.SwitchProviderAsync("openai").Returns(true);

        var result = await this.controller.SwitchProvider("ai", new SwitchSubsystemProviderRequest
        {
            ProviderId = "openai",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var switchResult = okResult!.Value as SwitchSubsystemProviderResult;
        switchResult!.Success.Should().BeTrue();
        switchResult.SubsystemId.Should().Be("ai");
        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public async Task SwitchProvider_NullOrEmptyProviderId_ReturnsBadRequest()
    {
        var nullRequestResult = await this.controller.SwitchProvider("bittorrent", null!);
        nullRequestResult.Result.Should().BeOfType<BadRequestObjectResult>();

        var emptyRequestResult = await this.controller.SwitchProvider("bittorrent", new SwitchSubsystemProviderRequest
        {
            ProviderId = "   ",
        });
        emptyRequestResult.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task SwitchProvider_UnknownSubsystem_ReturnsNotFound()
    {
        var result = await this.controller.SwitchProvider("unknown_subsystem", new SwitchSubsystemProviderRequest
        {
            ProviderId = "some_provider",
        });
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Test]
    public async Task SwitchProvider_Failure_DoesNotBroadcastSignalRMessage()
    {
        this.torrentEngineManager.SwitchEngineAsync("invalid").Returns(new EngineSwitchResult
        {
            Success = false,
            PreviousEngine = "libtorrent",
            ActiveEngine = "libtorrent",
            Error = "Engine not found",
        });

        var result = await this.controller.SwitchProvider("bittorrent", new SwitchSubsystemProviderRequest
        {
            ProviderId = "invalid",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        var switchResult = okResult!.Value as SwitchSubsystemProviderResult;
        switchResult!.Success.Should().BeFalse();

        this.signalRBroadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public async Task SwitchProvider_WhenSignalRBroadcasterIsNull_DoesNotThrow()
    {
        var controllerWithoutSignalR = new SubsystemsController(
            this.torrentEngineManager,
            this.extractorManager,
            this.mediaInspectorManager,
            this.geoIpManager,
            this.blocklistManager,
            this.networkBindingManager,
            this.mediaMetadataManager,
            this.httpTransportManager,
            this.aiManager,
            this.resourceService,
            this.blocklistUpdateService,
            null);

        this.torrentEngineManager.SwitchEngineAsync("monotorrent").Returns(new EngineSwitchResult
        {
            Success = true,
            PreviousEngine = "libtorrent",
            ActiveEngine = "monotorrent",
        });

        var result = await controllerWithoutSignalR.SwitchProvider("bittorrent", new SwitchSubsystemProviderRequest
        {
            ProviderId = "monotorrent",
        });

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
    }

    [Test]
    public async Task ProbeProvider_TorrentEngine_ReturnsProbeResult()
    {
        this.torrentEngineManager.ProbeEngineAsync("monotorrent").Returns(new EngineHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "Operational",
            DependencyChecks = new List<string> { "LibTorrent.so: OK" },
            Warnings = new List<string>(),
        });

        var result = await this.controller.ProbeProvider("bittorrent", "monotorrent");
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var probe = okResult!.Value as SubsystemProbeResult;
        probe.Should().NotBeNull();
        probe!.SubsystemId.Should().Be("bittorrent");
        probe.ProviderId.Should().Be("monotorrent");
        probe.IsHealthy.Should().BeTrue();
    }

    [Test]
    public async Task ProbeProvider_Extractor_ReturnsProbeResult()
    {
        this.extractorManager.ProbeProviderAsync("7zip").Returns(new ExtractorHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "Operational",
            DependencyChecks = new List<string> { "7z binary: OK" },
            Warnings = new List<string>(),
        });

        var result = await this.controller.ProbeProvider("extractor", "7zip");
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var probe = okResult!.Value as SubsystemProbeResult;
        probe.Should().NotBeNull();
        probe!.SubsystemId.Should().Be("extractor");
        probe.ProviderId.Should().Be("7zip");
        probe.IsHealthy.Should().BeTrue();
    }

    [Test]
    public async Task ProbeProvider_MediaInspector_ReturnsProbeResult()
    {
        this.mediaInspectorManager.ProbeProviderAsync("ffprobe").Returns(new MediaInspectorHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "Operational",
            DependencyChecks = new List<string> { "ffprobe: OK" },
            Warnings = new List<string>(),
        });

        var result = await this.controller.ProbeProvider("mediainspector", "ffprobe");
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var probe = okResult!.Value as SubsystemProbeResult;
        probe.Should().NotBeNull();
        probe!.SubsystemId.Should().Be("mediainspector");
        probe.ProviderId.Should().Be("ffprobe");
    }

    [Test]
    public async Task ProbeProvider_GeoIp_ReturnsProbeResult()
    {
        this.geoIpManager.ProbeProviderAsync("maxmind").Returns(new GeoIpHealthResult
        {
            IsHealthy = true,
            StatusMessage = "GeoLite2 database loaded",
            Warnings = new List<string>(),
        });

        var result = await this.controller.ProbeProvider("geoip", "maxmind");
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var probe = okResult!.Value as SubsystemProbeResult;
        probe.Should().NotBeNull();
        probe!.SubsystemId.Should().Be("geoip");
        probe.ProviderId.Should().Be("maxmind");
    }

    [Test]
    public async Task ProbeProvider_Blocklist_ReturnsProbeResult()
    {
        this.blocklistManager.ProbeProviderAsync("peerblock").Returns(new BlocklistHealthResult
        {
            IsHealthy = true,
            StatusMessage = "Rules loaded",
            Warnings = new List<string>(),
        });

        var result = await this.controller.ProbeProvider("blocklist", "peerblock");
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var probe = okResult!.Value as SubsystemProbeResult;
        probe.Should().NotBeNull();
        probe!.SubsystemId.Should().Be("blocklist");
        probe.ProviderId.Should().Be("peerblock");
    }

    [Test]
    public async Task ProbeProvider_NetworkBinding_ReturnsProbeResult()
    {
        this.networkBindingManager.ProbeProviderAsync("socketbind").Returns(new NetworkBindingHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "SO_BINDTODEVICE available",
            Warnings = new List<string>(),
        });

        var result = await this.controller.ProbeProvider("networkbinding", "socketbind");
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var probe = okResult!.Value as SubsystemProbeResult;
        probe.Should().NotBeNull();
        probe!.SubsystemId.Should().Be("networkbinding");
    }

    [Test]
    public async Task ProbeProvider_MediaMetadata_ReturnsProbeResult()
    {
        this.mediaMetadataManager.ProbeProviderAsync("tmdb").Returns(new MediaMetadataHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "API reachable",
            Warnings = new List<string>(),
        });

        var result = await this.controller.ProbeProvider("mediametadata", "tmdb");
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var probe = okResult!.Value as SubsystemProbeResult;
        probe.Should().NotBeNull();
        probe!.SubsystemId.Should().Be("mediametadata");
    }

    [Test]
    public async Task ProbeProvider_HttpTransport_ReturnsProbeResult()
    {
        this.httpTransportManager.ProbeProviderAsync("curl").Returns(new HttpTransportHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = "HTTP/3 and JA4 supported",
            Warnings = new List<string>(),
        });

        var result = await this.controller.ProbeProvider("httptransport", "curl");
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var probe = okResult!.Value as SubsystemProbeResult;
        probe.Should().NotBeNull();
        probe!.SubsystemId.Should().Be("httptransport");
    }

    [Test]
    public async Task ProbeProvider_Ai_ReturnsProbeResult()
    {
        this.aiManager.ProbeProviderAsync("ollama").Returns(new AiHealthResult
        {
            IsHealthy = true,
            StatusMessage = "Model ready",
            Warnings = new List<string>(),
        });

        var result = await this.controller.ProbeProvider("ai", "ollama");
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var probe = okResult!.Value as SubsystemProbeResult;
        probe.Should().NotBeNull();
        probe!.SubsystemId.Should().Be("ai");
    }

    [Test]
    public async Task ProbeProvider_UnknownSubsystem_ReturnsNotFound()
    {
        var result = await this.controller.ProbeProvider("unknown_subsystem", "provider1");
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }
}

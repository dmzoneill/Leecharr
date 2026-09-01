// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Leecharr.Api.V1.Network;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class NetworkControllerTest
{
    private INetworkStatusService networkStatusService = null!;
    private IConfigService configService = null!;
    private IDownloadEngine downloadEngine = null!;
    private NetworkController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.networkStatusService = Substitute.For<INetworkStatusService>();
        this.configService = Substitute.For<IConfigService>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();

        this.networkStatusService.GetStatus().Returns(new NetworkStatus
        {
            LocalIp = "192.168.1.100",
            ExternalIp = "203.0.113.1",
            ListenPort = 7889,
            UpnpAvailable = true,
            ProxyEnabled = false,
            LocalAddresses = new List<string> { "192.168.1.100", "10.0.0.1" },
            PortMappings = new List<PortMappingInfo>
            {
                new()
                {
                    InternalPort = 7889,
                    ExternalPort = 7889,
                    Protocol = "TCP",
                    Description = "Web UI",
                    IsActive = true,
                },
                new()
                {
                    InternalPort = 51413,
                    ExternalPort = 51413,
                    Protocol = "TCP/UDP",
                    Description = "BitTorrent Swarm",
                    IsActive = true,
                },
            },
        });

        this.networkStatusService.GetLocalAddresses().Returns(new List<string> { "192.168.1.100", "10.0.0.1" });

        this.configService.ListeningPort.Returns(51413);
        this.configService.MaxUploadSlots.Returns(8);
        this.configService.EnableDht.Returns(true);
        this.configService.EncryptionMode.Returns("preferEncrypted");

        this.controller = new NetworkController(
            this.networkStatusService,
            this.configService,
            this.downloadEngine);
    }

    [Test]
    public void GetStatus_ReturnsNetworkStatus()
    {
        var result = this.controller.GetStatus();

        result.Value.Should().NotBeNull();
        result.Value!.LocalIp.Should().Be("192.168.1.100");
        result.Value.ExternalIp.Should().Be("203.0.113.1");
    }

    [Test]
    public void GetAddresses_ReturnsLocalAddresses()
    {
        var actionResult = this.controller.GetAddresses();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var addresses = okResult!.Value as List<string>;
        addresses.Should().NotBeNull();
        addresses!.Should().Contain("192.168.1.100");
    }

    [Test]
    public void GetDiagnostics_WithPeers_CalculatesEncryptionMetricsCorrectly()
    {
        var task1 = Substitute.For<IDownloadTask>();
        task1.GetPeers().Returns(new List<PeerInfo>
        {
            new() { Ip = "1.2.3.4", Port = 5000, IsEncrypted = true },
            new() { Ip = "5.6.7.8", Port = 5001, IsEncrypted = false },
            new() { Ip = "9.10.11.12", Port = 5002, IsEncrypted = true },
        });

        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask> { task1 });

        var actionResult = this.controller.GetDiagnostics();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var diag = okResult!.Value as NetworkDiagnosticsResource;
        diag.Should().NotBeNull();
        diag!.LocalIp.Should().Be("192.168.1.100");
        diag.ExternalIp.Should().Be("203.0.113.1");
        diag.ListeningPort.Should().Be(51413);
        diag.UploadSlots.Should().Be(8);
        diag.DhtEnabled.Should().BeTrue();
        diag.ActiveConnections.Should().Be(3);
        diag.EncryptedConnections.Should().Be(2);
        diag.PlaintextConnections.Should().Be(1);
        diag.EncryptionPercentage.Should().Be(66.7);
        diag.PortMappings.Should().HaveCount(2);
    }

    [Test]
    public void GetDiagnostics_WithoutPeers_Returns100PercentEncryption()
    {
        this.downloadEngine.GetAllTasks().Returns(new List<IDownloadTask>());

        var actionResult = this.controller.GetDiagnostics();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var diag = okResult!.Value as NetworkDiagnosticsResource;
        diag.Should().NotBeNull();
        diag!.ActiveConnections.Should().Be(0);
        diag.EncryptedConnections.Should().Be(0);
        diag.PlaintextConnections.Should().Be(0);
        diag.EncryptionPercentage.Should().Be(100.0);
    }

    [Test]
    public void GetDiagnostics_WhenServicesNull_DoesNotThrow()
    {
        var minimalController = new NetworkController(this.networkStatusService);

        var actionResult = minimalController.GetDiagnostics();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var diag = okResult!.Value as NetworkDiagnosticsResource;
        diag.Should().NotBeNull();
        diag!.ListeningPort.Should().Be(51413);
    }
}

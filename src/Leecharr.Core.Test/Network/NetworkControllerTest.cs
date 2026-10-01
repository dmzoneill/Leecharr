// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Network;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Network;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class NetworkControllerTest
{
    private INetworkStatusService networkStatusService = null!;
    private IConfigService configService = null!;
    private IDownloadEngine downloadEngine = null!;
    private INetworkSecurityService networkSecurityService = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private NetworkController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.networkStatusService = Substitute.For<INetworkStatusService>();
        this.configService = Substitute.For<IConfigService>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.networkSecurityService = Substitute.For<INetworkSecurityService>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.networkSecurityService.GetAvailableNetworkInterfaces().Returns(new List<string> { "eth0", "tun0", "wg0" });

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
            this.downloadEngine,
            this.networkSecurityService,
            this.safeHttpClientService);
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

    [Test]
    public void GetInterfaces_ReturnsListOfInterfaces()
    {
        var actionResult = this.controller.GetInterfaces();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var interfaces = okResult!.Value as List<string>;
        interfaces.Should().NotBeNull();
        interfaces.Should().Contain(new[] { "eth0", "tun0", "wg0" });
    }

    [Test]
    public async Task TestPort_WhenPortIsOpen_ReturnsOpen()
    {
        this.safeHttpClientService.DownloadStringAsync(
            Arg.Is<string>(url => url.Contains("51413")),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns("1");

        var actionResult = await this.controller.TestPort(new PortTestRequest { Port = 51413 });

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var result = okResult!.Value as PortTestResult;
        result.Should().NotBeNull();
        result!.Port.Should().Be(51413);
        result.IsOpen.Should().BeTrue();
        result.Message.Should().Contain("open");
    }

    [Test]
    public async Task TestPort_WhenPortIsClosed_ReturnsClosed()
    {
        this.safeHttpClientService.DownloadStringAsync(
            Arg.Is<string>(url => url.Contains("51413")),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns("0");

        var actionResult = await this.controller.TestPort(new PortTestRequest { Port = 51413 });

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var result = okResult!.Value as PortTestResult;
        result.Should().NotBeNull();
        result!.Port.Should().Be(51413);
        result.IsOpen.Should().BeFalse();
        result.Message.Should().Contain("closed");
    }

    [Test]
    public async Task TestPort_WhenCustomPortProvided_TestsSpecifiedPort()
    {
        this.safeHttpClientService.DownloadStringAsync(
            Arg.Is<string>(url => url.Contains("6881")),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>())
            .Returns("1");

        var actionResult = await this.controller.TestPort(new PortTestRequest { Port = 6881 });

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var result = okResult!.Value as PortTestResult;
        result.Should().NotBeNull();
        result!.Port.Should().Be(6881);
        result.IsOpen.Should().BeTrue();
    }

    [Test]
    public async Task TestPort_WhenPortOutOfRange_ReturnsBadRequest()
    {
        var actionResult = await this.controller.TestPort(new PortTestRequest { Port = 70000 });

        var badRequestResult = actionResult.Result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
    }
}

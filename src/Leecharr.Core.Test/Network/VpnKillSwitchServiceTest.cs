// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Sockets;
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
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.PortMapping;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class VpnKillSwitchServiceTest
{
    private INetworkSettingsRepository repository = null!;
    private IEventAggregator eventAggregator = null!;
    private IConfigService configService = null!;
    private VpnKillSwitchService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<INetworkSettingsRepository>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.configService = Substitute.For<IConfigService>();

        this.service = new VpnKillSwitchService(this.repository, this.eventAggregator, this.configService);
    }

    [TearDown]
    public void TearDown()
    {
        this.service.Dispose();
    }

    [Test]
    public void CheckVpnState_WhenKillSwitchDisabled_ReturnsFalseAndDoesNotEngageFailClosed()
    {
        var settings = new NetworkSettings
        {
            EnableVpnKillSwitch = false,
            BindInterface = "tun0",
        };
        this.repository.GetSettings().Returns(settings);

        var isKillSwitchTriggered = this.service.CheckVpnState();

        isKillSwitchTriggered.Should().BeFalse();
        this.service.IsFailClosedActive.Should().BeFalse();
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnKillSwitchTriggeredEvent>());
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnInterfaceRestoredEvent>());
    }

    [Test]
    public void CheckVpnState_WhenInterfaceDrops_EngagesFailClosedAndPublishesTriggeredEvent()
    {
        var settings = new NetworkSettings
        {
            EnableVpnKillSwitch = true,
            BindInterface = "tun0",
        };
        this.repository.GetSettings().Returns(settings);

        var vpnDroppedCalled = false;
        string droppedInterface = null;
        this.service.VpnDropped += iface =>
        {
            vpnDroppedCalled = true;
            droppedInterface = iface;
        };

        // Simulate interface drop
        this.service.InterfaceStatusCheck = _ => false;

        var triggered = this.service.CheckVpnState();

        triggered.Should().BeTrue();
        this.service.IsFailClosedActive.Should().BeTrue();
        vpnDroppedCalled.Should().BeTrue();
        droppedInterface.Should().Be("tun0");
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<VpnKillSwitchTriggeredEvent>(e => e.InterfaceName == "tun0"));
    }

    [Test]
    public void CheckVpnState_WhenInterfaceRestores_DisengagesFailClosedAndPublishesRestoredEvent()
    {
        var settings = new NetworkSettings
        {
            EnableVpnKillSwitch = true,
            BindInterface = "wg0",
        };
        this.repository.GetSettings().Returns(settings);

        var restoredCalled = false;
        string restoredInterface = null;
        this.service.VpnRestored += iface =>
        {
            restoredCalled = true;
            restoredInterface = iface;
        };

        // 1. First trigger drop
        this.service.InterfaceStatusCheck = _ => false;
        this.service.CheckVpnState();
        this.service.IsFailClosedActive.Should().BeTrue();

        // 2. Now simulate interface restoration
        this.service.InterfaceStatusCheck = _ => true;
        var triggered = this.service.CheckVpnState();

        triggered.Should().BeFalse();
        this.service.IsFailClosedActive.Should().BeFalse();
        restoredCalled.Should().BeTrue();
        restoredInterface.Should().Be("wg0");
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<VpnInterfaceRestoredEvent>(e => e.InterfaceName == "wg0"));
    }

    [Test]
    public void GetVpnInterfaceIpAddress_WhenFailClosedActive_ReturnsNull()
    {
        var settings = new NetworkSettings
        {
            EnableVpnKillSwitch = true,
            BindInterface = "tun0",
        };
        this.repository.GetSettings().Returns(settings);

        var testIp = IPAddress.Parse("10.8.0.2");
        this.service.InterfaceIpResolver = (_, _) => testIp;
        this.service.InterfaceStatusCheck = _ => false;

        // Trigger drop
        this.service.CheckVpnState();
        this.service.IsFailClosedActive.Should().BeTrue();

        // When fail-closed is active, it must never return an IP address
        var resolvedIp = this.service.GetVpnInterfaceIpAddress();
        resolvedIp.Should().BeNull();

        // Restore interface
        this.service.InterfaceStatusCheck = _ => true;
        this.service.CheckVpnState();
        this.service.IsFailClosedActive.Should().BeFalse();

        // Now returns the VPN IP
        resolvedIp = this.service.GetVpnInterfaceIpAddress();
        resolvedIp.Should().Be(testIp);
    }

    [Test]
    public void MonoTorrentDownloadEngine_WhenKillSwitchDrops_ImmediatelyHaltsAndSetsHaltedState()
    {
        var storagePathService = Substitute.For<IStoragePathService>();
        var categoryService = Substitute.For<ICategoryService>();
        var diskProvider = Substitute.For<IDiskProvider>();
        var mockVpnService = Substitute.For<IVpnKillSwitchService>();

        this.configService.EnableVpnKillSwitch.Returns(true);
        this.configService.BindInterface.Returns("tun0");

        using var engine = new MonoTorrentDownloadEngine(
            this.configService,
            storagePathService,
            categoryService,
            diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpnService);

        engine.IsHaltedByKillSwitch.Should().BeFalse();

        // Trigger kill switch event
        engine.Handle(new VpnKillSwitchTriggeredEvent("tun0"));

        engine.IsHaltedByKillSwitch.Should().BeTrue();
    }

    [Test]
    public void MonoTorrentDownloadEngine_WhenVpnRestores_ClearsHaltedState()
    {
        var storagePathService = Substitute.For<IStoragePathService>();
        var categoryService = Substitute.For<ICategoryService>();
        var diskProvider = Substitute.For<IDiskProvider>();
        var mockVpnService = Substitute.For<IVpnKillSwitchService>();

        this.configService.EnableVpnKillSwitch.Returns(true);
        this.configService.BindInterface.Returns("tun0");

        using var engine = new MonoTorrentDownloadEngine(
            this.configService,
            storagePathService,
            categoryService,
            diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpnService);

        // First trigger drop
        engine.Handle(new VpnKillSwitchTriggeredEvent("tun0"));
        engine.IsHaltedByKillSwitch.Should().BeTrue();

        // Then trigger restoration
        engine.Handle(new VpnInterfaceRestoredEvent("tun0"));
        engine.IsHaltedByKillSwitch.Should().BeFalse();
    }

    [Test]
    public async Task MonoTorrentDownloadEngine_StartAsync_WhenKillSwitchActiveAndInterfaceMissing_EnforcesFailClosedHalt()
    {
        var storagePathService = Substitute.For<IStoragePathService>();
        var categoryService = Substitute.For<ICategoryService>();
        var diskProvider = Substitute.For<IDiskProvider>();
        var mockVpnService = Substitute.For<IVpnKillSwitchService>();

        this.configService.EnableVpnKillSwitch.Returns(true);
        this.configService.BindInterface.Returns("nonexistent_tun0");
        mockVpnService.IsKillSwitchEnabled.Returns(true);
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns((IPAddress)null);

        using var engine = new MonoTorrentDownloadEngine(
            this.configService,
            storagePathService,
            categoryService,
            diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpnService);

        await engine.StartAsync();

        // Strict fail-closed: must be halted and not bound to default IPAddress.Any
        engine.IsHaltedByKillSwitch.Should().BeTrue();
    }

    [Test]
    public void EmbeddedTransmissionEngine_WhenKillSwitchTriggered_HaltsAndClearsOnRestore()
    {
        var storagePathService = Substitute.For<IStoragePathService>();
        var categoryService = Substitute.For<ICategoryService>();
        var diskProvider = Substitute.For<IDiskProvider>();

        using var engine = new EmbeddedTransmissionEngine(
            this.configService,
            storagePathService,
            categoryService,
            diskProvider,
            this.eventAggregator);

        engine.IsHaltedByKillSwitch.Should().BeFalse();

        engine.Handle(new VpnKillSwitchTriggeredEvent("wg0"));
        engine.IsHaltedByKillSwitch.Should().BeTrue();

        engine.Handle(new VpnInterfaceRestoredEvent("wg0"));
        engine.IsHaltedByKillSwitch.Should().BeFalse();
    }

    [Test]
    public void LibTorrentDownloadEngine_WhenKillSwitchTriggered_HaltsAndClearsOnRestore()
    {
        var storagePathService = Substitute.For<IStoragePathService>();
        var categoryService = Substitute.For<ICategoryService>();
        var diskProvider = Substitute.For<IDiskProvider>();

        using var engine = new LibTorrentDownloadEngine(
            this.configService,
            storagePathService,
            categoryService,
            diskProvider,
            this.eventAggregator);

        engine.IsHaltedByKillSwitch.Should().BeFalse();

        engine.Handle(new VpnKillSwitchTriggeredEvent("wg0"));
        engine.IsHaltedByKillSwitch.Should().BeTrue();

        engine.Handle(new VpnInterfaceRestoredEvent("wg0"));
        engine.IsHaltedByKillSwitch.Should().BeFalse();
    }
}

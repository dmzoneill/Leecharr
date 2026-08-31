// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Binding;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class DynamicNetworkBindingProxyTest
{
    private INetworkBindingProvider managedSocketProvider = null!;
    private INetworkBindingProvider linuxBindProvider = null!;
    private INetworkBindingProvider proxyTunnelProvider = null!;
    private IConfigService configService = null!;
    private IEventAggregator eventAggregator = null!;
    private DynamicNetworkBindingProxy proxy = null!;

    [SetUp]
    public void SetUp()
    {
        this.managedSocketProvider = Substitute.For<INetworkBindingProvider>();
        this.managedSocketProvider.ProviderId.Returns("ManagedSocket");
        this.managedSocketProvider.DisplayName.Returns("Managed Socket Binding (.NET Standard)");
        this.managedSocketProvider.IsAvailable.Returns(true);
        this.managedSocketProvider.Capabilities.Returns(new NetworkBindingCapabilities
        {
            SupportsInterfaceBinding = true,
            SupportsVpnKillSwitch = true,
        });
        this.managedSocketProvider.ProbeHealthAsync().Returns(Task.FromResult(new NetworkBindingHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.managedSocketProvider.IsInterfaceUp(Arg.Any<string>()).Returns(true);

        this.linuxBindProvider = Substitute.For<INetworkBindingProvider>();
        this.linuxBindProvider.ProviderId.Returns("LinuxBindToDevice");
        this.linuxBindProvider.DisplayName.Returns("Linux Kernel Device Binding (SO_BINDTODEVICE)");
        this.linuxBindProvider.IsAvailable.Returns(true);
        this.linuxBindProvider.Capabilities.Returns(new NetworkBindingCapabilities
        {
            SupportsInterfaceBinding = true,
            SupportsSoBindToDevice = true,
            SupportsVpnKillSwitch = true,
        });
        this.linuxBindProvider.ProbeHealthAsync().Returns(Task.FromResult(new NetworkBindingHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.linuxBindProvider.IsInterfaceUp(Arg.Any<string>()).Returns(true);

        this.proxyTunnelProvider = Substitute.For<INetworkBindingProvider>();
        this.proxyTunnelProvider.ProviderId.Returns("ProxyTunnel");
        this.proxyTunnelProvider.DisplayName.Returns("Proxy Tunnel Binding (SOCKS5 / Tor Onion)");
        this.proxyTunnelProvider.IsAvailable.Returns(true);
        this.proxyTunnelProvider.Capabilities.Returns(new NetworkBindingCapabilities
        {
            SupportsSocks5Proxy = true,
            SupportsTorOnion = true,
            SupportsAnonymousRouting = true,
        });
        this.proxyTunnelProvider.ProbeHealthAsync().Returns(Task.FromResult(new NetworkBindingHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        this.proxyTunnelProvider.IsInterfaceUp(Arg.Any<string>()).Returns(true);

        this.configService = Substitute.For<IConfigService>();
        this.configService.ActiveNetworkBindingProvider.Returns("ManagedSocket");

        this.eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<INetworkBindingProvider> { this.managedSocketProvider, this.linuxBindProvider, this.proxyTunnelProvider };

        this.proxy = new DynamicNetworkBindingProxy(
            providers,
            this.configService,
            this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        this.proxy.ActiveProviderId.Should().Be("ManagedSocket");
        this.proxy.ActiveProvider.Should().BeSameAs(this.managedSocketProvider);
    }

    [Test]
    public void Constructor_WhenConfigEmpty_FallsBackToDefaultOrFirst()
    {
        var config = Substitute.For<IConfigService>();
        config.ActiveNetworkBindingProvider.Returns(string.Empty);

        using var proxy = new DynamicNetworkBindingProxy(
            new[] { this.managedSocketProvider, this.linuxBindProvider },
            config,
            this.eventAggregator);

        proxy.ActiveProviderId.Should().Be("ManagedSocket");
    }

    [Test]
    public void Constructor_WhenNoProviders_ThrowsInvalidOperationException()
    {
        var act = () => new DynamicNetworkBindingProxy(
            Enumerable.Empty<INetworkBindingProvider>(),
            this.configService,
            this.eventAggregator);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = this.proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "ManagedSocket", "LinuxBindToDevice", "ProxyTunnel" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = this.proxy.GetProvider("linuxbindtodevice");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("LinuxBindToDevice");
    }

    [Test]
    public void GetProvider_WithInvalidOrEmptyId_ReturnsNull()
    {
        this.proxy.GetProvider("NonExistent").Should().BeNull();
        this.proxy.GetProvider(string.Empty).Should().BeNull();
        this.proxy.GetProvider(null).Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await this.proxy.ProbeProviderAsync("LinuxBindToDevice");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeProviderAsync_WithInvalidProvider_ReturnsUnhealthy()
    {
        var probe = await this.proxy.ProbeProviderAsync("InvalidProvider");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProviderAndPersistsConfig()
    {
        var result = await this.proxy.SwitchProviderAsync("LinuxBindToDevice");

        result.Success.Should().BeTrue();
        result.PreviousProvider.Should().Be("ManagedSocket");
        result.ActiveProvider.Should().Be("LinuxBindToDevice");

        this.proxy.ActiveProviderId.Should().Be("LinuxBindToDevice");
        this.proxy.ActiveProvider.Should().BeSameAs(this.linuxBindProvider);

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveNetworkBindingProvider"] == "LinuxBindToDevice"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<NetworkBindingProviderSwitchedEvent>(e => e.PreviousProvider == "ManagedSocket" && e.NewProvider == "LinuxBindToDevice"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await this.proxy.SwitchProviderAsync("ManagedSocket");

        result.Success.Should().BeTrue();
        result.ActiveProvider.Should().Be("ManagedSocket");

        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownOrEmptyProvider_ReturnsFailure()
    {
        var result1 = await this.proxy.SwitchProviderAsync("UnknownProvider");
        result1.Success.Should().BeFalse();
        result1.Error.Should().Contain("not registered");

        var result2 = await this.proxy.SwitchProviderAsync(string.Empty);
        result2.Success.Should().BeFalse();
        result2.Error.Should().Contain("empty");

        this.proxy.ActiveProviderId.Should().Be("ManagedSocket");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        this.linuxBindProvider.ProbeHealthAsync().Returns(Task.FromResult(new NetworkBindingHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "Not supported on this platform",
        }));

        var result = await this.proxy.SwitchProviderAsync("LinuxBindToDevice");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        this.proxy.ActiveProviderId.Should().Be("ManagedSocket");
    }

    [Test]
    public void Delegation_ForwardsBindSocketAndIsInterfaceUpToActiveProvider()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        this.proxy.BindSocket(socket, "eth0");
        this.managedSocketProvider.Received(1).BindSocket(socket, "eth0");

        var isUp = this.proxy.IsInterfaceUp("eth0");
        isUp.Should().BeTrue();
        this.managedSocketProvider.Received(1).IsInterfaceUp("eth0");
    }

    [Test]
    public void CheckVpnKillSwitch_WhenInterfaceIsUp_ReturnsFalse()
    {
        this.managedSocketProvider.IsInterfaceUp("tun0").Returns(true);

        var killSwitchTriggered = this.proxy.CheckVpnKillSwitch("tun0");
        killSwitchTriggered.Should().BeFalse();
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnKillSwitchTriggeredEvent>());
    }

    [Test]
    public void CheckVpnKillSwitch_WhenInterfaceDropped_PublishesEventAndReturnsTrue()
    {
        this.managedSocketProvider.IsInterfaceUp("tun0").Returns(false);

        var killSwitchTriggered = this.proxy.CheckVpnKillSwitch("tun0");
        killSwitchTriggered.Should().BeTrue();
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<VpnKillSwitchTriggeredEvent>(e => e.InterfaceName == "tun0"));
    }

    [Test]
    public void CheckVpnKillSwitch_WhenInterfaceEmpty_ReturnsFalse()
    {
        var killSwitchTriggered = this.proxy.CheckVpnKillSwitch(string.Empty);
        killSwitchTriggered.Should().BeFalse();
    }

    [Test]
    public async Task ConcreteProviders_ManagedSocketBindingProvider_Tests()
    {
        var provider = new ManagedSocketBindingProvider();
        provider.ProviderId.Should().Be("ManagedSocket");
        provider.DisplayName.Should().NotBeNullOrEmpty();
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.SupportsInterfaceBinding.Should().BeTrue();
        provider.Capabilities.SupportsVpnKillSwitch.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();

        provider.IsInterfaceUp(string.Empty).Should().BeTrue();
        provider.IsInterfaceUp("non_existent_iface_xyz_99").Should().BeFalse();
    }

    [Test]
    public async Task ConcreteProviders_LinuxBindToDeviceProvider_Tests()
    {
        var provider = new LinuxBindToDeviceProvider();
        provider.ProviderId.Should().Be("LinuxBindToDevice");
        provider.DisplayName.Should().NotBeNullOrEmpty();
        provider.Capabilities.SupportsSoBindToDevice.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.Should().NotBeNull();

        provider.IsInterfaceUp(string.Empty).Should().BeTrue();
    }

    [Test]
    public async Task ConcreteProviders_ProxyTunnelBindingProvider_Tests()
    {
        var config = Substitute.For<IConfigService>();
        config.ProxyHost.Returns("127.0.0.1");
        config.ProxyPort.Returns(9050);
        config.ProxyType.Returns("SOCKS5");

        var provider = new ProxyTunnelBindingProvider(config);
        provider.ProviderId.Should().Be("ProxyTunnel");
        provider.Capabilities.SupportsTorOnion.Should().BeTrue();
        provider.Capabilities.SupportsAnonymousRouting.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("SOCKS5");

        provider.IsInterfaceUp("any").Should().BeTrue();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var act = () => provider.BindSocket(socket, "any");
        act.Should().NotThrow();
    }
}

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
    private INetworkBindingProvider _managedSocketProvider = null!;
    private INetworkBindingProvider _linuxBindProvider = null!;
    private INetworkBindingProvider _proxyTunnelProvider = null!;
    private IConfigService _configService = null!;
    private IEventAggregator _eventAggregator = null!;
    private DynamicNetworkBindingProxy _proxy = null!;

    [SetUp]
    public void SetUp()
    {
        _managedSocketProvider = Substitute.For<INetworkBindingProvider>();
        _managedSocketProvider.ProviderId.Returns("ManagedSocket");
        _managedSocketProvider.DisplayName.Returns("Managed Socket Binding (.NET Standard)");
        _managedSocketProvider.IsAvailable.Returns(true);
        _managedSocketProvider.Capabilities.Returns(new NetworkBindingCapabilities
        {
            SupportsInterfaceBinding = true,
            SupportsVpnKillSwitch = true
        });
        _managedSocketProvider.ProbeHealthAsync().Returns(Task.FromResult(new NetworkBindingHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _managedSocketProvider.IsInterfaceUp(Arg.Any<string>()).Returns(true);

        _linuxBindProvider = Substitute.For<INetworkBindingProvider>();
        _linuxBindProvider.ProviderId.Returns("LinuxBindToDevice");
        _linuxBindProvider.DisplayName.Returns("Linux Kernel Device Binding (SO_BINDTODEVICE)");
        _linuxBindProvider.IsAvailable.Returns(true);
        _linuxBindProvider.Capabilities.Returns(new NetworkBindingCapabilities
        {
            SupportsInterfaceBinding = true,
            SupportsSoBindToDevice = true,
            SupportsVpnKillSwitch = true
        });
        _linuxBindProvider.ProbeHealthAsync().Returns(Task.FromResult(new NetworkBindingHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _linuxBindProvider.IsInterfaceUp(Arg.Any<string>()).Returns(true);

        _proxyTunnelProvider = Substitute.For<INetworkBindingProvider>();
        _proxyTunnelProvider.ProviderId.Returns("ProxyTunnel");
        _proxyTunnelProvider.DisplayName.Returns("Proxy Tunnel Binding (SOCKS5 / Tor Onion)");
        _proxyTunnelProvider.IsAvailable.Returns(true);
        _proxyTunnelProvider.Capabilities.Returns(new NetworkBindingCapabilities
        {
            SupportsSocks5Proxy = true,
            SupportsTorOnion = true,
            SupportsAnonymousRouting = true
        });
        _proxyTunnelProvider.ProbeHealthAsync().Returns(Task.FromResult(new NetworkBindingHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _proxyTunnelProvider.IsInterfaceUp(Arg.Any<string>()).Returns(true);

        _configService = Substitute.For<IConfigService>();
        _configService.ActiveNetworkBindingProvider.Returns("ManagedSocket");

        _eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<INetworkBindingProvider> { _managedSocketProvider, _linuxBindProvider, _proxyTunnelProvider };

        _proxy = new DynamicNetworkBindingProxy(
            providers,
            _configService,
            _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        _proxy.ActiveProviderId.Should().Be("ManagedSocket");
        _proxy.ActiveProvider.Should().BeSameAs(_managedSocketProvider);
    }

    [Test]
    public void Constructor_WhenConfigEmpty_FallsBackToDefaultOrFirst()
    {
        var config = Substitute.For<IConfigService>();
        config.ActiveNetworkBindingProvider.Returns(string.Empty);

        using var proxy = new DynamicNetworkBindingProxy(
            new[] { _managedSocketProvider, _linuxBindProvider },
            config,
            _eventAggregator);

        proxy.ActiveProviderId.Should().Be("ManagedSocket");
    }

    [Test]
    public void Constructor_WhenNoProviders_ThrowsInvalidOperationException()
    {
        var act = () => new DynamicNetworkBindingProxy(
            Enumerable.Empty<INetworkBindingProvider>(),
            _configService,
            _eventAggregator);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = _proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "ManagedSocket", "LinuxBindToDevice", "ProxyTunnel" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = _proxy.GetProvider("linuxbindtodevice");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("LinuxBindToDevice");
    }

    [Test]
    public void GetProvider_WithInvalidOrEmptyId_ReturnsNull()
    {
        _proxy.GetProvider("NonExistent").Should().BeNull();
        _proxy.GetProvider(string.Empty).Should().BeNull();
        _proxy.GetProvider(null).Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await _proxy.ProbeProviderAsync("LinuxBindToDevice");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeProviderAsync_WithInvalidProvider_ReturnsUnhealthy()
    {
        var probe = await _proxy.ProbeProviderAsync("InvalidProvider");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProviderAndPersistsConfig()
    {
        var result = await _proxy.SwitchProviderAsync("LinuxBindToDevice");

        result.Success.Should().BeTrue();
        result.PreviousProvider.Should().Be("ManagedSocket");
        result.ActiveProvider.Should().Be("LinuxBindToDevice");

        _proxy.ActiveProviderId.Should().Be("LinuxBindToDevice");
        _proxy.ActiveProvider.Should().BeSameAs(_linuxBindProvider);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveNetworkBindingProvider"] == "LinuxBindToDevice"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<NetworkBindingProviderSwitchedEvent>(e => e.PreviousProvider == "ManagedSocket" && e.NewProvider == "LinuxBindToDevice"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await _proxy.SwitchProviderAsync("ManagedSocket");

        result.Success.Should().BeTrue();
        result.ActiveProvider.Should().Be("ManagedSocket");

        _configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownOrEmptyProvider_ReturnsFailure()
    {
        var result1 = await _proxy.SwitchProviderAsync("UnknownProvider");
        result1.Success.Should().BeFalse();
        result1.Error.Should().Contain("not registered");

        var result2 = await _proxy.SwitchProviderAsync(string.Empty);
        result2.Success.Should().BeFalse();
        result2.Error.Should().Contain("empty");

        _proxy.ActiveProviderId.Should().Be("ManagedSocket");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        _linuxBindProvider.ProbeHealthAsync().Returns(Task.FromResult(new NetworkBindingHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "Not supported on this platform"
        }));

        var result = await _proxy.SwitchProviderAsync("LinuxBindToDevice");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        _proxy.ActiveProviderId.Should().Be("ManagedSocket");
    }

    [Test]
    public void Delegation_ForwardsBindSocketAndIsInterfaceUpToActiveProvider()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _proxy.BindSocket(socket, "eth0");
        _managedSocketProvider.Received(1).BindSocket(socket, "eth0");

        var isUp = _proxy.IsInterfaceUp("eth0");
        isUp.Should().BeTrue();
        _managedSocketProvider.Received(1).IsInterfaceUp("eth0");
    }

    [Test]
    public void CheckVpnKillSwitch_WhenInterfaceIsUp_ReturnsFalse()
    {
        _managedSocketProvider.IsInterfaceUp("tun0").Returns(true);

        var killSwitchTriggered = _proxy.CheckVpnKillSwitch("tun0");
        killSwitchTriggered.Should().BeFalse();
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnKillSwitchTriggeredEvent>());
    }

    [Test]
    public void CheckVpnKillSwitch_WhenInterfaceDropped_PublishesEventAndReturnsTrue()
    {
        _managedSocketProvider.IsInterfaceUp("tun0").Returns(false);

        var killSwitchTriggered = _proxy.CheckVpnKillSwitch("tun0");
        killSwitchTriggered.Should().BeTrue();
        _eventAggregator.Received(1).PublishEvent(Arg.Is<VpnKillSwitchTriggeredEvent>(e => e.InterfaceName == "tun0"));
    }

    [Test]
    public void CheckVpnKillSwitch_WhenInterfaceEmpty_ReturnsFalse()
    {
        var killSwitchTriggered = _proxy.CheckVpnKillSwitch(string.Empty);
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

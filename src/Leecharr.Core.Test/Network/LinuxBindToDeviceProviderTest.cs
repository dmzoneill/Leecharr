// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Network.Binding;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class LinuxBindToDeviceProviderTest
{
    private LinuxBindToDeviceProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        this.provider = new LinuxBindToDeviceProvider();
    }

    [Test]
    public void BindSocket_WhenSocketIsNull_ThrowsArgumentNullException()
    {
        var act = () => this.provider.BindSocket(null!, "eth0");
        act.Should().Throw<ArgumentNullException>();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BindSocket_WhenInterfaceIsNullOrEmpty_DoesNotThrow(string iface)
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        var act = () => this.provider.BindSocket(socket, iface);
        act.Should().NotThrow();
    }

    [TestCase("Any")]
    [TestCase("any")]
    [TestCase("all")]
    [TestCase("ALL")]
    public void BindSocket_WhenInterfaceIsAnyOrAll_DoesNotThrow(string iface)
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        var act = () => this.provider.BindSocket(socket, iface);
        act.Should().NotThrow();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Any")]
    [TestCase("any")]
    [TestCase("all")]
    [TestCase("ALL")]
    public void IsInterfaceUp_WhenInterfaceIsWildcardOrEmpty_ReturnsTrue(string iface)
    {
        this.provider.IsInterfaceUp(iface).Should().BeTrue();
    }

    [Test]
    public void IsInterfaceUp_WhenInterfaceDoesNotExist_ReturnsFalse()
    {
        this.provider.IsInterfaceUp("nonexistent_device_1234").Should().BeFalse();
    }

    [Test]
    public void BindSocket_WhenInterfaceExceedsMaxLinuxName_ThrowsArgumentException()
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        var longIface = new string('a', 16);
        var act = () => this.provider.BindSocket(socket, longIface);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void IsInterfaceUp_WhenInterfaceExceedsMaxLinuxName_ReturnsFalse()
    {
        var longIface = new string('a', 16);
        this.provider.IsInterfaceUp(longIface).Should().BeFalse();
    }

    [Test]
    public void BindSocket_OnLinux_WithLoopback_DoesNotThrow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Inconclusive("Linux-only SO_BINDTODEVICE test");
        }

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var act = () => this.provider.BindSocket(socket, "lo");
        act.Should().NotThrow();
    }

    [Test]
    public async Task ProbeHealthAsync_OnLinux_WhenSoBindToDeviceWorks_ReturnsHealthy()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Inconclusive("Linux-only SO_BINDTODEVICE test");
        }

        var health = await this.provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("SO_BINDTODEVICE");
    }

    [Test]
    public async Task ProbeHealthAsync_WhenNotLinux_ReturnsUnhealthy()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Inconclusive("Non-Linux SO_BINDTODEVICE availability test");
        }

        var health = await this.provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("only supported on Linux");
    }

    [Test]
    public void BindSocket_UsesSolSocketLevel_NotSocketOptionLevelSocket()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Inconclusive("Linux-only SO_BINDTODEVICE level regression (#1689 / #1170)");
        }

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var actWrongLevel = () => socket.SetRawSocketOption((int)SocketOptionLevel.Socket, 25, System.Text.Encoding.ASCII.GetBytes("lo\0"));
        actWrongLevel.Should().Throw<SocketException>();

        var act = () => this.provider.BindSocket(socket, "lo");
        act.Should().NotThrow();
    }

    [Test]
    public async Task ProbeHealthAsync_WhenSoBindToDeviceAccessDenied_ReturnsUnhealthy()
    {
        var denied = new AccessDeniedBindProvider();
        var health = await denied.ProbeHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("CAP_NET_RAW");
    }

    [Test]
    public async Task ProbeHealthAsync_WhenNativeEperm_ReturnsUnhealthy()
    {
        var denied = new NativeEpermBindProvider();
        var health = await denied.ProbeHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("CAP_NET_RAW");
    }

    [Test]
    public void BindSocket_WhenSoBindToDeviceAccessDenied_BindsInterfaceAddress()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Inconclusive("Linux loopback address fallback");
        }

        var denied = new AccessDeniedBindProvider();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var act = () => denied.BindSocket(socket, "lo");
        act.Should().NotThrow();
        socket.IsBound.Should().BeTrue();
        ((IPEndPoint)socket.LocalEndPoint!).Address.Should().Be(IPAddress.Loopback);
    }

    private sealed class AccessDeniedBindProvider : LinuxBindToDeviceProvider
    {
        protected override bool IsBindToDevicePlatform => true;

        protected override void ApplySoBindToDevice(Socket socket, byte[] option)
        {
            throw new SocketException((int)SocketError.AccessDenied);
        }
    }

    private sealed class NativeEpermBindProvider : LinuxBindToDeviceProvider
    {
        protected override bool IsBindToDevicePlatform => true;

        protected override void ApplySoBindToDevice(Socket socket, byte[] option)
        {
            throw new SocketException(1);
        }
    }
}

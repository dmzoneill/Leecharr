// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net.Sockets;
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
}

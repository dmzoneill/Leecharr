// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Network.Binding;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class ManagedSocketBindingProviderTest
{
    private ManagedSocketBindingProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        this.provider = new ManagedSocketBindingProvider();
    }

    [Test]
    public void BindSocket_WhenSocketIsNull_ThrowsArgumentNullException()
    {
        var act = () => this.provider.BindSocket(null!, "tun0");
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

    [Test]
    public void BindSocket_WhenInterfaceDoesNotExist_ThrowsInvalidOperationExceptionFailClosed()
    {
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        var act = () => this.provider.BindSocket(socket, "nonexistent_tun_9999");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*kill-switch active*");
    }

    [Test]
    public void IsInterfaceUp_WhenInterfaceDoesNotExist_ReturnsFalse()
    {
        this.provider.IsInterfaceUp("nonexistent_tun_9999").Should().BeFalse();
    }
}

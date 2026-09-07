// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
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

    [Test]
    public void SelectIpAddress_WhenGivenGlobalAndLinkLocalIPv6_PrioritizesGlobalIPv6()
    {
        var addresses = new[]
        {
            IPAddress.Parse("fe80::1"),
            IPAddress.Parse("2001:db8::1234"),
        };

        var selected = ManagedSocketBindingProvider.SelectIpAddress(addresses, 5, AddressFamily.InterNetworkV6);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("2001:db8::1234"));
    }

    [Test]
    public void SelectIpAddress_WhenGivenMultipleGlobalIPv6_ReturnsFirstGlobalIPv6()
    {
        var addresses = new[]
        {
            IPAddress.Parse("2600:1700::1"),
            IPAddress.Parse("2001:db8::1"),
        };

        var selected = ManagedSocketBindingProvider.SelectIpAddress(addresses, 5, AddressFamily.InterNetworkV6);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("2600:1700::1"));
    }

    [Test]
    public void SelectIpAddress_WhenOnlyLinkLocalIPv6AndNoScopeId_AssignsScopeIdFromInterfaceIndex()
    {
        var addresses = new[]
        {
            IPAddress.Parse("fe80::cafe:babe"),
        };

        var selected = ManagedSocketBindingProvider.SelectIpAddress(addresses, 7, AddressFamily.InterNetworkV6);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("fe80::cafe:babe%7"));
        selected!.ScopeId.Should().Be(7);
    }

    [Test]
    public void SelectIpAddress_WhenOnlyLinkLocalIPv6AndExistingScopeId_PreservesExistingScopeId()
    {
        var existingScopedIp = IPAddress.Parse("fe80::cafe:babe%3");
        var addresses = new[] { existingScopedIp };

        var selected = ManagedSocketBindingProvider.SelectIpAddress(addresses, 7, AddressFamily.InterNetworkV6);

        selected.Should().NotBeNull();
        selected!.ScopeId.Should().Be(3);
    }

    [Test]
    public void SelectIpAddress_WhenIPv6LoopbackAndSiteLocal_SkipsThemForGlobal()
    {
        var addresses = new[]
        {
            IPAddress.IPv6Loopback,
            IPAddress.Parse("fec0::1"),
            IPAddress.IPv6Any,
            IPAddress.Parse("2001:db8::5"),
        };

        var selected = ManagedSocketBindingProvider.SelectIpAddress(addresses, 2, AddressFamily.InterNetworkV6);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("2001:db8::5"));
    }

    [Test]
    public void SelectIpAddress_WhenIPv4_ReturnsNonLoopbackIPv4()
    {
        var addresses = new[]
        {
            IPAddress.Loopback,
            IPAddress.Parse("192.168.1.50"),
        };

        var selected = ManagedSocketBindingProvider.SelectIpAddress(addresses, (int?)null, AddressFamily.InterNetwork);

        selected.Should().NotBeNull();
        selected.Should().Be(IPAddress.Parse("192.168.1.50"));
    }

    [Test]
    public void SelectIpAddress_WhenEmptyOrNull_ReturnsNull()
    {
        ManagedSocketBindingProvider.SelectIpAddress(null, 1, AddressFamily.InterNetworkV6).Should().BeNull();
        ManagedSocketBindingProvider.SelectIpAddress(Array.Empty<IPAddress>(), 1, AddressFamily.InterNetworkV6).Should().BeNull();
        ManagedSocketBindingProvider.SelectIpAddress(Array.Empty<IPAddress>(), (int?)null, AddressFamily.InterNetwork).Should().BeNull();
        ManagedSocketBindingProvider.SelectIpAddress(Array.Empty<IPAddress>(), (IPInterfaceProperties)null, AddressFamily.InterNetwork).Should().BeNull();
    }

    [Test]
    public void SelectIpAddress_WhenNoMatchingFamily_ReturnsNull()
    {
        var addresses = new[] { IPAddress.Parse("192.168.1.1") };
        ManagedSocketBindingProvider.SelectIpAddress(addresses, 1, AddressFamily.InterNetworkV6).Should().BeNull();
    }

    [Test]
    public void SelectIpAddress_WhenOnlyIPv6Loopback_ReturnsNull()
    {
        var addresses = new[] { IPAddress.IPv6Loopback, IPAddress.IPv6Any };
        ManagedSocketBindingProvider.SelectIpAddress(addresses, 1, AddressFamily.InterNetworkV6).Should().BeNull();
    }

    [Test]
    public void GetInterfaceIp_WhenInterfaceDoesNotExist_ReturnsNull()
    {
        ManagedSocketBindingProvider.GetInterfaceIp("nonexistent_iface_9999", AddressFamily.InterNetworkV6).Should().BeNull();
        ManagedSocketBindingProvider.GetInterfaceIp("nonexistent_iface_9999", AddressFamily.InterNetwork).Should().BeNull();
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class SocketsHttpHandlerProviderTest
{
    private IConfigService configService = null!;
    private INetworkBindingService networkBindingService = null!;
    private IVpnKillSwitchService vpnKillSwitchService = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.networkBindingService = Substitute.For<INetworkBindingService>();
        this.vpnKillSwitchService = Substitute.For<IVpnKillSwitchService>();
    }

    [Test]
    public async Task ConnectCallback_WhenKillSwitchFailClosedActive_ThrowsNetworkUnreachable()
    {
        this.vpnKillSwitchService.IsFailClosedActive.Returns(true);
        var provider = new SocketsHttpHandlerProvider(this.configService, this.networkBindingService, this.vpnKillSwitchService);

        var context = CreateConnectionContext(new DnsEndPoint("127.0.0.1", 80));
        var act = async () => await provider.Handler.ConnectCallback!(context, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<SocketException>();
        ex.Which.SocketErrorCode.Should().Be(SocketError.NetworkUnreachable);
    }

    [Test]
    public async Task ConnectCallback_WhenBindInterfaceConfigured_BindsSocket()
    {
        this.vpnKillSwitchService.IsFailClosedActive.Returns(false);
        this.configService.BindInterface.Returns("tun0");

        var provider = new SocketsHttpHandlerProvider(this.configService, this.networkBindingService, this.vpnKillSwitchService);
        var context = CreateConnectionContext(new DnsEndPoint("127.0.0.1", 80));

        try
        {
            await provider.Handler.ConnectCallback!(context, CancellationToken.None);
        }
        catch
        {
            // Ignored if connection fails after socket is created and bound
        }

        this.networkBindingService.Received().BindSocket(Arg.Any<Socket>(), "tun0");
    }

    [Test]
    public async Task DynamicHttpTransportProxy_SendAsync_WhenKillSwitchFailClosedActive_ThrowsNetworkUnreachable()
    {
        this.vpnKillSwitchService.IsFailClosedActive.Returns(true);
        var provider = Substitute.For<IHttpTransportProvider>();
        provider.ProviderId.Returns("SocketsHttpHandler");
        provider.DisplayName.Returns("SocketsHttpHandler");
        provider.IsAvailable.Returns(true);

        var eventAggregator = Substitute.For<IEventAggregator>();
        var proxy = new DynamicHttpTransportProxy(new[] { provider }, this.configService, eventAggregator, this.vpnKillSwitchService);

        var act = async () => await proxy.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/test"));

        var ex = await act.Should().ThrowAsync<SocketException>();
        ex.Which.SocketErrorCode.Should().Be(SocketError.NetworkUnreachable);
        await provider.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Test]
    public async Task SafeHttpClientService_ConnectCallback_WhenKillSwitchFailClosedActive_ThrowsNetworkUnreachable()
    {
        this.vpnKillSwitchService.IsFailClosedActive.Returns(true);
        var service = new SafeHttpClientService(
            transportEngine: null,
            configService: this.configService,
            configFileProvider: null,
            httpClient: null,
            networkBindingService: this.networkBindingService,
            vpnKillSwitchService: this.vpnKillSwitchService);

        var handler = service.CreateSafeSocketsHttpHandlerInternal();
        var context = CreateConnectionContext(new DnsEndPoint("127.0.0.1", 80));

        var act = async () => await handler.ConnectCallback!(context, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<SocketException>();
        ex.Which.SocketErrorCode.Should().Be(SocketError.NetworkUnreachable);
    }

    [Test]
    public async Task SafeHttpClientService_ConnectCallback_WhenBindInterfaceConfigured_BindsSocket()
    {
        this.vpnKillSwitchService.IsFailClosedActive.Returns(false);
        this.configService.BindInterface.Returns("tun0");
        this.configService.AllowPrivateNetworkRequests.Returns(true);

        var service = new SafeHttpClientService(
            transportEngine: null,
            configService: this.configService,
            configFileProvider: null,
            httpClient: null,
            networkBindingService: this.networkBindingService,
            vpnKillSwitchService: this.vpnKillSwitchService);

        var handler = service.CreateSafeSocketsHttpHandlerInternal();
        var context = CreateConnectionContext(new DnsEndPoint("127.0.0.1", 80));

        try
        {
            await handler.ConnectCallback!(context, CancellationToken.None);
        }
        catch
        {
            // Ignored if connection fails after socket is created and bound
        }

        this.networkBindingService.Received().BindSocket(Arg.Any<Socket>(), "tun0");
    }

    private static SocketsHttpConnectionContext CreateConnectionContext(DnsEndPoint endPoint)
    {
        var context = (SocketsHttpConnectionContext)RuntimeHelpers.GetUninitializedObject(typeof(SocketsHttpConnectionContext));
        foreach (var field in typeof(SocketsHttpConnectionContext).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (field.FieldType == typeof(DnsEndPoint))
            {
                field.SetValue(context, endPoint);
            }
        }

        return context;
    }
}

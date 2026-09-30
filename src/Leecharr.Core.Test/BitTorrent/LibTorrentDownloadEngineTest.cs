// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
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
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class LibTorrentDownloadEngineTest
{
    private IConfigService configService = null!;
    private IStoragePathService storagePathService = null!;
    private ICategoryService categoryService = null!;
    private IDiskProvider diskProvider = null!;
    private IEventAggregator eventAggregator = null!;
    private INetworkBindingService networkBindingService = null!;
    private IVpnKillSwitchService vpnKillSwitchService = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.storagePathService = Substitute.For<IStoragePathService>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.networkBindingService = Substitute.For<INetworkBindingService>();
        this.vpnKillSwitchService = Substitute.For<IVpnKillSwitchService>();
    }

    [Test]
    public void LibTorrentDownloadEngine_ImplementsIHandleConfigSavedEvent()
    {
        typeof(IHandle<ConfigSavedEvent>).IsAssignableFrom(typeof(LibTorrentDownloadEngine)).Should().BeTrue();
    }

    [Test]
    public void ResolveBoundIpv4Address_WhenNoInterfaceConfigured_ReturnsAnyIpv4()
    {
        this.configService.NetworkInterfaceBinding.Returns(string.Empty);
        this.configService.BindInterface.Returns(string.Empty);

        using var engine = this.CreateEngine();

        var ip = engine.ResolveBoundIpv4Address();

        ip.Should().Be("0.0.0.0");
    }

    [Test]
    public void ResolveBoundIpv4Address_WhenVpnKillSwitchActive_FailsClosedToLoopback()
    {
        this.vpnKillSwitchService.IsFailClosedActive.Returns(true);

        using var engine = this.CreateEngine();

        var ip = engine.ResolveBoundIpv4Address();

        ip.Should().Be("127.0.0.1");
        ip.Should().NotBe("0.0.0.0");
    }

    [Test]
    public async Task ConfigureSessionSettingsAsync_DispatchesBoundIpAndPort()
    {
        this.configService.NetworkInterfaceBinding.Returns("tun0");
        this.configService.ListeningPort.Returns(6885);
        this.vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork)
            .Returns(IPAddress.Parse("10.200.1.5"));

        string capturedBody = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult()!;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"settings_applied\"}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody);
        var paramsObj = doc.RootElement.GetProperty("params");

        paramsObj.GetProperty("listen_interfaces").GetString().Should().Be("10.200.1.5:6885");
        paramsObj.GetProperty("listening_port").GetInt32().Should().Be(6885);
    }

    [Test]
    public async Task ConfigureSessionSettingsAsync_WhenProxyConfigured_DispatchesProxySettingsToRpc()
    {
        this.configService.ProxyType.Returns("socks5");
        this.configService.ProxyHost.Returns("127.0.0.1");
        this.configService.ProxyPort.Returns(1080);
        this.configService.ProxyAuthEnabled.Returns(true);
        this.configService.ProxyUsername.Returns("testuser");
        this.configService.ProxyPassword.Returns("secretpass");
        this.configService.ForceProxy.Returns(true);

        string capturedBody = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult()!;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"settings_applied\"}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody);
        var paramsObj = doc.RootElement.GetProperty("params");

        paramsObj.GetProperty("proxy_type").GetString().Should().Be("socks5");
        paramsObj.GetProperty("proxy_hostname").GetString().Should().Be("127.0.0.1");
        paramsObj.GetProperty("proxy_port").GetInt32().Should().Be(1080);
        paramsObj.GetProperty("proxy_auth_enabled").GetBoolean().Should().BeTrue();
        paramsObj.GetProperty("proxy_username").GetString().Should().Be("testuser");
        paramsObj.GetProperty("proxy_password").GetString().Should().Be("secretpass");
        paramsObj.GetProperty("force_proxy").GetBoolean().Should().BeTrue();
        paramsObj.GetProperty("proxy_peer_connections").GetBoolean().Should().BeTrue();
        paramsObj.GetProperty("proxy_tracker_connections").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task ConfigureSessionSettingsAsync_WhenProxyDisabled_DispatchesProxyDisabled()
    {
        this.configService.ProxyType.Returns("none");

        string capturedBody = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult()!;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"settings_applied\"}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody);
        var paramsObj = doc.RootElement.GetProperty("params");

        paramsObj.GetProperty("proxy_type").GetString().Should().Be("none");
        paramsObj.GetProperty("proxy_port").GetInt32().Should().Be(0);
        paramsObj.GetProperty("force_proxy").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task ConfigureSessionSettingsAsync_DispatchesRateLimitsAndDhtSettings()
    {
        this.configService.MaxDownloadSpeedKbps.Returns(5000);
        this.configService.MaxUploadSpeedKbps.Returns(1000);
        this.configService.EnableDht.Returns(true);
        this.configService.AnonymousMode.Returns(false);
        this.configService.DhtBootstrapNodes.Returns("router.bittorrent.com:6881,dht.transmissionbt.com:6881");
        this.configService.MaxGlobalConnections.Returns(250);

        string capturedBody = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult()!;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"settings_applied\"}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody);
        var paramsObj = doc.RootElement.GetProperty("params");

        paramsObj.GetProperty("download_rate_limit").GetInt64().Should().Be(5000L * 1024L);
        paramsObj.GetProperty("upload_rate_limit").GetInt64().Should().Be(1000L * 1024L);
        paramsObj.GetProperty("enable_dht").GetBoolean().Should().BeTrue();
        paramsObj.GetProperty("anonymous_mode").GetBoolean().Should().BeFalse();
        paramsObj.GetProperty("dht_bootstrap_nodes").GetString().Should().Be("router.bittorrent.com:6881,dht.transmissionbt.com:6881");
        paramsObj.GetProperty("connections_limit").GetInt32().Should().Be(250);
    }

    [Test]
    public async Task ConfigureSessionSettingsAsync_WhenAnonymousModeEnabled_DisablesDhtAndEnablesAnonymous()
    {
        this.configService.EnableDht.Returns(true);
        this.configService.AnonymousMode.Returns(true);

        string capturedBody = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult()!;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"settings_applied\"}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody);
        var paramsObj = doc.RootElement.GetProperty("params");

        paramsObj.GetProperty("enable_dht").GetBoolean().Should().BeFalse();
        paramsObj.GetProperty("anonymous_mode").GetBoolean().Should().BeTrue();
    }

    private LibTorrentDownloadEngine CreateEngine(HttpClient? client = null)
    {
        return new LibTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            this.networkBindingService,
            this.vpnKillSwitchService,
            client);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.handler(request));
        }
    }
}

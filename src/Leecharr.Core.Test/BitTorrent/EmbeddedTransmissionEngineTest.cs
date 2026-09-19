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
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class EmbeddedTransmissionEngineTest
{
    private IConfigService configService;
    private IStoragePathService storagePathService;
    private ICategoryService categoryService;
    private IDiskProvider diskProvider;
    private IEventAggregator eventAggregator;
    private INetworkBindingService networkBindingService;
    private IVpnKillSwitchService vpnKillSwitchService;
    private IBlocklistService blocklistService;

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
        this.blocklistService = Substitute.For<IBlocklistService>();
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
    public void ResolveBoundIpv4Address_WhenDirectIpConfigured_ReturnsDirectIp()
    {
        this.configService.NetworkInterfaceBinding.Returns("192.168.1.150");

        using var engine = this.CreateEngine();

        var ip = engine.ResolveBoundIpv4Address();

        ip.Should().Be("192.168.1.150");
    }

    [Test]
    public void ResolveBoundIpv4Address_WhenVpnKillSwitchActive_FailsClosedToLoopback()
    {
        this.configService.NetworkInterfaceBinding.Returns("tun0");
        this.vpnKillSwitchService.IsFailClosedActive.Returns(true);

        using var engine = this.CreateEngine();

        var ip = engine.ResolveBoundIpv4Address();

        ip.Should().Be("127.0.0.1");
        ip.Should().NotBe("0.0.0.0");
    }

    [Test]
    public void ResolveBoundIpv4Address_WhenVpnInterfaceIpResolved_ReturnsVpnIpRatherThanAny()
    {
        this.configService.NetworkInterfaceBinding.Returns("tun0");
        this.vpnKillSwitchService.IsFailClosedActive.Returns(false);
        this.vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork)
            .Returns(IPAddress.Parse("10.8.0.42"));

        using var engine = this.CreateEngine();

        var ip = engine.ResolveBoundIpv4Address();

        ip.Should().Be("10.8.0.42");
        ip.Should().NotBe("0.0.0.0");
    }

    [Test]
    public void ResolveBoundIpv4Address_WhenNetworkBindingReportsInterfaceDown_FailsClosedToLoopback()
    {
        this.configService.NetworkInterfaceBinding.Returns("tun0");
        this.networkBindingService.IsInterfaceUp("tun0").Returns(false);

        using var engine = this.CreateEngine();

        var ip = engine.ResolveBoundIpv4Address();

        ip.Should().Be("127.0.0.1");
        ip.Should().NotBe("0.0.0.0");
    }

    [Test]
    public void ResolveBoundIpv4Address_WhenNetworkBindingReportsKillSwitch_FailsClosedToLoopback()
    {
        this.configService.NetworkInterfaceBinding.Returns("tun0");
        this.networkBindingService.CheckVpnKillSwitch("tun0").Returns(true);

        using var engine = this.CreateEngine();

        var ip = engine.ResolveBoundIpv4Address();

        ip.Should().Be("127.0.0.1");
        ip.Should().NotBe("0.0.0.0");
    }

    [Test]
    public void ResolveBoundIpv4Address_WhenInterfaceConfiguredButNotResolvable_FailsClosedToLoopback()
    {
        this.configService.NetworkInterfaceBinding.Returns("nonexistent_interface_xyz");

        using var engine = this.CreateEngine();

        var ip = engine.ResolveBoundIpv4Address();

        ip.Should().Be("127.0.0.1");
        ip.Should().NotBe("0.0.0.0");
    }

    [Test]
    public async Task ConfigureSessionSettingsAsync_DispatchesBoundIpRatherThanZeroZeroZeroZero()
    {
        this.configService.NetworkInterfaceBinding.Returns("tun0");
        this.vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork)
            .Returns(IPAddress.Parse("10.200.1.5"));

        string capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"success\",\"arguments\":{}}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        var body = capturedBody;
        using var doc = JsonDocument.Parse(body);
        var args = doc.RootElement.GetProperty("arguments");

        args.GetProperty("bind-address-ipv4").GetString().Should().Be("10.200.1.5");
        args.GetProperty("bind-address-ipv4").GetString().Should().NotBe("0.0.0.0");
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

        string capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"success\",\"arguments\":{}}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        var body = capturedBody;
        using var doc = JsonDocument.Parse(body);
        var args = doc.RootElement.GetProperty("arguments");

        args.GetProperty("proxy-enabled").GetBoolean().Should().BeTrue();
        args.GetProperty("proxy-type").GetString().Should().Be("socks5");
        args.GetProperty("proxy-host").GetString().Should().Be("127.0.0.1");
        args.GetProperty("proxy-port").GetInt32().Should().Be(1080);
        args.GetProperty("proxy-auth-enabled").GetBoolean().Should().BeTrue();
        args.GetProperty("proxy-auth-username").GetString().Should().Be("testuser");
        args.GetProperty("proxy-auth-password").GetString().Should().Be("secretpass");
    }

    [Test]
    public async Task ConfigureSessionSettingsAsync_WhenProxyDisabled_DispatchesProxyDisabled()
    {
        this.configService.ProxyType.Returns("none");

        string capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"success\",\"arguments\":{}}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        var body = capturedBody;
        using var doc = JsonDocument.Parse(body);
        var args = doc.RootElement.GetProperty("arguments");

        args.GetProperty("proxy-enabled").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task ConfigureSessionSettingsAsync_WhenBlocklistEnabled_DispatchesBlocklistSettings()
    {
        this.configService.BlocklistEnabled.Returns(true);
        this.configService.BlocklistUrl.Returns("https://list.iblocklist.com/lists/level1.gz");

        string capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"success\",\"arguments\":{}}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        var body = capturedBody;
        using var doc = JsonDocument.Parse(body);
        var args = doc.RootElement.GetProperty("arguments");

        args.GetProperty("blocklist-enabled").GetBoolean().Should().BeTrue();
        args.GetProperty("blocklist-url").GetString().Should().Be("https://list.iblocklist.com/lists/level1.gz");
    }

    [Test]
    public async Task ConfigureSessionSettingsAsync_WhenBlocklistDisabled_DispatchesBlocklistDisabled()
    {
        this.configService.BlocklistEnabled.Returns(false);

        string capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"success\",\"arguments\":{}}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        await engine.ConfigureSessionSettingsAsync();

        capturedBody.Should().NotBeNull();
        var body = capturedBody;
        using var doc = JsonDocument.Parse(body);
        var args = doc.RootElement.GetProperty("arguments");

        args.GetProperty("blocklist-enabled").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task NetworkBindingProviderSwitchedEvent_DispatchesBoundIpRatherThanZeroZeroZeroZero()
    {
        this.configService.NetworkInterfaceBinding.Returns("wg0");
        this.vpnKillSwitchService.GetVpnInterfaceIpAddress(AddressFamily.InterNetwork)
            .Returns(IPAddress.Parse("10.10.0.8"));

        var tcs = new TaskCompletionSource<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            tcs.TrySetResult(body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"success\",\"arguments\":{}}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        engine.Handle(new NetworkBindingProviderSwitchedEvent("oldProvider", "newProvider"));

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        completed.Should().Be(tcs.Task, "Handle should send session-set RPC asynchronously");

        var payload = await tcs.Task;
        using var doc = JsonDocument.Parse(payload);
        var args = doc.RootElement.GetProperty("arguments");

        args.GetProperty("bind-address-ipv4").GetString().Should().Be("10.10.0.8");
        args.GetProperty("bind-address-ipv4").GetString().Should().NotBe("0.0.0.0");
    }

    [Test]
    public async Task MoveTorrentFilesAsync_SendsTorrentSetLocationRpc_WithMoveFilesParameter()
    {
        var tcs = new TaskCompletionSource<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("torrent-set-location"))
            {
                tcs.TrySetResult(body);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"success\",\"arguments\":{}}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        var torrent = new Torrent { Id = 42, InfoHash = "0123456789abcdef0123456789abcdef01234567", Name = "Test" };
        await engine.AddTorrentAsync(torrent);

        await engine.MoveTorrentFilesAsync(42, "/new/save/path", moveFiles: false);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        completed.Should().Be(tcs.Task);

        var payload = await tcs.Task;
        using var doc = JsonDocument.Parse(payload);
        doc.RootElement.GetProperty("method").GetString().Should().Be("torrent-set-location");
        var args = doc.RootElement.GetProperty("arguments");
        args.GetProperty("location").GetString().Should().Be("/new/save/path");
        args.GetProperty("move").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task MoveTorrentFilesAsync_WhenRpcFails_RethrowsException()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var body = req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("torrent-set-location"))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"success\",\"arguments\":{}}"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var engine = this.CreateEngine(httpClient);

        var torrent = new Torrent { Id = 42, InfoHash = "0123456789abcdef0123456789abcdef01234567", Name = "Test" };
        await engine.AddTorrentAsync(torrent);

        var act = async () => await engine.MoveTorrentFilesAsync(42, "/new/save/path", moveFiles: true);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private EmbeddedTransmissionEngine CreateEngine(HttpClient client = null)
    {
        return new EmbeddedTransmissionEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            this.networkBindingService,
            this.vpnKillSwitchService,
            this.blocklistService,
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

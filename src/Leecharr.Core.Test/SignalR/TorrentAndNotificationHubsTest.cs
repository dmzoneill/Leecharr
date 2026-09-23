// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.SignalR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.SignalR;

[TestFixture]
public class TorrentAndNotificationHubsTest
{
    private IConfigFileProvider configFileProvider;
    private HubCallerContext hubCallerContext;
    private IHubCallerClients clients;
    private ISingleClientProxy callerProxy;
    private IClientProxy groupProxy;
    private IClientProxy allProxy;
    private IGroupManager groupManager;
    private DefaultHttpContext httpContext;
    private FeatureCollection featureCollection;
    private IUserService userService;

    [SetUp]
    public void SetUp()
    {
        TorrentHub.ResetForTesting();
        NotificationHub.ResetForTesting();

        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.hubCallerContext = Substitute.For<HubCallerContext>();
        this.clients = Substitute.For<IHubCallerClients>();
        this.callerProxy = Substitute.For<ISingleClientProxy>();
        this.groupProxy = Substitute.For<IClientProxy>();
        this.allProxy = Substitute.For<IClientProxy>();
        this.groupManager = Substitute.For<IGroupManager>();
        this.userService = Substitute.For<IUserService>();

        this.clients.Caller.Returns(this.callerProxy);
        this.clients.Group(Arg.Any<string>()).Returns(this.groupProxy);
        this.clients.All.Returns(this.allProxy);
        this.hubCallerContext.ConnectionId.Returns("conn-torrent-123");

        this.httpContext = new DefaultHttpContext();
        var httpContextFeature = Substitute.For<IHttpContextFeature>();
        httpContextFeature.HttpContext.Returns(this.httpContext);

        this.featureCollection = new FeatureCollection();
        this.featureCollection.Set<IHttpContextFeature>(httpContextFeature);
        this.hubCallerContext.Features.Returns(this.featureCollection);
    }

    [TearDown]
    public void TearDown()
    {
        TorrentHub.ResetForTesting();
        NotificationHub.ResetForTesting();
    }

    [Test]
    public async Task TorrentHub_OnConnectedAsync_WhenAuthDisabled_AcceptsConnectionAndSendsTorrentConnected()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeTrue();
        TorrentHub.ActiveConnectionCount.Should().Be(1);
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "torrentConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_OnConnectedAsync_WhenAuthEnabledAndUserAuthenticated_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-torrent-key");

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "TorrentUser") }, "Cookie");
        var user = new ClaimsPrincipal(identity);
        this.hubCallerContext.User.Returns(user);

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "torrentConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [TestCase("?access_token=secret-torrent-key")]
    [TestCase("?apikey=secret-torrent-key")]
    [TestCase("?api_key=secret-torrent-key")]
    [TestCase("?token=secret-torrent-key")]
    public async Task TorrentHub_OnConnectedAsync_WhenAuthEnabledAndQueryApiKey_AcceptsConnection(string queryString)
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-torrent-key");
        this.httpContext.Request.QueryString = new QueryString(queryString);

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "torrentConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [TestCase("X-Api-Key", "secret-torrent-key")]
    [TestCase("ApiKey", "secret-torrent-key")]
    public async Task TorrentHub_OnConnectedAsync_WhenAuthEnabledAndHeaderApiKey_AcceptsConnection(string headerName, string headerValue)
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-torrent-key");
        this.httpContext.Request.Headers[headerName] = headerValue;

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "torrentConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_OnConnectedAsync_WhenAuthEnabledAndBearerTokenHeader_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-torrent-key");
        this.httpContext.Request.Headers["Authorization"] = "Bearer secret-torrent-key";

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "torrentConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_OnConnectedAsync_WhenAuthEnabledAndBasicAuthPasswordMatches_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-torrent-key");
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:secret-torrent-key"));
        this.httpContext.Request.Headers["Authorization"] = $"Basic {credentials}";

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "torrentConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_OnConnectedAsync_WhenAuthEnabledAndBasicAuthUsernameMatches_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-torrent-key");
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("secret-torrent-key:anypassword"));
        this.httpContext.Request.Headers["Authorization"] = $"Basic {credentials}";

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "torrentConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_OnConnectedAsync_WhenAuthEnabledAndUnauthenticated_AbortsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-torrent-key");
        this.httpContext.Request.QueryString = new QueryString("?access_token=invalid-key");

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeFalse();
        this.hubCallerContext.Received(1).Abort();
        await this.callerProxy.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_OnConnectedAsync_WhenAuthEnabledAndWrongBearerToken_AbortsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-torrent-key");
        this.httpContext.Request.Headers["Authorization"] = "Bearer wrong-token";

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeFalse();
        this.hubCallerContext.Received(1).Abort();
        await this.callerProxy.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_OnConnectedAsync_WhenMalformedBasicAuth_AbortsGracefully()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-torrent-key");
        this.httpContext.Request.Headers["Authorization"] = "Basic not-valid-base64!@#$";

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        TorrentHub.IsConnected.Should().BeFalse();
        this.hubCallerContext.Received(1).Abort();
    }

    [Test]
    public async Task TorrentHub_OnDisconnectedAsync_RemovesConnectionFromTracking()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();
        TorrentHub.ActiveConnectionCount.Should().Be(1);

        await hub.OnDisconnectedAsync(null);
        TorrentHub.ActiveConnectionCount.Should().Be(0);
        TorrentHub.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task TorrentHub_SubscribeAndUnsubscribeToTorrent_ManagesGroupMembership()
    {
        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.SubscribeToTorrent(42);
        await this.groupManager.Received(1).AddToGroupAsync("conn-torrent-123", "torrent-42", Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromTorrent(42);
        await this.groupManager.Received(1).RemoveFromGroupAsync("conn-torrent-123", "torrent-42", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_SubscribeAndUnsubscribeToAllTorrents_ManagesTorrentsGroupMembership()
    {
        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.SubscribeToAllTorrents();
        await this.groupManager.Received(1).AddToGroupAsync("conn-torrent-123", "torrents", Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromAllTorrents();
        await this.groupManager.Received(1).RemoveFromGroupAsync("conn-torrent-123", "torrents", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_BroadcastTorrentProgress_SendsProgressToSpecificTorrentGroup()
    {
        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        var progressData = new { Progress = 85.5, DownloadSpeed = 1048576L, UploadSpeed = 524288L };
        await hub.BroadcastTorrentProgress(99, progressData);

        this.clients.Received(1).Group("torrent-99");
        await this.groupProxy.Received(1).SendCoreAsync(
            "torrentProgress",
            Arg.Is<object[]>(args => args.Length == 1 && args[0] == progressData),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_BroadcastGlobalProgress_SendsProgressToAllTorrentsGroup()
    {
        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        var globalProgress = new { TotalDownloadSpeed = 5000000L, ActiveCount = 3 };
        await hub.BroadcastGlobalProgress(globalProgress);

        this.clients.Received(1).Group("torrents");
        await this.groupProxy.Received(1).SendCoreAsync(
            "torrentProgress",
            Arg.Is<object[]>(args => args.Length == 1 && args[0] == globalProgress),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_PushSwarmTelemetry_SendsTelemetryToTorrentGroup()
    {
        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        var swarmData = new { ConnectedPeers = 25, Seeders = 10, Leechers = 15 };
        await hub.PushSwarmTelemetry(7, swarmData);

        this.clients.Received(1).Group("torrent-7");
        await this.groupProxy.Received(1).SendCoreAsync(
            "swarmTelemetry",
            Arg.Is<object[]>(args => args.Length == 1 && args[0] == swarmData),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TorrentHub_BroadcastSwarmTelemetry_SendsTelemetryToAllClients()
    {
        var hub = new TorrentHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        var swarmOverview = new { TotalPeers = 150, GlobalSwarmHealth = "Optimal" };
        await hub.BroadcastSwarmTelemetry(swarmOverview);

        await this.allProxy.Received(1).SendCoreAsync(
            "swarmTelemetry",
            Arg.Is<object[]>(args => args.Length == 1 && args[0] == swarmOverview),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void TorrentHub_TestingHelpers_ManageConnectionsCorrectly()
    {
        TorrentHub.IsConnected.Should().BeFalse();
        TorrentHub.ActiveConnectionCount.Should().Be(0);

        TorrentHub.AddConnectionForTesting("test-conn-1");
        TorrentHub.IsConnected.Should().BeTrue();
        TorrentHub.ActiveConnectionCount.Should().Be(1);

        TorrentHub.AddConnectionForTesting("test-conn-2");
        TorrentHub.ActiveConnectionCount.Should().Be(2);

        TorrentHub.RemoveConnectionForTesting("test-conn-1");
        TorrentHub.ActiveConnectionCount.Should().Be(1);

        TorrentHub.ResetForTesting();
        TorrentHub.IsConnected.Should().BeFalse();
        TorrentHub.ActiveConnectionCount.Should().Be(0);
    }

    [Test]
    public async Task NotificationHub_OnConnectedAsync_WhenAuthDisabled_AcceptsConnectionAndSendsNotificationConnected()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        NotificationHub.IsConnected.Should().BeTrue();
        NotificationHub.ActiveConnectionCount.Should().Be(1);
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "notificationConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotificationHub_OnConnectedAsync_WhenAuthEnabledAndUserAuthenticated_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-notif-key");

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NotifAdmin") }, "Cookie");
        var user = new ClaimsPrincipal(identity);
        this.hubCallerContext.User.Returns(user);

        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        NotificationHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "notificationConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [TestCase("?access_token=secret-notif-key")]
    [TestCase("?apikey=secret-notif-key")]
    [TestCase("?api_key=secret-notif-key")]
    [TestCase("?token=secret-notif-key")]
    public async Task NotificationHub_OnConnectedAsync_WhenAuthEnabledAndQueryApiKey_AcceptsConnection(string queryString)
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-notif-key");
        this.httpContext.Request.QueryString = new QueryString(queryString);

        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        NotificationHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "notificationConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [TestCase("X-Api-Key", "secret-notif-key")]
    [TestCase("ApiKey", "secret-notif-key")]
    public async Task NotificationHub_OnConnectedAsync_WhenAuthEnabledAndHeaderApiKey_AcceptsConnection(string headerName, string headerValue)
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-notif-key");
        this.httpContext.Request.Headers[headerName] = headerValue;

        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        NotificationHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "notificationConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotificationHub_OnConnectedAsync_WhenAuthEnabledAndBearerTokenHeader_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-notif-key");
        this.httpContext.Request.Headers["Authorization"] = "Bearer secret-notif-key";

        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        NotificationHub.IsConnected.Should().BeTrue();
        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "notificationConnected",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotificationHub_OnConnectedAsync_WhenAuthEnabledAndUnauthenticated_AbortsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-notif-key");
        this.httpContext.Request.QueryString = new QueryString("?access_token=wrong-key");

        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        NotificationHub.IsConnected.Should().BeFalse();
        this.hubCallerContext.Received(1).Abort();
        await this.callerProxy.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotificationHub_OnConnectedAsync_WhenAuthEnabledAndWrongBearerToken_AbortsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-notif-key");
        this.httpContext.Request.Headers["Authorization"] = "Bearer wrong-key";

        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        NotificationHub.IsConnected.Should().BeFalse();
        this.hubCallerContext.Received(1).Abort();
        await this.callerProxy.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotificationHub_OnConnectedAsync_WhenMalformedBasicAuth_AbortsGracefully()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-notif-key");
        this.httpContext.Request.Headers["Authorization"] = "Basic @@@invalid@@@";

        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();

        NotificationHub.IsConnected.Should().BeFalse();
        this.hubCallerContext.Received(1).Abort();
    }

    [Test]
    public async Task NotificationHub_OnDisconnectedAsync_RemovesConnectionFromTracking()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.OnConnectedAsync();
        NotificationHub.ActiveConnectionCount.Should().Be(1);

        await hub.OnDisconnectedAsync(null);
        NotificationHub.ActiveConnectionCount.Should().Be(0);
        NotificationHub.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task NotificationHub_SubscribeAndUnsubscribeToTopic_ManagesGroupMembership()
    {
        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.SubscribeToTopic("Downloads");
        await this.groupManager.Received(1).AddToGroupAsync("conn-torrent-123", "topic-downloads", Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromTopic("Downloads");
        await this.groupManager.Received(1).RemoveFromGroupAsync("conn-torrent-123", "topic-downloads", Arg.Any<CancellationToken>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task NotificationHub_SubscribeAndUnsubscribeToTopic_WhenInvalidTopic_DoesNothing(string invalidTopic)
    {
        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.SubscribeToTopic(invalidTopic);
        await this.groupManager.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromTopic(invalidTopic);
        await this.groupManager.DidNotReceive().RemoveFromGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotificationHub_SubscribeAndUnsubscribeToLevel_ManagesGroupMembership()
    {
        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.SubscribeToLevel("Warning");
        await this.groupManager.Received(1).AddToGroupAsync("conn-torrent-123", "level-warning", Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromLevel("Warning");
        await this.groupManager.Received(1).RemoveFromGroupAsync("conn-torrent-123", "level-warning", Arg.Any<CancellationToken>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task NotificationHub_SubscribeAndUnsubscribeToLevel_WhenInvalidLevel_DoesNothing(string invalidLevel)
    {
        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.SubscribeToLevel(invalidLevel);
        await this.groupManager.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromLevel(invalidLevel);
        await this.groupManager.DidNotReceive().RemoveFromGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotificationHub_DispatchNotification_BroadcastsToAllClients()
    {
        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        var notification = new { Title = "Download Finished", Body = "Ubuntu.iso completed." };
        await hub.DispatchNotification(notification);

        await this.allProxy.Received(1).SendCoreAsync(
            "notificationDispatched",
            Arg.Is<object[]>(args => args.Length == 1 && args[0] == notification),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotificationHub_DispatchNotificationToTopic_SendsToSpecificTopicGroup()
    {
        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        var notification = new { Title = "Security Alert", Body = "Failed login detected." };
        await hub.DispatchNotificationToTopic("Security", notification);

        this.clients.Received(1).Group("topic-security");
        await this.groupProxy.Received(1).SendCoreAsync(
            "notificationDispatched",
            Arg.Is<object[]>(args => args.Length == 1 && args[0] == notification),
            Arg.Any<CancellationToken>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task NotificationHub_DispatchNotificationToTopic_WhenInvalidTopic_DoesNothing(string invalidTopic)
    {
        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.DispatchNotificationToTopic(invalidTopic, new { Title = "Ignored" });

        this.clients.DidNotReceive().Group(Arg.Any<string>());
        await this.groupProxy.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NotificationHub_DispatchSystemAlert_BroadcastsFormattedAlertToAllClients()
    {
        var hub = new NotificationHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
            Groups = this.groupManager,
        };

        await hub.DispatchSystemAlert("DiskSpaceLow", "Available storage is below 5GB.", "Warning");

        await this.allProxy.Received(1).SendCoreAsync(
            "systemAlert",
            Arg.Is<object[]>(args => args.Length == 1),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void NotificationHub_TestingHelpers_ManageConnectionsCorrectly()
    {
        NotificationHub.IsConnected.Should().BeFalse();
        NotificationHub.ActiveConnectionCount.Should().Be(0);

        NotificationHub.AddConnectionForTesting("notif-1");
        NotificationHub.IsConnected.Should().BeTrue();
        NotificationHub.ActiveConnectionCount.Should().Be(1);

        NotificationHub.AddConnectionForTesting("notif-2");
        NotificationHub.ActiveConnectionCount.Should().Be(2);

        NotificationHub.RemoveConnectionForTesting("notif-1");
        NotificationHub.ActiveConnectionCount.Should().Be(1);

        NotificationHub.ResetForTesting();
        NotificationHub.IsConnected.Should().BeFalse();
        NotificationHub.ActiveConnectionCount.Should().Be(0);
    }
}

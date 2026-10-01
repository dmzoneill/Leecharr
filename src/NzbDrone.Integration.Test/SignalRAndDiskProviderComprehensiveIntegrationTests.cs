// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using NLog;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SignalRAndDiskProviderComprehensiveIntegrationTests : IntegrationTestBase
{
    private string tempTestDirectory;

    [SetUp]
    public void SetUp()
    {
        this.tempTestDirectory = Path.Combine(Path.GetTempPath(), "LeecharrDiskProviderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempTestDirectory);
        MessageHub.ResetForTesting();
    }

    [TearDown]
    public void TearDown()
    {
        MessageHub.ResetForTesting();
        try
        {
            if (Directory.Exists(this.tempTestDirectory))
            {
                Directory.Delete(this.tempTestDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in temporary test directory
        }
    }

    [Test]
    public async Task MessageHub_ConnectionLifecycleAndAuthMatrix_AcceptsAndRejectsCorrectly()
    {
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.AuthenticationEnabled.Returns(true);
        configFileProvider.ApiKey.Returns("hub_secret_key");

        var hubCallerContext = Substitute.For<HubCallerContext>();
        var clients = Substitute.For<IHubCallerClients>();
        var callerProxy = Substitute.For<ISingleClientProxy>();
        clients.Caller.Returns(callerProxy);
        hubCallerContext.ConnectionId.Returns("test_conn_1");

        var httpContext = new DefaultHttpContext();
        var httpContextFeature = Substitute.For<IHttpContextFeature>();
        httpContextFeature.HttpContext.Returns(httpContext);

        var featureCollection = new FeatureCollection();
        featureCollection.Set<IHttpContextFeature>(httpContextFeature);
        hubCallerContext.Features.Returns(featureCollection);

        var hub = new MessageHub(configFileProvider)
        {
            Context = hubCallerContext,
            Clients = clients,
        };

        // 1. Unauthenticated connection is aborted
        await hub.OnConnectedAsync();
        hubCallerContext.Received(1).Abort();
        MessageHub.IsConnected.Should().BeFalse();

        // 2. Authentication with X-Api-Key succeeds and sends version
        httpContext.Request.Headers["X-Api-Key"] = "hub_secret_key";
        hubCallerContext.ClearReceivedCalls();

        await hub.OnConnectedAsync();
        hubCallerContext.DidNotReceive().Abort();
        MessageHub.IsConnected.Should().BeTrue();

        await callerProxy.Received().SendCoreAsync(
            "receiveMessage",
            Arg.Is<object[]>(args => args.Length == 1 && ((SignalRMessage)args[0]).Name == "version"),
            Arg.Any<CancellationToken>());

        // 3. Disconnect removes connection
        await hub.OnDisconnectedAsync(null);
        MessageHub.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task MessageHub_QueryParametersAndBearerAuth_AuthenticatesSuccessfully()
    {
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.AuthenticationEnabled.Returns(true);
        configFileProvider.ApiKey.Returns("hub_secret_key");

        var hubCallerContext = Substitute.For<HubCallerContext>();
        var clients = Substitute.For<IHubCallerClients>();
        var callerProxy = Substitute.For<ISingleClientProxy>();
        clients.Caller.Returns(callerProxy);
        hubCallerContext.ConnectionId.Returns("test_conn_2");

        var authQueryParams = new[] { "apikey", "access_token", "api_key", "token" };
        foreach (var qp in authQueryParams)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString($"?{qp}=hub_secret_key");

            var httpContextFeature = Substitute.For<IHttpContextFeature>();
            httpContextFeature.HttpContext.Returns(httpContext);

            var featureCollection = new FeatureCollection();
            featureCollection.Set<IHttpContextFeature>(httpContextFeature);
            hubCallerContext.Features.Returns(featureCollection);

            var hub = new MessageHub(configFileProvider)
            {
                Context = hubCallerContext,
                Clients = clients,
            };

            await hub.OnConnectedAsync();
            hubCallerContext.DidNotReceive().Abort();
            await hub.OnDisconnectedAsync(null);
        }

        // Bearer header authentication
        var bearerContext = new DefaultHttpContext();
        bearerContext.Request.Headers["Authorization"] = "Bearer hub_secret_key";
        var bearerFeature = Substitute.For<IHttpContextFeature>();
        bearerFeature.HttpContext.Returns(bearerContext);
        var bearerFeatures = new FeatureCollection();
        bearerFeatures.Set<IHttpContextFeature>(bearerFeature);
        hubCallerContext.Features.Returns(bearerFeatures);

        var bearerHub = new MessageHub(configFileProvider)
        {
            Context = hubCallerContext,
            Clients = clients,
        };

        await bearerHub.OnConnectedAsync();
        hubCallerContext.DidNotReceive().Abort();
        await bearerHub.OnDisconnectedAsync(null);
    }

    [Test]
    public async Task MessageHub_RequestStateSnapshotAndSubscriptions_AggregatesAndDispatches()
    {
        var torrentService = Substitute.For<ITorrentService>();
        var torrents = new List<Torrent>
        {
            new Torrent
            {
                Id = 1,
                Name = "Linux Distro ISO",
                Status = TorrentStatus.Downloading,
                Progress = 0.65,
                DownloadSpeed = 2_000_000,
                UploadSpeed = 500_000,
                TotalSize = 4_000_000_000,
                Eta = 120,
            },
            new Torrent
            {
                Id = 2,
                Name = "Open Source Dataset",
                Status = TorrentStatus.Seeding,
                Progress = 1.0,
                DownloadSpeed = 0,
                UploadSpeed = 1_500_000,
                TotalSize = 8_000_000_000,
                Eta = 0,
            },
            new Torrent
            {
                Id = 3,
                Name = "Paused Torrent",
                Status = TorrentStatus.Paused,
                Progress = 0.20,
                DownloadSpeed = 0,
                UploadSpeed = 0,
                TotalSize = 1_000_000_000,
                Eta = 0,
            },
        };

        torrentService.GetAll().Returns(torrents);

        var hubCallerContext = Substitute.For<HubCallerContext>();
        hubCallerContext.ConnectionId.Returns("conn-sub-test");

        var clients = Substitute.For<IHubCallerClients>();
        var callerProxy = Substitute.For<ISingleClientProxy>();
        var allProxy = Substitute.For<IClientProxy>();
        clients.Caller.Returns(callerProxy);
        clients.All.Returns(allProxy);

        var groups = Substitute.For<IGroupManager>();

        var hub = new MessageHub(configFileProvider: null, torrentService: torrentService)
        {
            Context = hubCallerContext,
            Clients = clients,
            Groups = groups,
        };

        // 1. RequestStateSnapshot
        var snapshot = await hub.RequestStateSnapshot();
        snapshot.Should().NotBeNull();
        snapshot.TotalCount.Should().Be(3);
        snapshot.ActiveCount.Should().Be(2);
        snapshot.DownloadSpeed.Should().Be(2_000_000);
        snapshot.UploadSpeed.Should().Be(2_000_000);
        snapshot.Torrents.Should().HaveCount(3);

        await callerProxy.Received(1).SendCoreAsync(
            "stateSnapshot",
            Arg.Is<object[]>(args => args.Length == 1 && ((StateSnapshotResource)args[0]).TotalCount == 3),
            Arg.Any<CancellationToken>());

        // 2. Torrent and Channel subscriptions
        await hub.SubscribeToTorrent(1);
        await groups.Received(1).AddToGroupAsync("conn-sub-test", "torrent-1", Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromTorrent(1);
        await groups.Received(1).RemoveFromGroupAsync("conn-sub-test", "torrent-1", Arg.Any<CancellationToken>());

        await hub.SubscribeToChannel("general");
        await groups.Received(1).AddToGroupAsync("conn-sub-test", "channel-general", Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromChannel("general");
        await groups.Received(1).RemoveFromGroupAsync("conn-sub-test", "channel-general", Arg.Any<CancellationToken>());

        // Blank channel names are safely ignored
        await hub.SubscribeToChannel("   ");
        await hub.UnsubscribeFromChannel("   ");

        // 3. Tracker updates
        await hub.TrackerUpdated(new { tracker = "udp://tracker.open.org" });
        await allProxy.Received(1).SendCoreAsync("trackerUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());

        await hub.TrackerAnnounced(new { announced = true });
        await allProxy.Received(1).SendCoreAsync("trackerAnnounced", Arg.Any<object[]>(), Arg.Any<CancellationToken>());

        // 4. Testing helpers
        MessageHub.AddConnectionForTesting("extra-conn");
        MessageHub.IsConnected.Should().BeTrue();
        MessageHub.RemoveConnectionForTesting("extra-conn");
        MessageHub.DisconnectAllConnections();
        MessageHub.IsConnected.Should().BeFalse();
    }

    [Test]
    public void SignalRMessageBroadcaster_BroadcastTelemetryAndGuaranteed_ChannelsRouteAppropriately()
    {
        var hubContext = Substitute.For<IHubContext<MessageHub>>();
        var hubClients = Substitute.For<IHubClients>();
        var allProxy = Substitute.For<IClientProxy>();
        var groupProxy = Substitute.For<IClientProxy>();

        hubContext.Clients.Returns(hubClients);
        hubClients.All.Returns(allProxy);
        hubClients.Group(Arg.Any<string>()).Returns(groupProxy);

        MessageHub.AddConnectionForTesting("broadcaster-conn");

        using var broadcaster = new SignalRMessageBroadcaster(hubContext, telemetryCapacity: 100, sendTimeout: TimeSpan.FromSeconds(2));
        broadcaster.IsConnected.Should().BeTrue();

        // 1. Telemetry message (speedPulse) routes to telemetry channel
        var telemetryMsg = new SignalRMessage
        {
            Name = "speedPulse",
            Body = new { downloadRate = 5000, uploadRate = 1000 },
        };
        broadcaster.BroadcastMessage(telemetryMsg);

        // 2. Guaranteed message (queueUpdated) routes to guaranteed channel
        var guaranteedMsg = new SignalRMessage
        {
            Name = "queueUpdated",
            Body = new { count = 10 },
        };
        broadcaster.BroadcastMessage(guaranteedMsg);

        // 3. Group and Channel broadcasting
        broadcaster.BroadcastToGroup("custom-group", guaranteedMsg);
        broadcaster.BroadcastToTorrent(42, guaranteedMsg);
        broadcaster.BroadcastToChannel("system", guaranteedMsg);

        // Null checks and whitespace checks
        broadcaster.BroadcastMessage(null);
        broadcaster.BroadcastToGroup(string.Empty, guaranteedMsg);
        broadcaster.BroadcastToChannel(null, guaranteedMsg);
    }

    [Test]
    public void PieceMapSignalREventHandler_AggregateAndFlush_DispatchesGroupedPieces()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        broadcaster.IsConnected.Returns(true);

        using var handler = new PieceMapSignalREventHandler(broadcaster, flushIntervalMs: 50);

        // 1. Handle PieceVerifiedEvent for torrent 1 and 2
        handler.Handle(new PieceVerifiedEvent(torrentId: 1, pieceIndex: 0));
        handler.Handle(new PieceVerifiedEvent(torrentId: 1, pieceIndex: 1));
        handler.Handle(new PieceVerifiedEvent(torrentId: 1, pieceIndex: 2));
        handler.Handle(new PieceVerifiedEvent(torrentId: 2, pieceIndex: 10));

        // 2. Flush
        handler.Flush();

        broadcaster.Received().BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "pieceMapUpdated"));

        // Second flush when empty does not broadcast
        broadcaster.ClearReceivedCalls();
        handler.Flush();
        broadcaster.DidNotReceiveWithAnyArgs().BroadcastToTorrent(default, default);
    }

    [Test]
    public void DiskProvider_FileAndFolderOperations_PerformsFilesystemMutations()
    {
        var diskProvider = new DiskProvider();

        // 1. Folder operations
        var subDir = Path.Combine(this.tempTestDirectory, "sub_folder");
        diskProvider.FolderExists(subDir).Should().BeFalse();

        diskProvider.CreateFolder(subDir);
        diskProvider.FolderExists(subDir).Should().BeTrue();

        var nestedDir = Path.Combine(subDir, "nested");
        diskProvider.CreateFolder(nestedDir);
        var subFolders = diskProvider.GetDirectories(subDir);
        subFolders.Should().Contain(nestedDir);

        // 2. File write, read, and size
        var filePath = Path.Combine(subDir, "test_file.txt");
        diskProvider.FileExists(filePath).Should().BeFalse();

        var testText = "Hello Leecharr Disk Provider Comprehensive Integration Testing!\nLine 2";
        diskProvider.WriteAllText(filePath, testText);
        diskProvider.FileExists(filePath).Should().BeTrue();

        var readText = diskProvider.ReadAllText(filePath);
        readText.Should().Be(testText);

        var size = diskProvider.GetFileSize(filePath);
        size.Should().Be(Encoding.UTF8.GetByteCount(testText));

        var files = diskProvider.GetFiles(subDir, recursive: false);
        files.Should().Contain(filePath);

        // 3. Timestamps and metadata
        var lastWrite = diskProvider.FileGetLastWrite(filePath);
        lastWrite.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(30));

        diskProvider.FolderWritable(subDir).Should().BeTrue();
        diskProvider.FolderEmpty(subDir).Should().BeFalse();
        diskProvider.GetFolderSize(subDir).Should().BeGreaterThan(0);

        // 4. Streams
        var streamFilePath = Path.Combine(subDir, "stream_file.bin");
        using (var writeStream = diskProvider.OpenWriteStream(streamFilePath))
        {
            var data = new byte[] { 1, 2, 3, 4, 5, 42, 99 };
            writeStream.Write(data);
        }

        using (var readStream = diskProvider.OpenReadStream(streamFilePath))
        {
            var readBytes = new byte[7];
            var count = readStream.Read(readBytes);
            count.Should().Be(7);
            readBytes.Should().Equal(new byte[] { 1, 2, 3, 4, 5, 42, 99 });
        }

        // 5. Copy and Move
        var copyPath = Path.Combine(subDir, "copied_file.txt");
        diskProvider.CopyFile(filePath, copyPath, overwrite: true);
        diskProvider.FileExists(copyPath).Should().BeTrue();

        var movePath = Path.Combine(subDir, "moved_file.txt");
        diskProvider.MoveFile(copyPath, movePath);
        diskProvider.FileExists(copyPath).Should().BeFalse();
        diskProvider.FileExists(movePath).Should().BeTrue();

        // 6. Delete file and folder
        diskProvider.DeleteFile(movePath);
        diskProvider.FileExists(movePath).Should().BeFalse();

        diskProvider.DeleteFolder(nestedDir, recursive: true);
        diskProvider.FolderExists(nestedDir).Should().BeFalse();
    }

    [Test]
    public void DiskProvider_SpaceAndDriveMetrics_ResolvesAvailableDriveMetrics()
    {
        var diskProvider = new DiskProvider();

        // 1. Available space and total size on valid temporary path
        var availableSpace = diskProvider.GetAvailableSpace(this.tempTestDirectory);
        availableSpace.Should().NotBeNull();
        availableSpace.Value.Should().BeGreaterThan(0);

        var totalSize = diskProvider.GetTotalSize(this.tempTestDirectory);
        totalSize.Should().NotBeNull();
        totalSize.Value.Should().BeGreaterThan(0);

        // 2. Best matching drive
        var drive = diskProvider.GetDrive(this.tempTestDirectory);
        drive.Should().NotBeNull();
        drive.IsReady.Should().BeTrue();

        // 3. Null and whitespace handling
        diskProvider.GetAvailableSpace(null).Should().BeNull();
        diskProvider.GetAvailableSpace("   ").Should().BeNull();
        diskProvider.GetTotalSize(null).Should().BeNull();
        diskProvider.GetTotalSize("   ").Should().BeNull();
    }

    [Test]
    public void EnvironmentInfo_OsAndRuntimeAndAppFolder_ProvidesSystemDetails()
    {
        // 1. OsInfo properties
        OsInfo.Os.Should().NotBeNullOrWhiteSpace();
        OsInfo.Version.Should().NotBeNullOrWhiteSpace();
        (OsInfo.IsLinux || OsInfo.IsWindows || OsInfo.IsOsx).Should().BeTrue();

        // 2. RuntimeInfo
        var runtimeInfo = new RuntimeInfo();
        runtimeInfo.IsWindowsService.Should().BeFalse();
        runtimeInfo.RestartPending = true;
        runtimeInfo.RestartPending.Should().BeTrue();
        runtimeInfo.RestartPending = false;
        runtimeInfo.RestartPending.Should().BeFalse();

        // 3. AppFolderInfo with StartupContext
        var customPath = Path.Combine(this.tempTestDirectory, "CustomAppData");
        var startupContext = new StartupContext("-data=" + customPath);

        var appFolderInfo = new AppFolderInfo(startupContext);
        appFolderInfo.AppDataFolder.Should().Be(customPath);
    }
}

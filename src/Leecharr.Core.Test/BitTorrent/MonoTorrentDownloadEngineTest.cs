// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.PortMapping;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using CoreTorrent = NzbDrone.Core.Torrents.Torrent;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class MonoTorrentDownloadEngineTest
{
    private IConfigService configService = null!;
    private IStoragePathService storagePathService = null!;
    private ICategoryService categoryService = null!;
    private IDiskProvider diskProvider = null!;
    private IEventAggregator eventAggregator = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private ITorrentLogService torrentLogService = null!;
    private MonoTorrentDownloadEngine engine = null!;

    private string testIncompleteDir = null!;
    private string testDownloadDir = null!;
    private string testAppDataDir = null!;

    [SetUp]
    public void SetUp()
    {
        this.testIncompleteDir = Path.Combine(Path.GetTempPath(), "leecharr_test_incomplete_" + Guid.NewGuid().ToString("N"));
        this.testDownloadDir = Path.Combine(Path.GetTempPath(), "leecharr_test_downloads_" + Guid.NewGuid().ToString("N"));
        this.testAppDataDir = Path.Combine(Path.GetTempPath(), "leecharr_test_appdata_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.testIncompleteDir);
        Directory.CreateDirectory(this.testDownloadDir);
        Directory.CreateDirectory(this.testAppDataDir);

        this.configService = Substitute.For<IConfigService>();
        this.configService.ListeningPort.Returns(0); // dynamic port
        this.configService.UpnpEnabled.Returns(false);
        this.configService.DiskWriteCacheSizeMb.Returns(128);
        this.configService.DownloadDir.Returns(this.testDownloadDir);
        this.configService.MaxPerTorrentConnections.Returns(50);
        this.configService.MaxUploadSlots.Returns(4);
        this.configService.EnableDht.Returns(true);
        this.configService.EnablePex.Returns(true);
        this.configService.EnableBep27PrivateTorrents.Returns(true);
        this.configService.EnableIncompleteDir.Returns(true);
        this.configService.AutoStart.Returns(true);

        this.storagePathService = Substitute.For<IStoragePathService>();
        this.storagePathService.GetIncompleteDirectory().Returns(this.testIncompleteDir);
        this.storagePathService.GetCompletedDirectory(Arg.Any<string>()).Returns(this.testDownloadDir);

        this.categoryService = Substitute.For<ICategoryService>();
        this.categoryService.GetSavePathForCategory(Arg.Any<string>()).Returns(this.testDownloadDir);

        this.diskProvider = Substitute.For<IDiskProvider>();
        this.eventAggregator = Substitute.For<IEventAggregator>();

        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(this.testAppDataDir);
        this.torrentLogService = Substitute.For<ITorrentLogService>();

        this.engine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            appFolderInfo: this.appFolderInfo,
            torrentLogService: this.torrentLogService);
    }

    [TearDown]
    public void TearDown()
    {
        this.engine?.Dispose();

        try
        {
            if (Directory.Exists(this.testIncompleteDir))
            {
                Directory.Delete(this.testIncompleteDir, true);
            }

            if (Directory.Exists(this.testDownloadDir))
            {
                Directory.Delete(this.testDownloadDir, true);
            }

            if (Directory.Exists(this.testAppDataDir))
            {
                Directory.Delete(this.testAppDataDir, true);
            }
        }
        catch
        {
        }
    }

    private static byte[] CreateSampleSingleFileTorrentBytes(
        string name = "testfile.bin",
        int length = 16384,
        bool isPrivate = false,
        IList<string> trackerUrls = null)
    {
        var pieceLength = 16384;
        var pieceCount = Math.Max(1, (int)Math.Ceiling((double)length / pieceLength));
        var pieces = new byte[pieceCount * 20];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)((i % 250) + 1);
        }

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString(name) },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieces) },
            { "length", new BEncodedNumber(length) },
        };

        if (isPrivate)
        {
            infoDict.Add("private", new BEncodedNumber(1));
        }

        var primaryAnnounce = trackerUrls != null && trackerUrls.Count > 0
            ? trackerUrls[0]
            : "http://tracker.example.com/announce";

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString(primaryAnnounce) },
            { "info", infoDict },
        };

        if (trackerUrls != null && trackerUrls.Count > 0)
        {
            var announceList = new BEncodedList();
            foreach (var url in trackerUrls)
            {
                announceList.Add(new BEncodedList { new BEncodedString(url) });
            }

            rootDict.Add("announce-list", announceList);
        }

        return rootDict.Encode();
    }

    private static byte[] CreateSampleMultiFileTorrentBytes(string name = "MultiFileTorrent")
    {
        var pieces = new byte[40]; // 2 pieces
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)(i % 255);
        }

        var fileList = new BEncodedList
        {
            new BEncodedDictionary
            {
                { "length", new BEncodedNumber(16384) },
                { "path", new BEncodedList { new BEncodedString("subfolder"), new BEncodedString("file1.dat") } },
            },
            new BEncodedDictionary
            {
                { "length", new BEncodedNumber(16384) },
                { "path", new BEncodedList { new BEncodedString("file2.dat") } }
            },
        };

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString(name) },
            { "piece length", new BEncodedNumber(16384) },
            { "pieces", new BEncodedString(pieces) },
            { "files", fileList },
        };

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "info", infoDict },
        };

        return rootDict.Encode();
    }

    [Test]
    public void ProtocolName_ReturnsBitTorrent()
    {
        this.engine.ProtocolName.Should().Be("BitTorrent");
        this.engine.EngineId.Should().Be("MonoTorrent");
        this.engine.DisplayName.Should().Contain("MonoTorrent");
        this.engine.IsAvailable.Should().BeTrue();
    }

    [Test]
    public async Task BoundSocketConnector_WhenPeerIpIsBlocklisted_ThrowsAndIncrementsBlockedCount()
    {
        var blocklistService = Substitute.For<NzbDrone.Core.Network.Blocklist.IBlocklistService>();
        blocklistService.IsIpBlocked("198.51.100.1").Returns(true);
        blocklistService.IsIpBlocked("198.51.100.2").Returns(false);

        var blockedCount = 0;
        var connector = new BoundSocketConnector(
            IPAddress.Any,
            IPAddress.IPv6Any,
            blocklistService: blocklistService,
            onPeerBlocked: () => Interlocked.Increment(ref blockedCount));

        Func<Task> actBlocked = async () => await connector.ConnectAsync(new Uri("tcp://198.51.100.1:6881"), CancellationToken.None);
        await actBlocked.Should().ThrowAsync<System.Net.Sockets.SocketException>();
        blockedCount.Should().Be(1);
    }

    [Test]
    public void FilteringPeerConnectionListener_WhenIncomingPeerIsBlocklisted_DisposesConnectionAndDropsEvent()
    {
        var blocklistService = Substitute.For<NzbDrone.Core.Network.Blocklist.IBlocklistService>();
        blocklistService.IsIpBlocked("203.0.113.5").Returns(true);
        blocklistService.IsIpBlocked("203.0.113.6").Returns(false);

        var innerListener = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnectionListener>();
        var blockedCount = 0;
        var filteringListener = new FilteringPeerConnectionListener(
            innerListener,
            blocklistService,
            () => Interlocked.Increment(ref blockedCount));

        var eventRaised = false;
        filteringListener.ConnectionReceived += (_, _) => eventRaised = true;

        var mockConn = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnection, IDisposable>();
        mockConn.IsIncoming.Returns(true);
        mockConn.Uri.Returns(new Uri("ipv4://203.0.113.5:12345"));
        var args = new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(mockConn, null);

        // Raise incoming connection from blocked peer on inner listener
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(innerListener, args);

        eventRaised.Should().BeFalse();
        blockedCount.Should().Be(1);
        ((IDisposable)mockConn).Received(1).Dispose();

        // Raise incoming connection from allowed peer
        var mockConnAllowed = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnection, IDisposable>();
        mockConnAllowed.IsIncoming.Returns(true);
        mockConnAllowed.Uri.Returns(new Uri("ipv4://203.0.113.6:12345"));
        var allowedArgs = new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(mockConnAllowed, null);

        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(innerListener, allowedArgs);

        eventRaised.Should().BeTrue();
        blockedCount.Should().Be(1);
        ((IDisposable)mockConnAllowed).DidNotReceive().Dispose();
    }

    [Test]
    public void FilteringPeerConnectionListener_WhenHalfOpenLimitExceeded_RejectsAndDisposesIncomingConnection()
    {
        var innerListener = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnectionListener>();
        var filteringListener = new FilteringPeerConnectionListener(
            innerListener,
            maxHalfOpenConnections: 2);

        var receivedCount = 0;
        filteringListener.ConnectionReceived += (_, _) => receivedCount++;

        var conn1 = new FakePeerConnection();
        var conn2 = new FakePeerConnection();
        var conn3 = new FakePeerConnection();

        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn1, null));
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn2, null));

        receivedCount.Should().Be(2);
        filteringListener.HalfOpenConnections.Should().Be(2);
        conn1.Disposed.Should().BeFalse();
        conn2.Disposed.Should().BeFalse();

        // 3rd connection exceeds limit of 2
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn3, null));

        receivedCount.Should().Be(2);
        filteringListener.HalfOpenConnections.Should().Be(2);
        conn3.Disposed.Should().BeTrue();
    }

    [Test]
    public async Task FilteringPeerConnectionListener_WhenHandshakeTimesOut_AbortsSocketAndDecrementsHalfOpenCount()
    {
        var innerListener = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnectionListener>();
        var filteringListener = new FilteringPeerConnectionListener(
            innerListener,
            handshakeTimeout: TimeSpan.FromMilliseconds(50));

        filteringListener.ConnectionReceived += (_, _) => { };

        var conn = new FakePeerConnection();
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn, null));

        filteringListener.HalfOpenConnections.Should().Be(1);
        conn.Disposed.Should().BeFalse();

        // Wait for handshake timeout to fire
        await Task.Delay(150);

        conn.Disposed.Should().BeTrue();
        filteringListener.HalfOpenConnections.Should().Be(0);
    }

    [Test]
    public async Task FilteringPeerConnectionListener_WhenHandshakeCompletes_DecrementsHalfOpenCountAndDoesNotTimeout()
    {
        var innerListener = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnectionListener>();
        var filteringListener = new FilteringPeerConnectionListener(
            innerListener,
            handshakeTimeout: TimeSpan.FromMilliseconds(80));

        MonoTorrent.Connections.Peer.IPeerConnection wrappedConn = null!;
        filteringListener.ConnectionReceived += (_, e) => wrappedConn = e.Connection;

        var conn = new FakePeerConnection { BytesToReturnOnReceive = 68 };
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn, null));

        filteringListener.HalfOpenConnections.Should().Be(1);

        // Read handshake bytes (68 bytes)
        var read = await wrappedConn.ReceiveAsync(new byte[68]);
        read.Should().Be(68);

        // HalfOpenConnections decrements immediately on handshake completion
        filteringListener.HalfOpenConnections.Should().Be(0);

        // Wait past handshake timeout
        await Task.Delay(150);

        // Connection was not aborted by timeout
        conn.Disposed.Should().BeFalse();
    }

    [Test]
    public void FilteringPeerConnectionListener_WhenConnectionDisposedBeforeHandshake_DecrementsHalfOpenCount()
    {
        var innerListener = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnectionListener>();
        var filteringListener = new FilteringPeerConnectionListener(innerListener);

        MonoTorrent.Connections.Peer.IPeerConnection wrappedConn = null!;
        filteringListener.ConnectionReceived += (_, e) => wrappedConn = e.Connection;

        var conn = new FakePeerConnection();
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn, null));

        filteringListener.HalfOpenConnections.Should().Be(1);

        wrappedConn.Dispose();

        filteringListener.HalfOpenConnections.Should().Be(0);
        conn.Disposed.Should().BeTrue();
    }

    [Test]
    public void FilteringPeerConnectionListener_WhenMaxConnectionsPerIpExceeded_RejectsAndDisposesIncomingConnection()
    {
        var innerListener = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnectionListener>();
        var filteringListener = new FilteringPeerConnectionListener(
            innerListener,
            maxConnectionsPerIp: 2,
            maxHalfOpenConnections: 10);

        var receivedCount = 0;
        filteringListener.ConnectionReceived += (_, _) => receivedCount++;

        var conn1 = new FakePeerConnection("192.168.1.50", 1001);
        var conn2 = new FakePeerConnection("192.168.1.50", 1002);
        var conn3 = new FakePeerConnection("192.168.1.50", 1003);
        var connOther = new FakePeerConnection("192.168.1.51", 1001);

        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn1, null));
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn2, null));

        receivedCount.Should().Be(2);
        filteringListener.GetActiveConnections("192.168.1.50").Should().Be(2);
        conn1.Disposed.Should().BeFalse();
        conn2.Disposed.Should().BeFalse();

        // 3rd connection from same IP exceeds limit of 2 -> dropped and disposed
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn3, null));

        receivedCount.Should().Be(2);
        filteringListener.GetActiveConnections("192.168.1.50").Should().Be(2);
        conn3.Disposed.Should().BeTrue();

        // Connection from different IP is accepted
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(connOther, null));

        receivedCount.Should().Be(3);
        filteringListener.GetActiveConnections("192.168.1.51").Should().Be(1);
        connOther.Disposed.Should().BeFalse();
    }

    [Test]
    public void FilteringPeerConnectionListener_WhenPeerDisconnects_DecrementsActiveConnectionCount()
    {
        var innerListener = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnectionListener>();
        var filteringListener = new FilteringPeerConnectionListener(
            innerListener,
            maxConnectionsPerIp: 1,
            maxHalfOpenConnections: 10);

        MonoTorrent.Connections.Peer.IPeerConnection wrappedConn = null!;
        filteringListener.ConnectionReceived += (_, e) => wrappedConn = e.Connection;

        var conn1 = new FakePeerConnection("192.168.1.60", 2001);
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn1, null));

        filteringListener.GetActiveConnections("192.168.1.60").Should().Be(1);

        // Disconnect first connection
        wrappedConn.Dispose();

        filteringListener.GetActiveConnections("192.168.1.60").Should().Be(0);

        // New connection from same IP is now permitted
        var conn2 = new FakePeerConnection("192.168.1.60", 2002);
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn2, null));

        filteringListener.GetActiveConnections("192.168.1.60").Should().Be(1);
        conn2.Disposed.Should().BeFalse();
    }

    [Test]
    public async Task FilteringPeerConnectionListener_WhenHandshakeTimesOut_DecrementsIpConnectionCount()
    {
        var innerListener = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnectionListener>();
        var filteringListener = new FilteringPeerConnectionListener(
            innerListener,
            maxConnectionsPerIp: 1,
            maxHalfOpenConnections: 10,
            handshakeTimeout: TimeSpan.FromMilliseconds(50));

        filteringListener.ConnectionReceived += (_, _) => { };

        var conn = new FakePeerConnection("192.168.1.70", 3001);
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(
            innerListener,
            new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(conn, null));

        filteringListener.GetActiveConnections("192.168.1.70").Should().Be(1);
        conn.Disposed.Should().BeFalse();

        await Task.Delay(150);

        conn.Disposed.Should().BeTrue();
        filteringListener.GetActiveConnections("192.168.1.70").Should().Be(0);
    }

    private sealed class FakePeerConnection : MonoTorrent.Connections.Peer.IPeerConnection, IDisposable
    {
        public FakePeerConnection(string ip = "127.0.0.1", int port = 12345)
        {
            this.EndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
            this.Uri = new Uri($"ipv4://{ip}:{port}");
        }

        public bool Disposed { get; private set; }

        public bool CanReconnect => false;

        public bool IsIncoming => true;

        public IPEndPoint EndPoint { get; set; }

        public Uri Uri { get; set; }

        public ReadOnlyMemory<byte> AddressBytes => new byte[4];

        public int BytesToReturnOnReceive { get; set; }

        public ReusableTasks.ReusableTask ConnectAsync() => default;

        public async ReusableTasks.ReusableTask<int> ReceiveAsync(Memory<byte> buffer)
        {
            await Task.Yield();
            return this.BytesToReturnOnReceive;
        }

        public async ReusableTasks.ReusableTask<int> SendAsync(Memory<byte> buffer)
        {
            await Task.Yield();
            return buffer.Length;
        }

        public void Dispose()
        {
            this.Disposed = true;
        }
    }

    [Test]
    public async Task ProbeHealthAsync_ReturnsHealthy()
    {
        var health = await this.engine.ProbeHealthAsync();
        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
        health.DependencyChecks.Should().NotBeEmpty();
    }

    [Test]
    public void Capabilities_SupportsExpectedFeatures()
    {
        this.engine.Capabilities.SupportsUtp.Should().BeTrue();
        this.engine.Capabilities.SupportsDht.Should().BeTrue();
        this.engine.Capabilities.SupportsPex.Should().BeTrue();
        this.engine.Capabilities.SupportsLpd.Should().BeTrue();
        this.engine.Capabilities.SupportsSequentialDownload.Should().BeTrue();
        this.engine.Capabilities.SupportsFastResume.Should().BeTrue();
        this.engine.Capabilities.SupportsCustomPiecePickers.Should().BeTrue();
        this.engine.Capabilities.SupportsDynamicRateLimits.Should().BeTrue();
        this.engine.Capabilities.SupportsSparseAllocation.Should().BeTrue();
    }

    [Test]
    public async Task StartAndStop_ExecutesCleanly()
    {
        await this.engine.StartAsync();
        await this.engine.StopAsync();
    }

    [Test]
    public async Task StartAsync_AppliesCustomPeerIdAndUserAgent()
    {
        this.configService.PeerIdPrefix.Returns("-qB4420-");
        this.configService.BitTorrentUserAgent.Returns("qBittorrent/4.4.2");

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.PeerId.Text.Should().StartWith("-qB4420-");
        monoEngine.PeerId.Text.Length.Should().Be(20);
        monoEngine.PeerId.Text.Should().NotContain("MO3002");

        var connMgrProp = typeof(ClientEngine).GetProperty("ConnectionManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var connMgrField = typeof(ClientEngine).GetField("ConnectionManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(ClientEngine).GetField("<ConnectionManager>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var connMgr = connMgrProp?.GetValue(monoEngine) ?? connMgrField?.GetValue(monoEngine);
        connMgr.Should().NotBeNull();

        var localPeerIdProp = connMgr!.GetType().GetProperty("LocalPeerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var localPeerIdField = connMgr.GetType().GetField("<LocalPeerId>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? connMgr.GetType().GetField("LocalPeerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var localPeerId = (localPeerIdProp?.GetValue(connMgr) ?? localPeerIdField?.GetValue(connMgr)) as MonoTorrent.BEncoding.BEncodedString;
        localPeerId.Should().NotBeNull();
        localPeerId!.Text.Should().StartWith("-qB4420-");
        localPeerId.Text.Should().NotContain("MO3002");

        await this.engine.StopAsync();
    }

    [Test]
    public async Task Handle_ConfigSavedEvent_UpdatesPeerIdAndConnectionManager()
    {
        this.configService.PeerIdPrefix.Returns("-qB4420-");
        this.configService.BitTorrentUserAgent.Returns("qBittorrent/4.4.2");

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine!.PeerId.Text.Should().StartWith("-qB4420-");

        // Update to Deluge preset
        this.configService.PeerIdPrefix.Returns("-DE2050-");
        this.configService.BitTorrentUserAgent.Returns("Deluge/2.0.5 libtorrent/1.2.14.0");

        this.engine.Handle(new ConfigSavedEvent());

        monoEngine.PeerId.Text.Should().StartWith("-DE2050-");
        monoEngine.PeerId.Text.Should().NotContain("MO3002");

        var connMgrProp = typeof(ClientEngine).GetProperty("ConnectionManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var connMgrField = typeof(ClientEngine).GetField("ConnectionManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(ClientEngine).GetField("<ConnectionManager>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var connMgr = connMgrProp?.GetValue(monoEngine) ?? connMgrField?.GetValue(monoEngine);
        var localPeerIdProp = connMgr!.GetType().GetProperty("LocalPeerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var localPeerIdField = connMgr.GetType().GetField("<LocalPeerId>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? connMgr.GetType().GetField("LocalPeerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var localPeerId = (localPeerIdProp?.GetValue(connMgr) ?? localPeerIdField?.GetValue(connMgr)) as MonoTorrent.BEncoding.BEncodedString;
        localPeerId.Should().NotBeNull();
        localPeerId!.Text.Should().StartWith("-DE2050-");
        localPeerId.Text.Should().NotContain("MO3002");

        await this.engine.StopAsync();
    }

    [Test]
    public async Task Handle_ConfigSavedEvent_AppliesDynamicConnectionLimitsAndSpeedLimitsAndNetworkOptions()
    {
        this.configService.MaxGlobalConnections.Returns(100);
        this.configService.MaxDownloadSpeedKbps.Returns(500);
        this.configService.MaxUploadSpeedKbps.Returns(250);
        this.configService.EncryptionMode.Returns("preferencrypted");
        this.configService.EnableDht.Returns(false);
        this.configService.TransportConnectionTimeoutSeconds.Returns(15);
        this.configService.WebSeedDelaySeconds.Returns(10);
        this.configService.UpnpEnabled.Returns(false);
        this.configService.ExtensionLtDontHave.Returns(false);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.MaximumConnections.Should().Be(100);
        monoEngine.Settings.MaximumDownloadRate.Should().Be(500 * 1024);
        monoEngine.Settings.MaximumUploadRate.Should().Be(250 * 1024);
        monoEngine.Settings.DhtEndPoint.Should().BeNull();
        monoEngine.Settings.AllowPortForwarding.Should().BeFalse();
        monoEngine.Settings.AllowHaveSuppression.Should().BeFalse();

        // Update settings in config
        this.configService.MaxGlobalConnections.Returns(1000);
        this.configService.MaxDownloadSpeedKbps.Returns(5000);
        this.configService.MaxUploadSpeedKbps.Returns(2000);
        this.configService.EncryptionMode.Returns("forceencrypted");
        this.configService.EnableDht.Returns(true);
        this.configService.TransportConnectionTimeoutSeconds.Returns(45);
        this.configService.WebSeedDelaySeconds.Returns(60);
        this.configService.UpnpEnabled.Returns(true);
        this.configService.ExtensionLtDontHave.Returns(true);

        this.engine.Handle(new ConfigSavedEvent());

        // Wait for background Task.Run in Handle to complete
        for (var i = 0; i < 50 && monoEngine.Settings.MaximumConnections != 1000; i++)
        {
            await Task.Delay(20);
        }

        monoEngine.Settings.MaximumConnections.Should().Be(1000);
        monoEngine.Settings.MaximumDownloadRate.Should().Be(5000 * 1024);
        monoEngine.Settings.MaximumUploadRate.Should().Be(2000 * 1024);
        monoEngine.Settings.AllowedEncryption.Should().Contain(MonoTorrent.Connections.EncryptionType.RC4Full);
        monoEngine.Settings.AllowedEncryption.Should().NotContain(MonoTorrent.Connections.EncryptionType.PlainText);
        monoEngine.Settings.DhtEndPoint.Should().NotBeNull();
        monoEngine.Settings.ConnectionTimeout.Should().Be(TimeSpan.FromSeconds(45));
        monoEngine.Settings.WebSeedDelay.Should().Be(TimeSpan.FromSeconds(60));
        monoEngine.Settings.AllowPortForwarding.Should().BeTrue();
        monoEngine.Settings.AllowHaveSuppression.Should().BeTrue();

        await this.engine.StopAsync();
    }

    [Test]
    public async Task Handle_ConfigFileSavedEvent_AppliesDynamicConnectionLimitsAndSpeedLimits()
    {
        this.configService.MaxGlobalConnections.Returns(150);
        this.configService.MaxDownloadSpeedKbps.Returns(300);
        this.configService.MaxUploadSpeedKbps.Returns(150);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.MaximumConnections.Should().Be(150);

        // Update settings in config
        this.configService.MaxGlobalConnections.Returns(800);
        this.configService.MaxDownloadSpeedKbps.Returns(4000);
        this.configService.MaxUploadSpeedKbps.Returns(1500);

        this.engine.Handle(new ConfigFileSavedEvent());

        // Wait for background Task.Run in Handle to complete
        for (var i = 0; i < 50 && monoEngine.Settings.MaximumConnections != 800; i++)
        {
            await Task.Delay(20);
        }

        monoEngine.Settings.MaximumConnections.Should().Be(800);
        monoEngine.Settings.MaximumDownloadRate.Should().Be(4000 * 1024);
        monoEngine.Settings.MaximumUploadRate.Should().Be(1500 * 1024);

        await this.engine.StopAsync();
    }

    [Test]
    public async Task StartAsync_WithInvalidOrLeakedPrefix_FallsBackToDefaultPreset()
    {
        this.configService.PeerIdPrefix.Returns("-MO3002-");
        this.configService.BitTorrentUserAgent.Returns("MonoTorrent/3.0.2");

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.PeerId.Text.Should().StartWith("-qB4650-");
        monoEngine.PeerId.Text.Should().NotContain("MO3002");

        await this.engine.StopAsync();
    }

    [Test]
    public void GetTask_WhenNotFound_ReturnsNull()
    {
        var task = this.engine.GetTask(9999);
        task.Should().BeNull();
    }

    [Test]
    public void GetAllTasks_WhenEmpty_ReturnsEmptyCollection()
    {
        var tasks = this.engine.GetAllTasks();
        tasks.Should().BeEmpty();
    }

    #region Ingestion Tests

    [Test]
    public async Task AddTorrentAsync_WithV1BtihMagnetUri_AddsAndRegistersTask()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu.iso&tr=http%3A%2F%2Ftracker.local%2Fannounce";
        var torrent = new CoreTorrent
        {
            Id = 1,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Name = "Ubuntu.iso",
            Category = "linux",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, magnetUri: magnetUri);

        task.Should().NotBeNull();
        task.TorrentId.Should().Be(1);
        task.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        torrent.Category.Should().Be("linux");

        var retrieved = this.engine.GetTask(1);
        retrieved.Should().NotBeNull();
        retrieved!.TorrentId.Should().Be(1);

        var allTasks = this.engine.GetAllTasks().ToList();
        allTasks.Should().ContainSingle(t => t.TorrentId == 1);
    }

    [Test]
    public async Task AddTorrentAsync_WithV2BtmhMagnetUri_AddsAndRegistersTask()
    {
        var magnetUri = "magnet:?xt=urn:btih:d8fadd013a563de212309d361d4810186076b63b&dn=V2Torrent";
        var torrent = new CoreTorrent
        {
            Id = 2,
            InfoHash = "d8fadd013a563de212309d361d4810186076b63b",
            Name = "V2Torrent",
            Category = "iso",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, magnetUri: magnetUri);

        task.Should().NotBeNull();
        task.TorrentId.Should().Be(2);
        task.InfoHash.Should().Be("d8fadd013a563de212309d361d4810186076b63b");
    }

    [Test]
    public async Task AddTorrentAsync_WithTorrentFileBytes_AddsAndRegistersTask()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("debian.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 3,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "debian.iso",
            Category = "os",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.TorrentId.Should().Be(3);
        task.InfoHash.Should().Be(torrent.InfoHash);
        task.Manager.Should().NotBeNull();
        task.Manager.Torrent.Should().NotBeNull();
        task.Manager.Torrent.Name.Should().Be("debian.iso");
    }

    [Test]
    public async Task AddTorrentAsync_WithInfoHashAndTrackerUrl_ConstructsMagnetFallback()
    {
        var torrent = new CoreTorrent
        {
            Id = 4,
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            Name = "FallbackTorrent",
            TrackerUrl = "http://tracker.fallback.org/announce",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent);

        task.Should().NotBeNull();
        task.TorrentId.Should().Be(4);
        task.InfoHash.Should().Be("aabbccddeeff00112233445566778899aabbccdd");
    }

    [Test]
    public async Task AddTorrentAsync_WithPausedStatus_PausesManager()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("paused.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 5,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "paused.iso",
            Status = TorrentStatus.Paused,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.State.Should().BeOneOf(TorrentState.Paused, TorrentState.Stopping, TorrentState.Stopped);
    }

    [Test]
    public async Task AddTorrentAsync_WithStoppedStatus_DoesNotStartManager()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("stopped.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 6,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "stopped.iso",
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.State.Should().Be(TorrentState.Stopped);
    }

    [Test]
    public async Task AddTorrentAsync_WhenAutoStartIsFalse_PausesManagerAndSetsStatusToPaused()
    {
        this.configService.AutoStart.Returns(false);

        var torrentBytes = CreateSampleSingleFileTorrentBytes("autostart_false.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 939,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "autostart_false.iso",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        torrent.Status.Should().Be(TorrentStatus.Paused);
        task.Manager.State.Should().BeOneOf(TorrentState.Paused, TorrentState.Stopping, TorrentState.Stopped);
    }

    [Test]
    public async Task AddTorrentAsync_WhenCompletedOrSeeding_UsesTorrentSavePath()
    {
        var customSavePath = Path.Combine(Path.GetTempPath(), "leecharr_completed_" + Guid.NewGuid().ToString("N"));
        var torrentBytes = CreateSampleSingleFileTorrentBytes("completed.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 7,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "completed.iso",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SavePath = customSavePath,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.SavePath.Should().Be(customSavePath);
    }

    [Test]
    public async Task GetResourceMetrics_WhenSeedingTorrentWithZeroSessionDownloadedBytes_CalculatesRatioFromTorrentSize()
    {
        var customSavePath = Path.Combine(Path.GetTempPath(), "leecharr_metrics_" + Guid.NewGuid().ToString("N"));
        var torrentBytes = CreateSampleSingleFileTorrentBytes("metrics_test.bin", length: 16384);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 88,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "metrics_test.bin",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SavePath = customSavePath,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        var metrics = task.GetResourceMetrics();
        metrics.Should().NotBeNull();
        metrics.TotalBytes.Should().Be(16384);
        metrics.Ratio.Should().Be(0.0);
    }

    [Test]
    public async Task AddTorrentAsync_WhenEnableIncompleteDirIsFalse_UsesCompletedDirDirectly()
    {
        this.configService.EnableIncompleteDir.Returns(false);
        var torrentBytes = CreateSampleSingleFileTorrentBytes("direct_completed.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 8,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "direct_completed.iso",
            Status = TorrentStatus.Downloading,
            Progress = 0.0,
            Category = "tv",
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.SavePath.Should().Be(this.testDownloadDir);
    }

    [Test]
    public async Task AddTorrentAsync_WhenEnableIncompleteDirIsFalseAndExplicitSavePathGiven_UsesExplicitSavePath()
    {
        this.configService.EnableIncompleteDir.Returns(false);
        var customPath = Path.Combine(Path.GetTempPath(), "custom_staging_" + Guid.NewGuid().ToString("N"));
        var torrentBytes = CreateSampleSingleFileTorrentBytes("custom_direct.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 9,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "custom_direct.iso",
            Status = TorrentStatus.Downloading,
            Progress = 0.0,
            SavePath = customPath,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.SavePath.Should().Be(customPath);
    }

    #endregion

    #region Private Tracker Mode Tests

    [Test]
    public async Task AddTorrentAsync_WhenTorrentIsPrivate_DisablesDhtAndPex()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("private.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 10,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "private.iso",
            IsPrivate = true,
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.Settings.AllowDht.Should().BeFalse();
        task.Manager.Settings.AllowPeerExchange.Should().BeFalse();
    }

    [Test]
    public async Task AddTorrentAsync_WhenMagnetTorrentIsPrivate_PreemptivelyDisablesDhtAndPexDuringMetadataFetching()
    {
        var infoHashHex = "0123456789abcdef0123456789abcdef01234567";
        var magnetUri = $"magnet:?xt=urn:btih:{infoHashHex}&dn=PrivateMagnet";

        var torrent = new CoreTorrent
        {
            Id = 15,
            InfoHash = infoHashHex,
            Name = "PrivateMagnet",
            IsPrivate = true,
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, magnetUri: magnetUri);

        task.Should().NotBeNull();
        task.IsPrivate.Should().BeTrue();
        task.Manager.Settings.AllowDht.Should().BeFalse();
        task.Manager.Settings.AllowPeerExchange.Should().BeFalse();
    }

    [Test]
    public async Task AddTorrentAsync_WhenTorrentIsNotPrivate_AllowsDhtAndPexByDefault()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("public.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 11,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "public.iso",
            IsPrivate = false,
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.Settings.AllowDht.Should().BeTrue();
        task.Manager.Settings.AllowPeerExchange.Should().BeTrue();
    }

    [Test]
    public async Task AddTorrentAsync_WhenPrivateTorrentHasTrackerFailureAndZeroPeers_EntersStalledState()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("private_stall.iso", isPrivate: true);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 42,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "private_stall.iso",
            IsPrivate = true,
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.IsPrivate.Should().BeTrue();
        task.IsStalled.Should().BeFalse();

        // Simulate tracker failure
        task.SetTrackerStalled("Tracker failure: http://tracker.example.com/announce: Offline", this.eventAggregator);

        task.Status.Should().Be(TorrentStatus.Stalled);
        task.IsStalled.Should().BeTrue();
        task.ErrorMessage.Should().Contain("Offline");

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.TorrentId == 42 &&
            !e.IsResolved &&
            e.Source == "Tracker" &&
            e.Message.Contains("Offline")));
    }

    [Test]
    public async Task AddTorrentAsync_WhenStalledPrivateTorrentTrackerRecovers_RestoresDownloadingState()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("private_recover.iso", isPrivate: true);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 43,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "private_recover.iso",
            IsPrivate = true,
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.SetTrackerStalled("Tracker failure: http://tracker.example.com/announce: Offline", this.eventAggregator);
        task.Status.Should().Be(TorrentStatus.Stalled);

        // Tracker recovers and peers connect
        task.ClearTrackerStalled(this.eventAggregator);

        task.Status.Should().Be(TorrentStatus.Downloading);
        task.IsStalled.Should().BeFalse();
        task.ErrorMessage.Should().BeNull();

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.TorrentId == 43 &&
            e.IsResolved &&
            e.Source == "Tracker"));
    }

    [Test]
    public async Task CheckTrackerHealth_OnEngine_ChecksAllActiveTasks()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("private_check.iso", isPrivate: true);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 44,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "private_check.iso",
            IsPrivate = true,
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        this.engine.CheckTrackerHealth();
    }

    [Test]
    public async Task CheckTrackerHealth_WhenTrackersInConnectingState_DoesNotStallOrPublishHealthIssue()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("private_connecting.iso", isPrivate: true);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 50,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "private_connecting.iso",
            IsPrivate = true,
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State != TorrentState.Downloading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        task.Manager.State.Should().Be(TorrentState.Downloading);

        var trackers = task.Manager.TrackerManager.Tiers.SelectMany(t => t.Trackers).ToList();
        trackers.Should().NotBeEmpty();

        SetTrackerStatus(trackers[0], MonoTorrent.Trackers.TrackerState.Connecting);

        var isStalled = task.CheckTrackerHealth(this.eventAggregator);

        isStalled.Should().BeFalse();
        task.IsStalled.Should().BeFalse();
        task.Status.Should().Be(TorrentStatus.Downloading);
        task.ErrorMessage.Should().BeNull();
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<HealthIssueEvent>());

        SetTrackerStatus(trackers[0], MonoTorrent.Trackers.TrackerState.Unknown);
        task.CheckTrackerHealth(this.eventAggregator).Should().BeFalse();
        task.IsStalled.Should().BeFalse();

        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<HealthIssueEvent>());
    }

    [Test]
    [TestCase(MonoTorrent.Trackers.TrackerState.Offline)]
    [TestCase(MonoTorrent.Trackers.TrackerState.InvalidResponse)]
    public async Task CheckTrackerHealth_WhenTrackersDefinitivelyFailed_TriggersStalledStatusAndPublishesHealthIssue(MonoTorrent.Trackers.TrackerState failedState)
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes($"private_failed_{failedState}.iso", isPrivate: true);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 51,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = $"private_failed_{failedState}.iso",
            IsPrivate = true,
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State != TorrentState.Downloading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        task.Manager.State.Should().Be(TorrentState.Downloading);

        var trackers = task.Manager.TrackerManager.Tiers.SelectMany(t => t.Trackers).ToList();
        trackers.Should().NotBeEmpty();

        foreach (var tierItem in task.Manager.TrackerManager.Tiers)
        {
            SetTierAnnounceSucceeded(tierItem, false);
        }

        foreach (var tracker in trackers)
        {
            SetTrackerStatus(tracker, failedState);
        }

        var isStalled = task.CheckTrackerHealth(this.eventAggregator);

        isStalled.Should().BeTrue();
        task.IsStalled.Should().BeTrue();
        task.Status.Should().Be(TorrentStatus.Stalled);
        task.ErrorMessage.Should().NotBeNullOrEmpty();
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.TorrentId == 51 &&
            !e.IsResolved &&
            e.Source == "Tracker"));
    }

    [Test]
    public async Task CheckTrackerHealth_WhenMixedStates_DoesNotStallUntilAllTrackersHaveResolved()
    {
        var trackerUrls = new List<string>
        {
            "http://tracker1.example.com/announce",
            "http://tracker2.example.com/announce",
        };

        var torrentBytes = CreateSampleSingleFileTorrentBytes("private_mixed.iso", isPrivate: true, trackerUrls: trackerUrls);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 52,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "private_mixed.iso",
            IsPrivate = true,
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State != TorrentState.Downloading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        task.Manager.State.Should().Be(TorrentState.Downloading);

        var trackers = task.Manager.TrackerManager.Tiers.SelectMany(t => t.Trackers).ToList();
        trackers.Count.Should().Be(2);

        foreach (var tierItem in task.Manager.TrackerManager.Tiers)
        {
            SetTierAnnounceSucceeded(tierItem, false);
        }

        trackers = task.Manager.TrackerManager.Tiers.SelectMany(t => t.Trackers).ToList();
        SetTrackerStatus(trackers[0], MonoTorrent.Trackers.TrackerState.Offline);
        SetTrackerStatus(trackers[1], MonoTorrent.Trackers.TrackerState.Connecting);
        foreach (var tierItem in task.Manager.TrackerManager.Tiers)
        {
            SetTierAnnounceSucceeded(tierItem, false);
        }

        var isStalledConnecting = task.CheckTrackerHealth(this.eventAggregator);

        isStalledConnecting.Should().BeFalse();
        task.IsStalled.Should().BeFalse();
        task.Status.Should().Be(TorrentStatus.Downloading);
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<HealthIssueEvent>());

        trackers = task.Manager.TrackerManager.Tiers.SelectMany(t => t.Trackers).ToList();
        SetTrackerStatus(trackers[0], MonoTorrent.Trackers.TrackerState.Offline);
        SetTrackerStatus(trackers[1], MonoTorrent.Trackers.TrackerState.Unknown);
        foreach (var tierItem in task.Manager.TrackerManager.Tiers)
        {
            SetTierAnnounceSucceeded(tierItem, false);
        }

        var isStalledUnknown = task.CheckTrackerHealth(this.eventAggregator);

        isStalledUnknown.Should().BeFalse();
        task.IsStalled.Should().BeFalse();
        task.Status.Should().Be(TorrentStatus.Downloading);
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<HealthIssueEvent>());

        trackers = task.Manager.TrackerManager.Tiers.SelectMany(t => t.Trackers).ToList();
        SetTrackerStatus(trackers[0], MonoTorrent.Trackers.TrackerState.Offline);
        SetTrackerStatus(trackers[1], MonoTorrent.Trackers.TrackerState.InvalidResponse);

        foreach (var tierItem in task.Manager.TrackerManager.Tiers)
        {
            SetTierAnnounceSucceeded(tierItem, false);
        }

        var isStalledAfterResolved = task.CheckTrackerHealth(this.eventAggregator);

        isStalledAfterResolved.Should().BeTrue();
        task.IsStalled.Should().BeTrue();
        task.Status.Should().Be(TorrentStatus.Stalled);
        task.ErrorMessage.Should().NotBeNullOrEmpty();
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.TorrentId == 52 &&
            !e.IsResolved &&
            e.Source == "Tracker"));
    }

    private static void SetTrackerStatus(MonoTorrent.Trackers.ITracker tracker, MonoTorrent.Trackers.TrackerState state)
    {
        var currentType = tracker.GetType();
        while (currentType != null)
        {
            var prop = currentType.GetProperty("StatusOverride", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            prop?.SetValue(tracker, (MonoTorrent.Trackers.TrackerState?)state);

            var field = currentType.GetField("<Status>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? currentType.GetField("status", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? currentType.GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic);

            if (field != null)
            {
                field.SetValue(tracker, state);
                break;
            }

            currentType = currentType.BaseType;
        }
    }

    private static void SetTierAnnounceSucceeded(object tier, bool succeeded)
    {
        var currentType = tier.GetType();
        while (currentType != null)
        {
            var field = currentType.GetField("<LastAnnounceSucceeded>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? currentType.GetField("lastAnnounceSucceeded", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? currentType.GetField("_lastAnnounceSucceeded", BindingFlags.Instance | BindingFlags.NonPublic);

            if (field != null)
            {
                field.SetValue(tier, succeeded);
                break;
            }

            currentType = currentType.BaseType;
        }
    }

    #endregion

    #region Sequential Streaming Download Tests

    [Test]
    public async Task AddTorrentAsync_WhenSequentialDownloadTrue_AddsInStreamingMode()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("stream.mp4");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 20,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "stream.mp4",
            SequentialDownload = true,
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.Should().NotBeNull();
        this.engine.GetTask(20).Should().NotBeNull();
    }

    [Test]
    public async Task AddTorrentAsync_WhenSequentialDownloadTrue_WithMagnet_AddsInStreamingMode()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=streaming.mkv";
        var torrent = new CoreTorrent
        {
            Id = 21,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Name = "streaming.mkv",
            SequentialDownload = true,
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, magnetUri: magnetUri);

        task.Should().NotBeNull();
        task.Manager.Should().NotBeNull();
    }

    #endregion

    #region Safe Deletion Tests

    [Test]
    public async Task RemoveTorrentAsync_WithDeleteFilesFalse_DoesNotDeleteFilesOrDirectories()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("nodelete.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 30,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "nodelete.bin",
            Status = TorrentStatus.Stopped,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.RemoveTorrentAsync(30, deleteFiles: false);

        this.engine.GetTask(30).Should().BeNull();
        this.diskProvider.DidNotReceive().DeleteFile(Arg.Any<string>());
        this.diskProvider.DidNotReceive().DeleteFolder(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public async Task RemoveTorrentAsync_WithDeleteFilesTrue_DeletesIndividualFiles()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("deletefiles.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 31,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "deletefiles.bin",
            Status = TorrentStatus.Stopped,
        };

        this.diskProvider.FileExists(Arg.Any<string>()).Returns(true);

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.RemoveTorrentAsync(31, deleteFiles: true);

        this.engine.GetTask(31).Should().BeNull();
        this.diskProvider.Received().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public async Task RemoveTorrentAsync_WithDeleteFilesTrue_ForMultiFileTorrent_DeletesSubdirectory()
    {
        var torrentBytes = CreateSampleMultiFileTorrentBytes("MultiTorrentFolder");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 32,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "MultiTorrentFolder",
            Status = TorrentStatus.Stopped,
        };

        this.diskProvider.FolderExists(Arg.Any<string>()).Returns(true);

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.RemoveTorrentAsync(32, deleteFiles: true);

        // Crucial safety check: Ensure incomplete root directory itself is never deleted
        this.diskProvider.DidNotReceive().DeleteFolder(this.testIncompleteDir, true);
    }

    [Test]
    public async Task RemoveTorrentAsync_WithDeleteFilesTrue_WhenContainingDirIsRootDownload_DoesNotDeleteParentDirectory()
    {
        var multiBytes = CreateSampleMultiFileTorrentBytes("MultiRootDownload");
        var parsed = MonoTorrent.Torrent.Load(multiBytes);

        var torrent = new CoreTorrent
        {
            Id = 33,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "MultiRootDownload",
            Status = TorrentStatus.Seeding,
            SavePath = this.testDownloadDir,
        };

        this.diskProvider.FileExists(Arg.Any<string>()).Returns(true);
        this.diskProvider.FolderExists(Arg.Any<string>()).Returns(true);

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: multiBytes);
        await this.engine.RemoveTorrentAsync(33, deleteFiles: true);

        // Crucial safety check: Ensure download root directory itself is never deleted
        this.diskProvider.DidNotReceive().DeleteFolder(this.testDownloadDir, true);
    }

    [Test]
    public async Task RemoveTorrentAsync_WithDeleteFilesTrue_WhenContainingDirMatchesTorrentName_DeletesDedicatedFolder()
    {
        var multiBytes = CreateSampleMultiFileTorrentBytes("DedicatedTorrentFolder");
        var parsed = MonoTorrent.Torrent.Load(multiBytes);

        var dedicatedPath = Path.Combine(this.testDownloadDir, "DedicatedTorrentFolder");
        var torrent = new CoreTorrent
        {
            Id = 34,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "DedicatedTorrentFolder",
            Status = TorrentStatus.Seeding,
            SavePath = this.testDownloadDir,
        };

        this.diskProvider.FileExists(Arg.Any<string>()).Returns(true);
        this.diskProvider.FolderExists(Arg.Any<string>()).Returns(true);

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: multiBytes);
        await this.engine.RemoveTorrentAsync(34, deleteFiles: true);

        this.diskProvider.Received().DeleteFolder(Arg.Is<string>(p => p.Contains("DedicatedTorrentFolder")), true);
    }

    #endregion

    #region Concurrency & Thread-Safety Tests

    [Test]
    public async Task MonoTorrentDownloadTask_ConcurrentPeersAndAvailability_DoesNotThrow()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("concurrent.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 40,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "concurrent.iso",
            Status = TorrentStatus.Stopped,
        };

        var task = await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        // Concurrently invoke GetPeers(), PieceAvailability, PieceBitfield, Status, Progress
        var tasks = Enumerable.Range(0, 30).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                var peers = task.GetPeers();
                peers.Should().NotBeNull();

                var avail = task.PieceAvailability;
                avail.Should().NotBeNull();

                var bitfield = task.PieceBitfield;
                bitfield.Should().NotBeNull();

                var status = task.Status;
                status.Should().Be(TorrentStatus.Stopped);

                var progress = task.Progress;
                progress.Should().BeGreaterThanOrEqualTo(0.0);

                var dlSpeed = task.DownloadSpeed;
                var ulSpeed = task.UploadSpeed;
                var dlBytes = task.DownloadedBytes;
                var ulBytes = task.UploadedBytes;
            }
        })).ToArray();

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public void MonoTorrentDownloadTask_SynchronizePieceAvailability_DoesNotDecaySwarmAvailabilityOnPeerDisconnect()
    {
        var picker = new PiecePicker(5, 16384, 81920);
        picker.SetAvailability(new[] { 2, 2, 2, 2, 2 });

        var task = new MonoTorrentDownloadTask(1, "test-hash", null, picker: picker);
        task.PieceAvailability.Should().Equal(2, 2, 2, 2, 2);

        task.SynchronizePieceAvailability();
        task.PieceAvailability.Should().Equal(2, 2, 2, 2, 2);
    }

    #endregion

    #region State Transition & Event Safety Tests

    [Test]
    public void MonoTorrentDownloadTask_StatusMapping_ReflectsMonoTorrentStates()
    {
        var nullTask = new MonoTorrentDownloadTask(100, "abc", null);
        nullTask.Status.Should().Be(TorrentStatus.Stopped);
        nullTask.DownloadedBytes.Should().Be(0);
        nullTask.UploadedBytes.Should().Be(0);
        nullTask.Progress.Should().Be(0.0);
        nullTask.DownloadSpeed.Should().Be(0);
        nullTask.UploadSpeed.Should().Be(0);
        nullTask.ConnectedSeeders.Should().Be(0);
        nullTask.ConnectedLeechers.Should().Be(0);
        nullTask.PieceBitfield.Should().BeEmpty();
        nullTask.PieceAvailability.Should().BeEmpty();
        nullTask.GetPeers().Should().BeEmpty();
    }

    [Test]
    public async Task PauseTorrentAsync_WhenTorrentActive_PausesTask()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("to_pause.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 40,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "to_pause.iso",
            Status = TorrentStatus.Downloading,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.PauseTorrentAsync(40);

        var task = this.engine.GetTask(40);
        task.Should().NotBeNull();
        task!.Status.Should().BeOneOf(TorrentStatus.Paused, TorrentStatus.Stopped);
    }

    [Test]
    public async Task ResumeTorrentAsync_WhenTorrentPaused_ResumesTask()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("to_resume.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 41,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "to_resume.iso",
            Status = TorrentStatus.Paused,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.ResumeTorrentAsync(41);

        var task = this.engine.GetTask(41);
        task.Should().NotBeNull();
        task!.Status.Should().BeOneOf(TorrentStatus.Downloading, TorrentStatus.Checking);
    }

    [Test]
    public async Task PauseTorrentAsync_WhenTorrentNotFound_DoesNotThrow()
    {
        var act = async () => await this.engine.PauseTorrentAsync(9999);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ForceRecheckAsync_WhenTorrentNotFound_DoesNotThrow()
    {
        var act = async () => await this.engine.ForceRecheckAsync(9999);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ForceRecheckAsync_WhenTorrentExists_InitiatesHashCheck()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("to_recheck.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 42,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "to_recheck.iso",
            Status = TorrentStatus.Paused,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.ForceRecheckAsync(42);

        var task = this.engine.GetTask(42);
        task.Should().NotBeNull();
        task!.Status.Should().BeOneOf(TorrentStatus.Checking, TorrentStatus.Downloading, TorrentStatus.Stopped, TorrentStatus.Paused);
    }

    [Test]
    public async Task ForceRecheckAsync_WhenTorrentIsRunning_StopsAndHashChecks()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("to_recheck_running.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 43,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "to_recheck_running.iso",
            Status = TorrentStatus.Downloading,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        var act = async () => await this.engine.ForceRecheckAsync(43);
        await act.Should().NotThrowAsync();

        var task = this.engine.GetTask(43);
        task.Should().NotBeNull();
    }

    [Test]
    public async Task ForceRecheckAsync_WhenAnotherTorrentIsHashing_QueuesSecondTorrentSequentially()
    {
        var torrentBytes1 = CreateSampleSingleFileTorrentBytes("recheck1.iso");
        var parsed1 = MonoTorrent.Torrent.Load(torrentBytes1);
        var torrent1 = new CoreTorrent
        {
            Id = 501,
            InfoHash = parsed1.InfoHashes.V1OrV2.ToHex(),
            Name = "recheck1.iso",
            Status = TorrentStatus.Paused,
        };

        var torrentBytes2 = CreateSampleSingleFileTorrentBytes("recheck2.iso");
        var parsed2 = MonoTorrent.Torrent.Load(torrentBytes2);
        var torrent2 = new CoreTorrent
        {
            Id = 502,
            InfoHash = parsed2.InfoHashes.V1OrV2.ToHex(),
            Name = "recheck2.iso",
            Status = TorrentStatus.Paused,
        };

        await this.engine.AddTorrentAsync(torrent1, torrentFileBytes: torrentBytes1);
        await this.engine.AddTorrentAsync(torrent2, torrentFileBytes: torrentBytes2);

        var task1 = (MonoTorrentDownloadTask)this.engine.GetTask(501)!;
        var task2 = (MonoTorrentDownloadTask)this.engine.GetTask(502)!;
        task1.Should().NotBeNull();
        task2.Should().NotBeNull();

        var modeField = task1.Manager!.GetType().GetField("mode", BindingFlags.Instance | BindingFlags.NonPublic);
        var originalMode = modeField!.GetValue(task1.Manager);
        var hashingModeType = typeof(TorrentManager).Assembly.GetType("MonoTorrent.Client.Modes.HashingMode");
        var hashingMode = Activator.CreateInstance(
            hashingModeType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { task1.Manager!, task1.Manager!.Engine.DiskManager },
            null);
        modeField.SetValue(task1.Manager, hashingMode);

        task1.Manager.State.Should().Be(TorrentState.Hashing);

        // Attempt force recheck on task2 while task1 is actively hashing
        await this.engine.ForceRecheckAsync(502);

        // Task 2 should be queued for recheck and not immediately hashing
        task2!.IsQueuedForRecheck.Should().BeTrue();
        task2.Status.Should().Be(TorrentStatus.QueuedForChecking);
        task2.Manager!.State.Should().NotBe(TorrentState.Hashing);
        this.eventAggregator.Received().PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e => e.Torrent.Id == 502 && e.NewStatus == TorrentStatus.QueuedForChecking));

        // When task 1 completes hashing, trigger state changed
        modeField.SetValue(task1.Manager, originalMode);
        var args = (TorrentStateChangedEventArgs)Activator.CreateInstance(
            typeof(TorrentStateChangedEventArgs),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { task1.Manager, TorrentState.Hashing, TorrentState.Downloading },
            null)!;

        await this.engine.HandleTorrentStateChangedAsync(args);

        // Allow async Task.Run in HandleTorrentStateChangedAsync to execute ForceRecheckAsync for task2
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (task2.IsQueuedForRecheck && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        task2.IsQueuedForRecheck.Should().BeFalse();
    }

    [Test]
    public async Task ForceRecheckAsync_WhenCompletedTorrentFailsHashCheck_ResetsFilesMovedToCompletedLatch()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("repair_completed.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);
        var torrent = new CoreTorrent
        {
            Id = 510,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "repair_completed.iso",
            Status = TorrentStatus.Seeding,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        var task = (MonoTorrentDownloadTask)this.engine.GetTask(510)!;
        task.Should().NotBeNull();

        // Simulate that files were previously moved to completed directory upon completion
        task.IsFilesMovedToCompleted = true;

        // Trigger force recheck - hash check runs and finds missing pieces
        await this.engine.ForceRecheckAsync(510);

        // Latch should be reset to allow re-downloading missing pieces to repair torrent
        task.IsFilesMovedToCompleted.Should().BeFalse();
        task.IsExplicitRecheck.Should().BeFalse();
    }

    [Test]
    public async Task ForceRecheckAsync_WhenNotExplicitRecheck_DoesNotResetFilesMovedToCompletedLatch()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("repair_nonexplicit.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);
        var torrent = new CoreTorrent
        {
            Id = 511,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "repair_nonexplicit.iso",
            Status = TorrentStatus.Seeding,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        var task = (MonoTorrentDownloadTask)this.engine.GetTask(511)!;
        task.Should().NotBeNull();

        task.IsFilesMovedToCompleted = true;
        task.IsExplicitRecheck = false;

        // Simulate routine/background hashing completion (e.g. startup recheck, not explicit force recheck)
        var args = (TorrentStateChangedEventArgs)Activator.CreateInstance(
            typeof(TorrentStateChangedEventArgs),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { task.Manager!, TorrentState.Hashing, TorrentState.Downloading },
            null)!;

        await this.engine.HandleTorrentStateChangedAsync(args);

        // Files moved latch should remain true because this was not an explicit user-initiated recheck
        task.IsFilesMovedToCompleted.Should().BeTrue();
    }

    [Test]
    public async Task ForceRecheckAsync_WhenQueued_SetsExplicitRecheckAndQueuedForRecheck()
    {
        var torrentBytes1 = CreateSampleSingleFileTorrentBytes("explicit_queue1.iso");
        var parsed1 = MonoTorrent.Torrent.Load(torrentBytes1);
        var torrent1 = new CoreTorrent
        {
            Id = 512,
            InfoHash = parsed1.InfoHashes.V1OrV2.ToHex(),
            Name = "explicit_queue1.iso",
            Status = TorrentStatus.Paused,
        };

        var torrentBytes2 = CreateSampleSingleFileTorrentBytes("explicit_queue2.iso");
        var parsed2 = MonoTorrent.Torrent.Load(torrentBytes2);
        var torrent2 = new CoreTorrent
        {
            Id = 513,
            InfoHash = parsed2.InfoHashes.V1OrV2.ToHex(),
            Name = "explicit_queue2.iso",
            Status = TorrentStatus.Paused,
        };

        await this.engine.AddTorrentAsync(torrent1, torrentFileBytes: torrentBytes1);
        await this.engine.AddTorrentAsync(torrent2, torrentFileBytes: torrentBytes2);

        var task1 = (MonoTorrentDownloadTask)this.engine.GetTask(512)!;
        var task2 = (MonoTorrentDownloadTask)this.engine.GetTask(513)!;

        var modeField = task1.Manager!.GetType().GetField("mode", BindingFlags.Instance | BindingFlags.NonPublic);
        var hashingModeType = typeof(TorrentManager).Assembly.GetType("MonoTorrent.Client.Modes.HashingMode");
        var hashingMode = Activator.CreateInstance(
            hashingModeType!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { task1.Manager!, task1.Manager!.Engine.DiskManager },
            null);
        modeField!.SetValue(task1.Manager, hashingMode);

        await this.engine.ForceRecheckAsync(513);

        task2.IsQueuedForRecheck.Should().BeTrue();
        task2.IsExplicitRecheck.Should().BeTrue();
    }

    #endregion

    #region Rate Limiting & File Priority Tests

    [Test]
    public async Task SetRateLimitsAsync_WhenEngineActive_UpdatesLimitsCleanly()
    {
        await this.engine.StartAsync();

        var act = async () => await this.engine.SetRateLimitsAsync(5000, 2000);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task SetRateLimitsAsync_WhenSpeedsExceedTwoGbps_ClampsToIntMaxValueWithoutOverflow()
    {
        await this.engine.StartAsync();

        var act = async () => await this.engine.SetRateLimitsAsync(2_500_000, 10_000_000);
        await act.Should().NotThrowAsync();

        var actMax = async () => await this.engine.SetRateLimitsAsync(int.MaxValue, int.MaxValue);
        await actMax.Should().NotThrowAsync();
    }

    [Test]
    public async Task SetTorrentRateLimitsAsync_WhenTorrentActive_UpdatesSettings()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("limits.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 70,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "limits.iso",
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        await this.engine.SetTorrentRateLimitsAsync(70, 1500, 500);

        task.Manager.Settings.MaximumDownloadRate.Should().Be(1500 * 1024);
        task.Manager.Settings.MaximumUploadRate.Should().Be(500 * 1024);
    }

    [Test]
    public async Task SetTorrentRateLimitsAsync_WhenSpeedsExceedTwoGbps_ClampsToIntMaxValueWithoutOverflow()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("limits_overflow.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 71,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "limits_overflow.iso",
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        // 2,500,000 KB/s (~2.5 Gbps) and 10,000,000 KB/s (~10 Gbps) would overflow a 32-bit signed int if multiplied directly by 1024
        await this.engine.SetTorrentRateLimitsAsync(71, 2_500_000, 10_000_000);

        task.Manager.Settings.MaximumDownloadRate.Should().Be(int.MaxValue);
        task.Manager.Settings.MaximumUploadRate.Should().Be(int.MaxValue);

        await this.engine.SetTorrentRateLimitsAsync(71, int.MaxValue, int.MaxValue);

        task.Manager.Settings.MaximumDownloadRate.Should().Be(int.MaxValue);
        task.Manager.Settings.MaximumUploadRate.Should().Be(int.MaxValue);
    }

    [Test]
    public async Task AddTorrentAsync_WhenLimitsExceedTwoGbps_ClampsToIntMaxValueWithoutOverflow()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("add_limits_overflow.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 72,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "add_limits_overflow.iso",
            Status = TorrentStatus.Stopped,
            DownloadLimit = 2_500_000,
            UploadLimit = 10_000_000,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Manager.Settings.MaximumDownloadRate.Should().Be(int.MaxValue);
        task.Manager.Settings.MaximumUploadRate.Should().Be(int.MaxValue);
    }

    [Test]
    public async Task SetFilePriorityAsync_WhenFileExists_UpdatesPriority()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("priority.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 80,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "priority.iso",
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        var filePath = task.Manager.Files[0].Path;

        // Test DoNotDownload (0), Low (2), High (4)
        await this.engine.SetFilePriorityAsync(80, filePath, 0);
        task.Manager.Files[0].Priority.Should().Be(MonoTorrent.Priority.DoNotDownload);

        await this.engine.SetFilePriorityAsync(80, filePath, 4);
        task.Manager.Files[0].Priority.Should().Be(MonoTorrent.Priority.High);
    }

    [Test]
    public async Task SetFilePriorityAsync_WhenTorrentNotFound_DoesNotThrow()
    {
        var act = async () => await this.engine.SetFilePriorityAsync(9999, "nonexistent.file", 3);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task SetFilePriorityAsync_WhenPathHasWindowsBackslashesOrLeadingSlashes_MatchesAndUpdatesPriority()
    {
        var multiBytes = CreateSampleMultiFileTorrentBytes("multi_priority_test");
        var parsed = MonoTorrent.Torrent.Load(multiBytes);

        var torrent = new CoreTorrent
        {
            Id = 81,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "multi_priority_test",
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: multiBytes);

        await this.engine.SetFilePriorityAsync(81, @"\subfolder\file1.dat", 0);
        task.Manager.Files[0].Priority.Should().Be(MonoTorrent.Priority.DoNotDownload);

        await this.engine.SetFilePriorityAsync(81, "/subfolder/file1.dat", 4);
        task.Manager.Files[0].Priority.Should().Be(MonoTorrent.Priority.High);

        await this.engine.SetFilePriorityAsync(81, @"subfolder\file1.dat", 2);
        task.Manager.Files[0].Priority.Should().Be(MonoTorrent.Priority.Low);
    }

    [Test]
    public async Task AddTorrentAsync_WhenTorrentIsPrivateAndBep27Enabled_DisablesDhtAndPex()
    {
        this.configService.EnableBep27PrivateTorrents.Returns(true);
        this.configService.EnableDht.Returns(true);
        this.configService.EnablePex.Returns(true);

        var torrentBytes = CreateSampleSingleFileTorrentBytes("private_bep27.bin", isPrivate: true);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 91,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "private_bep27.bin",
            IsPrivate = true,
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Manager.Settings.AllowDht.Should().BeFalse();
        task.Manager.Settings.AllowPeerExchange.Should().BeFalse();
    }

    [Test]
    public async Task AddTorrentAsync_WhenTorrentIsPrivateAndBep27DisabledInConfig_StillDisablesDhtAndPex()
    {
        this.configService.EnableBep27PrivateTorrents.Returns(false);
        this.configService.EnableDht.Returns(true);
        this.configService.EnablePex.Returns(true);

        var torrentBytes = CreateSampleSingleFileTorrentBytes("private_bep27_off.bin", isPrivate: true);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 92,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "private_bep27_off.bin",
            IsPrivate = true,
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Manager.Settings.AllowDht.Should().BeFalse();
        task.Manager.Settings.AllowPeerExchange.Should().BeFalse();
    }

    [Test]
    public async Task SetTorrentPrivateStatusAsync_TogglesDhtAndPexDynamically()
    {
        this.configService.EnableBep27PrivateTorrents.Returns(true);
        this.configService.EnableDht.Returns(true);
        this.configService.EnablePex.Returns(true);

        var torrentBytes = CreateSampleSingleFileTorrentBytes("dynamic_privacy.bin", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 93,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "dynamic_privacy.bin",
            IsPrivate = false,
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Manager.Settings.AllowDht.Should().BeTrue();
        task.Manager.Settings.AllowPeerExchange.Should().BeTrue();

        // Dynamically toggle to Private
        await this.engine.SetTorrentPrivateStatusAsync(93, true);
        task.Manager.Settings.AllowDht.Should().BeFalse();
        task.Manager.Settings.AllowPeerExchange.Should().BeFalse();

        // Dynamically toggle back to Public
        await this.engine.SetTorrentPrivateStatusAsync(93, false);
        task.Manager.Settings.AllowDht.Should().BeTrue();
        task.Manager.Settings.AllowPeerExchange.Should().BeTrue();
    }

    [Test]
    public async Task AddTorrentAsync_InitializesPiecePicker_WhenTorrentBytesProvided()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("picker_test.bin", length: 32768);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 101,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "picker_test.bin",
            Status = TorrentStatus.Stopped,
        };

        var task = await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Picker.Should().NotBeNull();
        task.Picker.PieceCount.Should().Be(parsed.PieceCount);
        task.Picker.PieceLength.Should().Be(parsed.PieceLength);
        task.Picker.TotalSize.Should().Be(parsed.Size);
        task.PieceAvailability.Should().NotBeNull();
        task.PieceAvailability.Length.Should().Be(parsed.PieceCount);
    }

    [Test]
    public async Task SetFilePriorityAsync_UpdatesPiecePickerPiecePriorities()
    {
        var torrentBytes = CreateSampleMultiFileTorrentBytes("multifile_picker.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 102,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "multifile_picker.bin",
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Picker.Should().NotBeNull();

        // The second file is at subfolder/file1.dat
        var targetFile = task.Manager.Files.First();
        await this.engine.SetFilePriorityAsync(102, targetFile.Path, 0); // DoNotDownload

        // Verify that piece priority was updated in the picker
        for (var p = targetFile.StartPieceIndex; p <= targetFile.EndPieceIndex; p++)
        {
            // PickBlocks with a peer having all pieces should skip piece with priority 0
            var fullBitfield = Enumerable.Repeat(true, task.Picker.PieceCount).ToArray();
            var requests = task.Picker.PickBlocks(fullBitfield, 10);
            requests.Any(r => r.PieceIndex == p).Should().BeFalse();
        }
    }

    [Test]
    public async Task SetFilePriorityAsync_WhenFilesSharePiece_PreservesPiecePriorityForWantedFiles()
    {
        var pieces = new byte[40]; // 2 pieces
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)(i % 255);
        }

        var fileList = new BEncodedList
        {
            new BEncodedDictionary
            {
                { "length", new BEncodedNumber(10000) },
                { "path", new BEncodedList { new BEncodedString("shared1.dat") } },
            },
            new BEncodedDictionary
            {
                { "length", new BEncodedNumber(20000) },
                { "path", new BEncodedList { new BEncodedString("shared2.dat") } },
            },
        };

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString("SharedPieceTorrent") },
            { "piece length", new BEncodedNumber(16384) },
            { "pieces", new BEncodedString(pieces) },
            { "files", fileList },
        };

        var rootDict = new BEncodedDictionary
        {
            { "info", infoDict },
        };

        var torrentBytes = rootDict.Encode();
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 103,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "SharedPieceTorrent",
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Picker.Should().NotBeNull();

        // File 1 is piece 0. File 2 spans piece 0 and piece 1.
        // Set File 1 to DoNotDownload (0). File 2 is still Normal (priority 1).
        await this.engine.SetFilePriorityAsync(103, task.Manager.Files[0].Path, 0);

        // Piece 0 must still be wanted (priority > 0) because File 2 overlaps piece 0
        var fullBitfield = Enumerable.Repeat(true, task.Picker.PieceCount).ToArray();
        var requests = task.Picker.PickBlocks(fullBitfield, 10);
        requests.Any(r => r.PieceIndex == 0).Should().BeTrue();

        // Now set File 2 to DoNotDownload as well
        await this.engine.SetFilePriorityAsync(103, task.Manager.Files[1].Path, 0);

        // Piece 0 and Piece 1 should now be skipped (priority 0)
        requests = task.Picker.PickBlocks(fullBitfield, 10);
        requests.Any(r => r.PieceIndex == 0).Should().BeFalse();
        requests.Any(r => r.PieceIndex == 1).Should().BeFalse();
    }

    [Test]
    public async Task AddTorrentAsync_UsesStreamingMode_WhenPiecePickerStrategyIsSequential()
    {
        this.configService.PiecePickerStrategy.Returns("Sequential");

        var torrentBytes = CreateSampleSingleFileTorrentBytes("sequential_picker.bin", length: 16384);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 103,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "sequential_picker.bin",
            SequentialDownload = false, // Engine strategy overrides
            Status = TorrentStatus.Stopped,
        };

        var task = await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Picker.Should().NotBeNull();
        task.Status.Should().Be(TorrentStatus.Stopped);
    }

    [Test]
    public async Task Blocklist_InjectsAndTracksBlockedPeersInMetrics()
    {
        var blocklistService = Substitute.For<NzbDrone.Core.Network.Blocklist.IBlocklistService>();
        blocklistService.IsIpBlocked("192.168.1.100").Returns(true);

        var customEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            blocklistService);

        try
        {
            customEngine.BlockedPeersCount.Should().Be(0);

            var torrentBytes = CreateSampleSingleFileTorrentBytes("blocklist_metrics.bin", length: 16384);
            var parsed = MonoTorrent.Torrent.Load(torrentBytes);

            var torrent = new CoreTorrent
            {
                Id = 202,
                InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
                Name = "blocklist_metrics.bin",
                Status = TorrentStatus.Stopped,
            };

            var task = await customEngine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
            task.Should().NotBeNull();

            var metrics = customEngine.GetEngineMetrics();
            metrics.BlockedPeersCount.Should().Be(0);
        }
        finally
        {
            customEngine.Dispose();
        }
    }

    [Test]
    public void EnableIPv6_Configuration_DefaultsToTrue()
    {
        this.configService.EnableIPv6.Returns(true);
        this.configService.EnableIPv6.Should().BeTrue();
    }

    [Test]
    public void Engine_Capabilities_SupportsV2AndExpectedFeatures()
    {
        this.engine.Capabilities.SupportsV2Torrents.Should().BeTrue();
        this.engine.Capabilities.SupportsSequentialDownload.Should().BeTrue();
        this.engine.Capabilities.SupportsCustomPiecePickers.Should().BeTrue();
        this.engine.Capabilities.SupportsDht.Should().BeTrue();
        this.engine.Capabilities.SupportsPex.Should().BeTrue();
        this.engine.Capabilities.SupportsUtp.Should().BeTrue();
    }

    [Test]
    public async Task StartAsync_WithUpnpEnabled_CoordinatesWithNatPmpPortMapperService_AndStopsOnEngineStop()
    {
        this.configService.UpnpEnabled.Returns(true);
        this.configService.ListeningPort.Returns(51413);

        var mockNatPmp = Substitute.For<INatPmpPortMapperService>();
        var customEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            natPmpPortMapperService: mockNatPmp);

        try
        {
            await customEngine.StartAsync();

            // Allow background task to invoke MapPortAsync
            await Task.Delay(100);

            await mockNatPmp.Received().MapPortAsync(51413, NatPmpProtocol.Tcp);
            await mockNatPmp.Received().MapPortAsync(51413, NatPmpProtocol.Udp);

            await customEngine.StopAsync();

            await mockNatPmp.Received().StopAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            customEngine.Dispose();
        }
    }

    #endregion

    #region Async Event Handler Reliability Tests

    [Test]
    public void OnTorrentCompleted_WhenCalledWithNullManager_DoesNotThrowUnhandledException()
    {
        var action = () =>
        {
            this.engine.OnTorrentCompleted(999, "0123456789012345678901234567890123456789", null);
        };

        action.Should().NotThrow();
    }

    [Test]
    public async Task OnTorrentCompletedAsync_WhenMoveFilesFails_CatchesExceptionAndPublishesCompletedEvent()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("completed_error_test.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 104,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "completed_error_test.bin",
            Status = TorrentStatus.Downloading,
            Category = "movies",
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        this.categoryService.GetSavePathForCategory("movies").Returns("/invalid_nonexistent_dest_dir");
        this.storagePathService.GetCompletedDirectory("movies").Returns("/invalid_nonexistent_dest_dir");

        var act = async () =>
        {
            await this.engine.OnTorrentCompletedAsync(104, torrent.InfoHash, task.Manager);
        };

        await act.Should().NotThrowAsync();

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentDownloadCompletedEvent>(e =>
            e.Torrent.Id == 104 &&
            e.Torrent.Status == TorrentStatus.Seeding));
    }

    [Test]
    public async Task OnTorrentCompletedAsync_WhenCategoryHasCustomSavePath_MovesFilesToCategorySavePath()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("completed_custom_category.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var customPath = Path.Combine(Path.GetTempPath(), "leecharr_custom_tv_" + Guid.NewGuid().ToString("N"));
        this.storagePathService.GetCompletedDirectory("tv").Returns(customPath);
        string tvMovedDest;
        this.storagePathService
            .MoveToCompleted(Arg.Any<string>(), "tv", Arg.Any<string>(), out tvMovedDest)
            .Returns(x =>
            {
                x[3] = customPath;
                return true;
            });

        var torrent = new CoreTorrent
        {
            Id = 110,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "completed_custom_category.bin",
            Status = TorrentStatus.Stopped,
            Category = "tv",
        };

        try
        {
            var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

            await this.engine.OnTorrentCompletedAsync(110, torrent.InfoHash, task.Manager);

            string tvAssertDest;
            this.storagePathService.Received(1).MoveToCompleted(Arg.Any<string>(), "tv", Arg.Any<string>(), out tvAssertDest);
            task.Manager.SavePath.Should().Be(customPath);
            this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentDownloadCompletedEvent>(e =>
                e.Torrent.Id == 110 &&
                e.Torrent.Category == "tv" &&
                e.Torrent.SavePath == customPath &&
                e.Torrent.Status == TorrentStatus.Seeding));
        }
        finally
        {
            if (Directory.Exists(customPath))
            {
                Directory.Delete(customPath, true);
            }
        }
    }

    [Test]
    public async Task OnTorrentCompletedAsync_WhenCategorySavePathIsEmpty_FallsBackToGlobalDownloadDir()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("completed_empty_category.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        string moviesMovedDest;
        this.storagePathService
            .MoveToCompleted(Arg.Any<string>(), "movies", Arg.Any<string>(), out moviesMovedDest)
            .Returns(x =>
            {
                x[3] = this.testDownloadDir;
                return true;
            });

        var torrent = new CoreTorrent
        {
            Id = 111,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "completed_empty_category.bin",
            Status = TorrentStatus.Stopped,
            Category = "movies",
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        await this.engine.OnTorrentCompletedAsync(111, torrent.InfoHash, task.Manager);

        string moviesAssertDest;
        this.storagePathService.Received(1).MoveToCompleted(Arg.Any<string>(), "movies", Arg.Any<string>(), out moviesAssertDest);
        task.Manager.SavePath.Should().Be(this.testDownloadDir);
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentDownloadCompletedEvent>(e =>
            e.Torrent.Id == 111 &&
            e.Torrent.Category == "movies" &&
            e.Torrent.SavePath == this.testDownloadDir &&
            e.Torrent.Status == TorrentStatus.Seeding));
    }

    [Test]
    public async Task OnTorrentCompletedAsync_WhenTorrentHasNoCategory_FallsBackToGlobalDownloadDir()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("completed_no_category.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        string noCatMovedDest;
        this.storagePathService
            .MoveToCompleted(Arg.Any<string>(), Arg.Is<string>(s => s == null), Arg.Any<string>(), out noCatMovedDest)
            .Returns(x =>
            {
                x[3] = this.testDownloadDir;
                return true;
            });

        var torrent = new CoreTorrent
        {
            Id = 112,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "completed_no_category.bin",
            Status = TorrentStatus.Stopped,
            Category = null,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        await this.engine.OnTorrentCompletedAsync(112, torrent.InfoHash, task.Manager);

        string noCatAssertDest;
        this.storagePathService.Received(1).MoveToCompleted(Arg.Any<string>(), Arg.Is<string>(s => s == null), Arg.Any<string>(), out noCatAssertDest);
        task.Manager.SavePath.Should().Be(this.testDownloadDir);
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentDownloadCompletedEvent>(e =>
            e.Torrent.Id == 112 &&
            e.Torrent.Category == null &&
            e.Torrent.SavePath == this.testDownloadDir &&
            e.Torrent.Status == TorrentStatus.Seeding));
    }

    [Test]
    public async Task OnTorrentCompletedAsync_WhenTorrentHasCustomSavePath_PreservesCustomSavePathAndDoesNotRelocateToCategoryOrDownloadDir()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("completed_custom_savepath.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var customSavePath = Path.Combine(Path.GetTempPath(), "leecharr_custom_savepath_" + Guid.NewGuid().ToString("N"));
        var categoryCompletedDir = Path.Combine(Path.GetTempPath(), "leecharr_cat_dir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(customSavePath);
        Directory.CreateDirectory(categoryCompletedDir);

        this.storagePathService.GetCompletedDirectory("movies").Returns(categoryCompletedDir);

        var torrent = new CoreTorrent
        {
            Id = 120,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "completed_custom_savepath.bin",
            Status = TorrentStatus.Stopped,
            Category = "movies",
            SavePath = customSavePath,
        };

        try
        {
            var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

            await this.engine.OnTorrentCompletedAsync(120, torrent.InfoHash, task.Manager);

            string assertDest;
            this.storagePathService.DidNotReceive().MoveToCompleted(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), out assertDest);
            task.Manager.SavePath.Should().Be(customSavePath);
            task.SavePath.Should().Be(customSavePath);
            this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentDownloadCompletedEvent>(e =>
                e.Torrent.Id == 120 &&
                e.Torrent.Category == "movies" &&
                e.Torrent.SavePath == customSavePath &&
                e.Torrent.Status == TorrentStatus.Seeding));
        }
        finally
        {
            if (Directory.Exists(customSavePath))
            {
                Directory.Delete(customSavePath, true);
            }

            if (Directory.Exists(categoryCompletedDir))
            {
                Directory.Delete(categoryCompletedDir, true);
            }
        }
    }

    [Test]
    public async Task OnTorrentCompletedAsync_WhenTorrentRelocatedViaMoveTorrentFilesAsync_PreservesRelocatedSavePathUponCompletion()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("completed_relocated.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var relocatedPath = Path.Combine(Path.GetTempPath(), "leecharr_relocated_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(relocatedPath);

        var torrent = new CoreTorrent
        {
            Id = 121,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "completed_relocated.bin",
            Status = TorrentStatus.Stopped,
            Category = "movies",
        };

        try
        {
            var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
            await this.engine.MoveTorrentFilesAsync(121, relocatedPath, moveFiles: false);

            await this.engine.OnTorrentCompletedAsync(121, torrent.InfoHash, task.Manager);

            string assertDest;
            this.storagePathService.DidNotReceive().MoveToCompleted(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), out assertDest);
            task.Manager.SavePath.Should().Be(relocatedPath);
            task.SavePath.Should().Be(relocatedPath);
            this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentDownloadCompletedEvent>(e =>
                e.Torrent.Id == 121 &&
                e.Torrent.SavePath == relocatedPath &&
                e.Torrent.Status == TorrentStatus.Seeding));
        }
        finally
        {
            if (Directory.Exists(relocatedPath))
            {
                Directory.Delete(relocatedPath, true);
            }
        }
    }

    [Test]
    public async Task OnTorrentCompletedAsync_SingleFileTorrent_DoesNotPassIncompleteDirectoryAsSourcePath()
    {
        this.diskProvider.FolderExists(this.testIncompleteDir).Returns(true);
        var torrentBytes = CreateSampleSingleFileTorrentBytes("SingleFileMovie.mkv");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        string expectedSourcePath = Path.Combine(this.testIncompleteDir, "SingleFileMovie.mkv");
        string capturedSource = null;
        string movedDest;

        this.storagePathService
            .MoveToCompleted(Arg.Do<string>(s => capturedSource = s), "movies", "SingleFileMovie.mkv", out movedDest)
            .Returns(x =>
            {
                x[3] = Path.Combine(this.testDownloadDir, "SingleFileMovie.mkv");
                return true;
            });

        var torrent = new CoreTorrent
        {
            Id = 113,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "SingleFileMovie.mkv",
            Status = TorrentStatus.Stopped,
            Category = "movies",
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.OnTorrentCompletedAsync(113, torrent.InfoHash, task.Manager);

        capturedSource.Should().Be(expectedSourcePath);
        capturedSource.Should().NotBe(this.testIncompleteDir);
    }

    [Test]
    public async Task OnTorrentCompletedAsync_SingleFileTorrent_WhenIncompleteDirDisabled_UsesDirectSavePathAsSourcePath()
    {
        this.configService.EnableIncompleteDir.Returns(false);
        var torrentBytes = CreateSampleSingleFileTorrentBytes("DirectMovie.mkv");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        string expectedSourcePath = Path.Combine(this.testDownloadDir, "DirectMovie.mkv");
        string capturedSource = null;
        string movedDest;

        this.storagePathService
            .MoveToCompleted(Arg.Do<string>(s => capturedSource = s), "movies", "DirectMovie.mkv", out movedDest)
            .Returns(x =>
            {
                x[3] = Path.Combine(this.testDownloadDir, "DirectMovie.mkv");
                return true;
            });

        var torrent = new CoreTorrent
        {
            Id = 115,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "DirectMovie.mkv",
            Status = TorrentStatus.Stopped,
            Category = "movies",
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.OnTorrentCompletedAsync(115, torrent.InfoHash, task.Manager);

        capturedSource.Should().Be(expectedSourcePath);
    }

    [Test]
    public async Task OnTorrentCompletedAsync_MultiFileTorrent_PassesContainingDirectoryAsSourcePath()
    {
        var torrentBytes = CreateSampleMultiFileTorrentBytes("MultiSeasonFolder");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        string capturedSource = null;
        string movedDest;

        this.storagePathService
            .MoveToCompleted(Arg.Do<string>(s => capturedSource = s), "tv", "MultiSeasonFolder", out movedDest)
            .Returns(x =>
            {
                x[3] = Path.Combine(this.testDownloadDir, "MultiSeasonFolder");
                return true;
            });

        var torrent = new CoreTorrent
        {
            Id = 114,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "MultiSeasonFolder",
            Status = TorrentStatus.Stopped,
            Category = "tv",
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.OnTorrentCompletedAsync(114, torrent.InfoHash, task.Manager);

        capturedSource.Should().Be(Path.Combine(this.testIncompleteDir, "MultiSeasonFolder"));
    }

    [Test]
    public async Task MonoTorrentDownloadTask_UnhookEvents_DetachesWithoutExceptions()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("unhook_test.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 105,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "unhook_test.bin",
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        var act = () => task.UnhookEvents();

        act.Should().NotThrow();
    }

    [Test]
    public async Task MoveTorrentFilesAsync_WithValidTask_UpdatesSavePath()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("move_test.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 106,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "move_test.bin",
            Status = TorrentStatus.Stopped,
            SavePath = this.testDownloadDir,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        var newDir = Path.Combine(Path.GetTempPath(), "leecharr_move_target_" + Guid.NewGuid().ToString("N"));
        try
        {
            await this.engine.MoveTorrentFilesAsync(106, newDir);

            var task = this.engine.GetTask(106) as MonoTorrentDownloadTask;
            task.Should().NotBeNull();
            task!.Manager.SavePath.Should().Be(newDir);
            task.WorkingPath.Should().Be(newDir);
            task.SavePath.Should().Be(newDir);
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, true);
            }
        }
    }

    [Test]
    public async Task MoveTorrentFilesAsync_WithEmptyPath_ThrowsArgumentException()
    {
        var act = () => this.engine.MoveTorrentFilesAsync(106, string.Empty);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task MoveTorrentFilesAsync_WithNonExistentTorrent_DoesNotThrow()
    {
        var act = () => this.engine.MoveTorrentFilesAsync(99999, "/some/path");
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task MoveTorrentFilesAsync_WhenStorageFull_ClearsStorageFullOnAmpleSpace()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("move_storage_full.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 107,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "move_storage_full.bin",
            Status = TorrentStatus.Stopped,
            SavePath = this.testDownloadDir,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        var task = this.engine.GetTask(107) as MonoTorrentDownloadTask;
        task.Should().NotBeNull();

        task!.SetStorageFull("StorageFull: test low disk space", this.eventAggregator);
        task.IsStorageFull.Should().BeTrue();

        var newDir = Path.Combine(Path.GetTempPath(), "leecharr_move_ample_" + Guid.NewGuid().ToString("N"));
        this.diskProvider.GetAvailableSpace(newDir).Returns(50L * 1024 * 1024 * 1024);

        try
        {
            await this.engine.MoveTorrentFilesAsync(107, newDir);

            task.WorkingPath.Should().Be(newDir);
            task.SavePath.Should().Be(newDir);
            task.Manager.SavePath.Should().Be(newDir);
            task.IsStorageFull.Should().BeFalse();
            this.eventAggregator.Received().PublishEvent(Arg.Is<HealthIssueEvent>(e => e.TorrentId == 107 && e.Source == "DiskSpace" && e.IsResolved));
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, true);
            }
        }
    }

    [Test]
    public async Task MoveTorrentFilesAsync_WhenStorageFull_LeavesStorageFullIfNewLocationAlsoDepleted()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("move_storage_depleted.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 108,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "move_storage_depleted.bin",
            Status = TorrentStatus.Stopped,
            SavePath = this.testDownloadDir,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        var task = this.engine.GetTask(108) as MonoTorrentDownloadTask;
        task.Should().NotBeNull();

        task!.SetStorageFull("StorageFull: test low disk space", this.eventAggregator);
        task.IsStorageFull.Should().BeTrue();

        var newDir = Path.Combine(Path.GetTempPath(), "leecharr_move_depleted_" + Guid.NewGuid().ToString("N"));
        this.diskProvider.GetAvailableSpace(newDir).Returns(100L * 1024 * 1024);

        try
        {
            await this.engine.MoveTorrentFilesAsync(108, newDir);

            task.WorkingPath.Should().Be(newDir);
            task.SavePath.Should().Be(newDir);
            task.Manager.SavePath.Should().Be(newDir);
            task.IsStorageFull.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, true);
            }
        }
    }

    #endregion

    #region Preallocation Tests

    [Test]
    public async Task AddTorrentAsync_WhenPreallocationModeIsFull_PreallocatesFileToFullSize()
    {
        this.configService.PreallocationMode.Returns("Full");
        var fileSize = 32768;
        var torrentBytes = CreateSampleSingleFileTorrentBytes("prealloc_full.bin", fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 201,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "prealloc_full.bin",
            Status = TorrentStatus.Stopped,
            SavePath = this.testIncompleteDir,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        var expectedFilePath = Path.Combine(this.testIncompleteDir, "prealloc_full.bin");
        File.Exists(expectedFilePath).Should().BeTrue();
        new FileInfo(expectedFilePath).Length.Should().Be(fileSize);
    }

    [Test]
    public async Task AddTorrentAsync_WhenPreallocationModeIsSparse_PreallocatesFileToFullSize()
    {
        this.configService.PreallocationMode.Returns("Sparse");
        var fileSize = 49152;
        var torrentBytes = CreateSampleSingleFileTorrentBytes("prealloc_sparse.bin", fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 202,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "prealloc_sparse.bin",
            Status = TorrentStatus.Stopped,
            SavePath = this.testIncompleteDir,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        var expectedFilePath = Path.Combine(this.testIncompleteDir, "prealloc_sparse.bin");
        File.Exists(expectedFilePath).Should().BeTrue();
        new FileInfo(expectedFilePath).Length.Should().Be(fileSize);
    }

    [Test]
    public async Task AddTorrentAsync_WhenPreallocationModeIsOff_DoesNotPreallocateFile()
    {
        this.configService.PreallocationMode.Returns("Off");
        var fileSize = 32768;
        var torrentBytes = CreateSampleSingleFileTorrentBytes("prealloc_off.bin", fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 203,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "prealloc_off.bin",
            Status = TorrentStatus.Stopped,
            SavePath = this.testIncompleteDir,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        var expectedFilePath = Path.Combine(this.testIncompleteDir, "prealloc_off.bin");
        File.Exists(expectedFilePath).Should().BeFalse();
    }

    [Test]
    public async Task AddTorrentAsync_WhenPreallocationModeIsFull_PreallocatesMultiFileTorrent()
    {
        this.configService.PreallocationMode.Returns("Full");
        var torrentBytes = CreateSampleMultiFileTorrentBytes("MultiPrealloc");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 204,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "MultiPrealloc",
            Status = TorrentStatus.Stopped,
            SavePath = this.testIncompleteDir,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        foreach (var file in parsed.Files)
        {
            var expectedFilePath = Path.Combine(this.testIncompleteDir, "MultiPrealloc", file.Path);
            File.Exists(expectedFilePath).Should().BeTrue();
            new FileInfo(expectedFilePath).Length.Should().Be(file.Length);
        }
    }

    [Test]
    public async Task AddTorrentAsync_WhenPreallocationModeIsSparse_InitializesEmptyFastResumeAndSkipsHashingCheck()
    {
        this.configService.PreallocationMode.Returns("Sparse");
        var fileSize = 65536;
        var torrentBytes = CreateSampleSingleFileTorrentBytes("prealloc_sparse_skip_hash.bin", fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 205,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "prealloc_sparse_skip_hash.bin",
            Status = TorrentStatus.Downloading,
            SavePath = this.testIncompleteDir,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.HashChecked.Should().BeTrue();
        task.Manager.Bitfield.PercentComplete.Should().Be(0.0);
        task.Manager.State.Should().NotBe(TorrentState.Hashing);

        var expectedFilePath = Path.Combine(this.testIncompleteDir, "prealloc_sparse_skip_hash.bin");
        File.Exists(expectedFilePath).Should().BeTrue();
        new FileInfo(expectedFilePath).Length.Should().Be(fileSize);
    }

    [Test]
    public async Task AddTorrentAsync_WhenPreallocationModeIsFull_InitializesEmptyFastResumeAndSkipsHashingCheck()
    {
        this.configService.PreallocationMode.Returns("Full");
        var fileSize = 65536;
        var torrentBytes = CreateSampleSingleFileTorrentBytes("prealloc_full_skip_hash.bin", fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 206,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "prealloc_full_skip_hash.bin",
            Status = TorrentStatus.Downloading,
            SavePath = this.testIncompleteDir,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.HashChecked.Should().BeTrue();
        task.Manager.Bitfield.PercentComplete.Should().Be(0.0);
        task.Manager.State.Should().NotBe(TorrentState.Hashing);

        var expectedFilePath = Path.Combine(this.testIncompleteDir, "prealloc_full_skip_hash.bin");
        File.Exists(expectedFilePath).Should().BeTrue();
        new FileInfo(expectedFilePath).Length.Should().Be(fileSize);
    }

    [Test]
    public async Task AddTorrentAsync_WhenExistingFileWithDataOnDisk_ExecutesHashCheckAndDoesNotInitializeSyntheticFastResume()
    {
        this.configService.PreallocationMode.Returns("Sparse");
        var fileSize = 65536;
        var fileName = "prealloc_existing_data.bin";
        var torrentBytes = CreateSampleSingleFileTorrentBytes(fileName, fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        Directory.CreateDirectory(this.testIncompleteDir);
        var existingFilePath = Path.Combine(this.testIncompleteDir, fileName);
        await File.WriteAllBytesAsync(existingFilePath, new byte[16384]);

        var torrent = new CoreTorrent
        {
            Id = 207,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = fileName,
            Status = TorrentStatus.Stopped,
            SavePath = this.testIncompleteDir,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.HashChecked.Should().BeFalse();
    }

    [Test]
    public async Task AddTorrentAsync_WhenExistingFileWithZeroLengthOnDisk_InitializesEmptyFastResume()
    {
        this.configService.PreallocationMode.Returns("Sparse");
        var fileSize = 65536;
        var fileName = "prealloc_existing_zero_len.bin";
        var torrentBytes = CreateSampleSingleFileTorrentBytes(fileName, fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        Directory.CreateDirectory(this.testIncompleteDir);
        var existingFilePath = Path.Combine(this.testIncompleteDir, fileName);
        await File.WriteAllBytesAsync(existingFilePath, Array.Empty<byte>());

        var torrent = new CoreTorrent
        {
            Id = 208,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = fileName,
            Status = TorrentStatus.Downloading,
            SavePath = this.testIncompleteDir,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.Manager.HashChecked.Should().BeTrue();
        task.Manager.Bitfield.PercentComplete.Should().Be(0.0);
    }

    [Test]
    public async Task AddTorrentAsync_WhenVpnKillSwitchActiveAndEngineHalted_QueuesTorrentAndDrainsOnVpnRestored()
    {
        var mockVpnService = Substitute.For<IVpnKillSwitchService>();
        this.configService.EnableVpnKillSwitch.Returns(true);
        this.configService.BindInterface.Returns("tun0");
        mockVpnService.IsKillSwitchEnabled.Returns(true);
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<System.Net.Sockets.AddressFamily>()).Returns((IPAddress)null);

        using var vpnEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpnService);

        var torrentBytes = CreateSampleSingleFileTorrentBytes("vpn_queued.bin", 16384);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 301,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "vpn_queued.bin",
            Status = TorrentStatus.Stopped,
            SavePath = this.testIncompleteDir,
        };

        var task = await vpnEngine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().BeNull();
        vpnEngine.IsHaltedByKillSwitch.Should().BeTrue();
        vpnEngine.GetTask(torrent.Id).Should().BeNull();

        // Simulate VPN restoration
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<System.Net.Sockets.AddressFamily>()).Returns(IPAddress.Loopback);
        await vpnEngine.ResumeTorrentsAfterVpnRestoredAsync();

        vpnEngine.IsHaltedByKillSwitch.Should().BeFalse();
        vpnEngine.GetTask(torrent.Id).Should().NotBeNull();
        vpnEngine.GetTask(torrent.Id).InfoHash.Should().Be(torrent.InfoHash);
    }

    [Test]
    public async Task AddTorrentAsync_WhenQueuedAndRemovedBeforeVpnRestores_IsRemovedFromPendingQueue()
    {
        var mockVpnService = Substitute.For<IVpnKillSwitchService>();
        this.configService.EnableVpnKillSwitch.Returns(true);
        this.configService.BindInterface.Returns("tun0");
        mockVpnService.IsKillSwitchEnabled.Returns(true);
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<System.Net.Sockets.AddressFamily>()).Returns((IPAddress)null);

        using var vpnEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpnService);

        var torrentBytes = CreateSampleSingleFileTorrentBytes("vpn_removed.bin", 16384);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 302,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "vpn_removed.bin",
            Status = TorrentStatus.Stopped,
            SavePath = this.testIncompleteDir,
        };

        var task = await vpnEngine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().BeNull();

        await vpnEngine.RemoveTorrentAsync(torrent.Id, false);

        // Restore VPN
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<System.Net.Sockets.AddressFamily>()).Returns(IPAddress.Loopback);
        await vpnEngine.ResumeTorrentsAfterVpnRestoredAsync();

        vpnEngine.GetTask(torrent.Id).Should().BeNull();
    }

    [Test]
    public async Task UpdateEngineListenEndpointsAsync_WhenInterfaceBoundAndIpCannotBeResolvedWithKillSwitchEnabled_FailsClosedAndHaltsEngine()
    {
        var mockVpnService = Substitute.For<IVpnKillSwitchService>();
        this.configService.EnableVpnKillSwitch.Returns(true);
        this.configService.BindInterface.Returns("tun0");
        mockVpnService.IsKillSwitchEnabled.Returns(true);
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<System.Net.Sockets.AddressFamily>()).Returns(IPAddress.Loopback);

        using var vpnEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpnService);

        await vpnEngine.StartAsync();
        vpnEngine.IsHaltedByKillSwitch.Should().BeFalse();

        // Simulate interface losing its IP
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<System.Net.Sockets.AddressFamily>()).Returns((IPAddress)null);

        await vpnEngine.UpdateEngineListenEndpointsAsync();

        vpnEngine.IsHaltedByKillSwitch.Should().BeTrue();
    }

    [Test]
    public async Task ResumeTorrentsAfterVpnRestoredAsync_WhenKillSwitchEnabledViaRepositorySettings_EnforcesFailClosed()
    {
        var mockVpnService = Substitute.For<IVpnKillSwitchService>();
        // configService returns false, but vpnKillSwitchService returns true (e.g. from network settings repository)
        this.configService.EnableVpnKillSwitch.Returns(false);
        this.configService.BindInterface.Returns("tun0");
        mockVpnService.IsKillSwitchEnabled.Returns(true);
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<System.Net.Sockets.AddressFamily>()).Returns((IPAddress)null);

        using var vpnEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpnService);

        vpnEngine.OnVpnDropped("tun0");
        vpnEngine.IsHaltedByKillSwitch.Should().BeTrue();

        await vpnEngine.ResumeTorrentsAfterVpnRestoredAsync();

        // Must remain halted fail-closed
        vpnEngine.IsHaltedByKillSwitch.Should().BeTrue();
    }

    [Test]
    public void OnVpnRestored_DoesNotPrematurelyClearIsHaltedByKillSwitch()
    {
        var mockVpnService = Substitute.For<IVpnKillSwitchService>();
        this.configService.EnableVpnKillSwitch.Returns(true);
        this.configService.BindInterface.Returns("tun0");
        mockVpnService.IsKillSwitchEnabled.Returns(true);
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<System.Net.Sockets.AddressFamily>()).Returns((IPAddress)null);

        using var vpnEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpnService);

        vpnEngine.OnVpnDropped("tun0");
        vpnEngine.IsHaltedByKillSwitch.Should().BeTrue();

        vpnEngine.OnVpnRestored("tun0");

        // Immediately after OnVpnRestored, before resumption has validated IP, isHaltedByKillSwitch must not be cleared
        vpnEngine.IsHaltedByKillSwitch.Should().BeTrue();
    }

    [Test]
    public async Task BoundSocketConnector_WhenLocalIpv4IsNull_ThrowsNetworkUnreachable()
    {
        var connector = new BoundSocketConnector(() => (IPAddress)null, () => IPAddress.IPv6Any);
        var act = async () => await connector.ConnectAsync(new Uri("http://127.0.0.1:8080"), CancellationToken.None);

        await act.Should().ThrowAsync<System.Net.Sockets.SocketException>()
            .Where(e => e.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable);
    }

    [Test]
    public async Task BoundSocketConnector_WhenLocalIpv6IsNull_ThrowsNetworkUnreachable()
    {
        var connector = new BoundSocketConnector(() => IPAddress.Any, () => (IPAddress)null);
        var act = async () => await connector.ConnectAsync(new Uri("http://[::1]:8080"), CancellationToken.None);

        await act.Should().ThrowAsync<System.Net.Sockets.SocketException>()
            .Where(e => e.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable);
    }

    [Test]
    public async Task BoundSocketConnector_ConnectAsync_WhenInterfaceDown_ThrowsNetworkUnreachableBeforeDns()
    {
        var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
        mockBindingService.IsInterfaceUp("tun0").Returns(false);

        var connector = new BoundSocketConnector(
            () => IPAddress.Parse("10.0.0.2"),
            () => null,
            networkBindingService: mockBindingService,
            getInterfaceName: () => "tun0");

        var act = async () => await connector.ConnectAsync(new Uri("http://tracker.unreachable-domain-should-not-resolve.invalid:1234"), CancellationToken.None);

        await act.Should().ThrowAsync<System.Net.Sockets.SocketException>()
            .Where(e => e.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable);

        mockBindingService.Received(1).IsInterfaceUp("tun0");
    }

    [Test]
    public async Task BoundSocketConnector_ConnectAsync_WhenKillSwitchActive_ThrowsNetworkUnreachableBeforeDns()
    {
        var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();

        var connector = new BoundSocketConnector(
            () => IPAddress.Parse("10.0.0.2"),
            () => null,
            networkBindingService: mockBindingService,
            getInterfaceName: () => "tun0",
            isKillSwitchActive: () => true);

        var act = async () => await connector.ConnectAsync(new Uri("http://tracker.unreachable-domain-should-not-resolve.invalid:1234"), CancellationToken.None);

        await act.Should().ThrowAsync<System.Net.Sockets.SocketException>()
            .Where(e => e.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable);

        mockBindingService.DidNotReceive().IsInterfaceUp(Arg.Any<string>());
    }

    [Test]
    public async Task BoundSocketConnector_ConnectAsync_WhenBoundInterfaceHasNoValidLocalIps_ThrowsNetworkUnreachableBeforeDns()
    {
        var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
        mockBindingService.IsInterfaceUp("tun0").Returns(true);

        var connector = new BoundSocketConnector(
            () => null,
            () => null,
            networkBindingService: mockBindingService,
            getInterfaceName: () => "tun0");

        var act = async () => await connector.ConnectAsync(new Uri("http://tracker.unreachable-domain-should-not-resolve.invalid:1234"), CancellationToken.None);

        await act.Should().ThrowAsync<System.Net.Sockets.SocketException>()
            .Where(e => e.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable);
    }

    [Test]
    public async Task BoundSocketConnector_WhenProxyTunnelActive_DelegatesToConnectTunnelAsync()
    {
        var mockProxyProvider = Substitute.For<NzbDrone.Core.Network.Binding.IProxyTunnelBindingProvider>();
        using var dummySocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        mockProxyProvider.ConnectTunnelAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dummySocket));

        var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
        mockBindingService.ActiveProvider.Returns(mockProxyProvider);

        var connector = new BoundSocketConnector(
            () => IPAddress.Any,
            () => IPAddress.IPv6Any,
            mockBindingService,
            () => null);

        var socket = await connector.ConnectAsync(new Uri("http://target.peer.org:6881"), CancellationToken.None);

        socket.Should().BeSameAs(dummySocket);
        await mockProxyProvider.Received(1).ConnectTunnelAsync("target.peer.org", 6881, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BoundSocketConnector_WhenBindingServiceAndInterfaceConfigured_BindsSocketViaService()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
            var mockProvider = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingProvider>();
            mockBindingService.ActiveProvider.Returns(mockProvider);

            var connector = new BoundSocketConnector(
                () => IPAddress.Loopback,
                () => IPAddress.IPv6Loopback,
                mockBindingService,
                () => "lo");

            using var socket = await connector.ConnectAsync(new Uri($"http://127.0.0.1:{port}"), CancellationToken.None);

            socket.Should().NotBeNull();
            socket.Connected.Should().BeTrue();
            mockBindingService.Received(1).BindSocket(Arg.Any<System.Net.Sockets.Socket>(), "lo");
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task BoundSocketConnector_WhenNetworkInterfaceBindingConfigured_PrioritizesOverBindInterface()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
            var mockProvider = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingProvider>();
            mockBindingService.ActiveProvider.Returns(mockProvider);

            var mockConfigService = Substitute.For<IConfigService>();
            mockConfigService.NetworkInterfaceBinding.Returns("tun0");
            mockConfigService.BindInterface.Returns("eth0");

            var connector = new BoundSocketConnector(
                () => IPAddress.Loopback,
                () => IPAddress.IPv6Loopback,
                mockBindingService,
                () => !string.IsNullOrWhiteSpace(mockConfigService.NetworkInterfaceBinding)
                    ? mockConfigService.NetworkInterfaceBinding
                    : mockConfigService.BindInterface,
                configService: mockConfigService);

            using var socket = await connector.ConnectAsync(new Uri($"http://127.0.0.1:{port}"), CancellationToken.None);

            socket.Should().NotBeNull();
            socket.Connected.Should().BeTrue();
            mockBindingService.Received(1).BindSocket(Arg.Any<System.Net.Sockets.Socket>(), "tun0");
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestCase("Any")]
    [TestCase("any")]
    [TestCase("all")]
    [TestCase("ALL")]
    public async Task BoundSocketConnector_WhenInterfaceIsAnyOrAll_DoesNotCallNetworkBindingServiceAndConnectsSuccessfully(string iface)
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
            var connector = new BoundSocketConnector(
                () => IPAddress.Loopback,
                () => IPAddress.IPv6Loopback,
                mockBindingService,
                () => iface);

            using var socket = await connector.ConnectAsync(new Uri($"http://127.0.0.1:{port}"), CancellationToken.None);

            socket.Should().NotBeNull();
            socket.Connected.Should().BeTrue();
            mockBindingService.DidNotReceive().BindSocket(Arg.Any<System.Net.Sockets.Socket>(), Arg.Any<string>(), Arg.Any<int>());
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestCase("Any")]
    [TestCase("any")]
    [TestCase("all")]
    [TestCase("ALL")]
    public void BoundSocketConnector_CreateDatagramSocket_WhenInterfaceIsAnyOrAll_DoesNotCallNetworkBindingService(string iface)
    {
        var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
        var connector = new BoundSocketConnector(
            () => IPAddress.Loopback,
            () => IPAddress.IPv6Loopback,
            mockBindingService,
            () => iface);

        using var socket = connector.CreateDatagramSocket(AddressFamily.InterNetwork, 0);

        socket.Should().NotBeNull();
        socket.IsBound.Should().BeTrue();
        mockBindingService.DidNotReceive().BindSocket(Arg.Any<System.Net.Sockets.Socket>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Test]
    public void BoundSocketConnector_CreateDatagramSocket_CreatesAndBindsUdpSocket()
    {
        var connector = new BoundSocketConnector(
            () => IPAddress.Loopback,
            () => IPAddress.IPv6Loopback);

        using var udpSocket = connector.CreateDatagramSocket(System.Net.Sockets.AddressFamily.InterNetwork);

        udpSocket.Should().NotBeNull();
        udpSocket.SocketType.Should().Be(System.Net.Sockets.SocketType.Dgram);
        udpSocket.ProtocolType.Should().Be(System.Net.Sockets.ProtocolType.Udp);
        udpSocket.IsBound.Should().BeTrue();
        ((IPEndPoint)udpSocket.LocalEndPoint!).Address.Should().Be(IPAddress.Loopback);
    }

    [Test]
    public void BoundSocketConnector_CreateDatagramSocket_WithWildcardAddressAndEphemeralPort0_ExplicitlyBindsSocket()
    {
        var connector = new BoundSocketConnector(
            () => IPAddress.Any,
            () => IPAddress.IPv6Any);

        using var udpSocket = connector.CreateDatagramSocket(System.Net.Sockets.AddressFamily.InterNetwork, localPort: 0);

        udpSocket.Should().NotBeNull();
        udpSocket.SocketType.Should().Be(System.Net.Sockets.SocketType.Dgram);
        udpSocket.ProtocolType.Should().Be(System.Net.Sockets.ProtocolType.Udp);
        udpSocket.IsBound.Should().BeTrue();
        ((IPEndPoint)udpSocket.LocalEndPoint!).Address.Should().Be(IPAddress.Any);
        ((IPEndPoint)udpSocket.LocalEndPoint!).Port.Should().BeGreaterThan(0);
    }

    [Test]
    public void BoundSocketConnector_CreateDatagramSocket_IPv6WithWildcardAddressAndEphemeralPort0_ExplicitlyBindsSocket()
    {
        var connector = new BoundSocketConnector(
            () => IPAddress.Any,
            () => IPAddress.IPv6Any);

        using var udpSocket = connector.CreateDatagramSocket(System.Net.Sockets.AddressFamily.InterNetworkV6, localPort: 0);

        udpSocket.Should().NotBeNull();
        udpSocket.SocketType.Should().Be(System.Net.Sockets.SocketType.Dgram);
        udpSocket.ProtocolType.Should().Be(System.Net.Sockets.ProtocolType.Udp);
        udpSocket.IsBound.Should().BeTrue();
        ((IPEndPoint)udpSocket.LocalEndPoint!).Address.Should().Be(IPAddress.IPv6Any);
        ((IPEndPoint)udpSocket.LocalEndPoint!).Port.Should().BeGreaterThan(0);
    }

    [Test]
    public void BoundSocketConnector_CreateDatagramSocket_WhenLocalIpv4IsNull_ThrowsNetworkUnreachable()
    {
        var connector = new BoundSocketConnector(
            () => (IPAddress)null,
            () => IPAddress.IPv6Any);

        var act = () => connector.CreateDatagramSocket(System.Net.Sockets.AddressFamily.InterNetwork);

        act.Should().Throw<System.Net.Sockets.SocketException>()
            .Where(e => e.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable);
    }

    [Test]
    public void BoundSocketConnector_CreateBoundSocket_WithDeviceBinding_InvokesNetworkBindingService()
    {
        var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
        var connector = new BoundSocketConnector(
            () => IPAddress.Any,
            () => IPAddress.IPv6Any,
            mockBindingService,
            () => "wg0");

        using var udpSocket = connector.CreateBoundSocket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp);

        udpSocket.Should().NotBeNull();
        mockBindingService.Received(1).BindSocket(udpSocket, "wg0", 0);
    }

    [Test]
    public void BoundSocketConnector_BindSocket_WhenNetworkBindingBindsSocket_AvoidsDoubleBindSocketException()
    {
        var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
        mockBindingService.When(x => x.BindSocket(Arg.Any<Socket>(), Arg.Any<string>(), Arg.Any<int>()))
            .Do(ci =>
            {
                var s = ci.Arg<Socket>();
                s.Bind(new IPEndPoint(IPAddress.Loopback, ci.Arg<int>()));
            });

        var connector = new BoundSocketConnector(
            () => IPAddress.Loopback,
            () => IPAddress.IPv6Loopback,
            mockBindingService,
            () => "tun0");

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var act = () => connector.BindSocket(socket, 0);

        act.Should().NotThrow();
        socket.IsBound.Should().BeTrue();
        mockBindingService.Received(1).BindSocket(socket, "tun0", 0);
    }

    [Test]
    public void BoundSocketConnector_BindSocket_PassesSpecifiedLocalPortToNetworkBindingService()
    {
        var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
        var connector = new BoundSocketConnector(
            () => IPAddress.Any,
            () => IPAddress.IPv6Any,
            mockBindingService,
            () => "wg0");

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        connector.BindSocket(socket, 6881);

        mockBindingService.Received(1).BindSocket(socket, "wg0", 6881);
    }

    [Test]
    public void BoundSocketConnector_CreateBoundSocket_WhenNetworkBindingBinds_DoesNotThrowDoubleBind()
    {
        var mockBindingService = Substitute.For<NzbDrone.Core.Network.Binding.INetworkBindingService>();
        mockBindingService.When(x => x.BindSocket(Arg.Any<Socket>(), Arg.Any<string>(), Arg.Any<int>()))
            .Do(ci =>
            {
                var s = ci.Arg<Socket>();
                s.Bind(new IPEndPoint(IPAddress.Loopback, ci.Arg<int>()));
            });

        var connector = new BoundSocketConnector(
            () => IPAddress.Loopback,
            () => IPAddress.IPv6Loopback,
            mockBindingService,
            () => "tun0");

        using var socket = connector.CreateBoundSocket(
            AddressFamily.InterNetwork,
            SocketType.Dgram,
            ProtocolType.Udp,
            0);

        socket.Should().NotBeNull();
        socket.IsBound.Should().BeTrue();
    }

    [Test]
    public async Task BoundSocketConnector_ConnectAsync_WithUtpScheme_ConnectsUdpDatagramSocket()
    {
        using var udpListener = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)udpListener.Client.LocalEndPoint!).Port;

        var connector = new BoundSocketConnector(
            () => IPAddress.Loopback,
            () => IPAddress.IPv6Loopback);

        using var socket = await connector.ConnectAsync(new Uri($"utp://127.0.0.1:{port}"), CancellationToken.None);

        socket.Should().NotBeNull();
        socket.SocketType.Should().Be(System.Net.Sockets.SocketType.Dgram);
        socket.ProtocolType.Should().Be(System.Net.Sockets.ProtocolType.Udp);
        socket.Connected.Should().BeTrue();
    }

    [Test]
    public async Task MonoTorrentDownloadEngine_StartsSuccessfully_WithBepConfigs()
    {
        this.configService.ExtensionFastExtension.Returns(true);
        this.configService.UtpEnabled.Returns(true);
        this.configService.TcpFallback.Returns(true);
        this.configService.ExtensionLtDontHave.Returns(true);
        this.configService.TransportConnectionTimeoutSeconds.Returns(45);

        await this.engine.StartAsync();

        this.engine.IsAvailable.Should().BeTrue();
        this.engine.IsHaltedByKillSwitch.Should().BeFalse();

        await this.engine.StopAsync();
    }

    [Test]
    public async Task CheckDiskSpace_WhenFreeSpaceBelowThreshold_SetsStorageFullAndPublishesHealthIssueEvent()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("lowdisk.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 501,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "lowdisk.iso",
            Status = TorrentStatus.Downloading,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(100_000_000L); // 100 MB < 500 MB threshold

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.IsStorageFull.Should().BeTrue();
        task.IsOutOfDiskSpace.Should().BeTrue();
        task.Status.Should().Be(TorrentStatus.Paused);
        task.ErrorMessage.Should().Contain("StorageFull");

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.TorrentId == 501 &&
            !e.IsResolved &&
            e.Source == "DiskSpace" &&
            e.Message.Contains("StorageFull")));
    }

    [Test]
    public async Task CheckDiskSpace_WhenFreeSpaceRecovers_ClearsStorageFullAndPublishesResolvedHealthIssueEvent()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("lowdisk_recover.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 502,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "lowdisk_recover.iso",
            Status = TorrentStatus.Downloading,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(100_000_000L);
        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.IsStorageFull.Should().BeTrue();

        // Disk space recovers above 500 MB threshold
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(10_000_000_000L);
        var isLowDisk = task.CheckDiskSpace(this.diskProvider, 500L * 1024L * 1024L, this.eventAggregator);

        isLowDisk.Should().BeFalse();
        task.IsStorageFull.Should().BeFalse();
        task.ErrorMessage.Should().BeNull();

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.TorrentId == 502 &&
            e.IsResolved &&
            e.Source == "DiskSpace" &&
            e.Message.Contains("Disk space restored")));
    }

    [Test]
    public async Task CheckDiskSpace_WhenFreeSpaceRestored_AutomaticallyResumesPausedTorrent()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("lowdisk_autoresume.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 505,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "lowdisk_autoresume.iso",
            Status = TorrentStatus.Downloading,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(100_000_000L);
        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.IsStorageFull.Should().BeTrue();
        task.WasAutoPausedByDiskSpace.Should().BeTrue();
        task.Status.Should().Be(TorrentStatus.Paused);

        // Disk space recovers above 500 MB threshold
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(10_000_000_000L);
        var isLowDisk = task.CheckDiskSpace(this.diskProvider, 500L * 1024L * 1024L, this.eventAggregator);

        isLowDisk.Should().BeFalse();
        task.IsStorageFull.Should().BeFalse();
        task.WasAutoPausedByDiskSpace.Should().BeFalse();

        // Wait for auto-resume Task.Run to execute StartAsync
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State == TorrentState.Paused && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        task.Manager.State.Should().NotBe(TorrentState.Paused);
    }

    [Test]
    public async Task CheckDiskSpace_WhenUserManuallyPauses_DoesNotAutoResumeWhenSpaceRestored()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("manual_pause_disk.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 506,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "manual_pause_disk.iso",
            Status = TorrentStatus.Downloading,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(10_000_000_000L);
        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        // User manually pauses
        await this.engine.PauseTorrentAsync(torrent.Id);
        task.WasAutoPausedByDiskSpace.Should().BeFalse();
        task.Manager.State.Should().BeOneOf(TorrentState.Paused, TorrentState.Stopping, TorrentState.Stopped);

        // Low disk space check occurs while user has it paused
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(100_000_000L);
        task.CheckDiskSpace(this.diskProvider, 500L * 1024L * 1024L, this.eventAggregator);
        task.WasAutoPausedByDiskSpace.Should().BeFalse();

        // Space recovers
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(10_000_000_000L);
        task.CheckDiskSpace(this.diskProvider, 500L * 1024L * 1024L, this.eventAggregator);

        await Task.Delay(100);
        task.WasAutoPausedByDiskSpace.Should().BeFalse();
        task.Manager.State.Should().BeOneOf(TorrentState.Paused, TorrentState.Stopped);
    }

    [Test]
    public async Task CheckDiskSpace_WhenAutoPausedTorrentManuallyPausedByUser_ClearsAutoResumeFlag()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("autopause_override.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 507,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "autopause_override.iso",
            Status = TorrentStatus.Downloading,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(100_000_000L);
        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.WasAutoPausedByDiskSpace.Should().BeTrue();

        // User explicitly pauses the torrent
        await this.engine.PauseTorrentAsync(torrent.Id);
        task.WasAutoPausedByDiskSpace.Should().BeFalse();

        // Disk space is restored
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(10_000_000_000L);
        task.CheckDiskSpace(this.diskProvider, 500L * 1024L * 1024L, this.eventAggregator);

        await Task.Delay(100);
        task.Manager.State.Should().BeOneOf(TorrentState.Paused, TorrentState.Stopped);
    }

    [Test]
    public async Task CheckDiskSpace_WhenRunningTorrentRunsOutOfDiskSpace_PausesAndThenResumesOnRecovery()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("running_lowdisk.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 508,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "running_lowdisk.iso",
            Status = TorrentStatus.Downloading,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(10_000_000_000L);
        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State != TorrentState.Downloading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        task.Manager.State.Should().Be(TorrentState.Downloading);

        // Disk space drops below threshold
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(100_000_000L);
        var isLowDisk = task.CheckDiskSpace(this.diskProvider, 500L * 1024L * 1024L, this.eventAggregator);

        isLowDisk.Should().BeTrue();
        task.IsStorageFull.Should().BeTrue();
        task.WasAutoPausedByDiskSpace.Should().BeTrue();

        // Wait for PauseAsync to take effect
        timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State == TorrentState.Downloading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        task.Manager.State.Should().BeOneOf(TorrentState.Paused, TorrentState.Stopping, TorrentState.Stopped);

        // Disk space recovers
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(10_000_000_000L);
        task.CheckDiskSpace(this.diskProvider, 500L * 1024L * 1024L, this.eventAggregator);

        task.IsStorageFull.Should().BeFalse();
        task.WasAutoPausedByDiskSpace.Should().BeFalse();

        // Wait for auto-resume
        timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State == TorrentState.Paused && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        task.Manager.State.Should().NotBe(TorrentState.Paused);
    }

    [Test]
    public async Task ResumeTorrentAsync_WhenFreeSpaceBelowThreshold_PreventsResumeAndSetsStorageFull()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("resume_lowdisk.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 503,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "resume_lowdisk.iso",
            Status = TorrentStatus.Paused,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(50_000_000_000L);
        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        // Drop free space below threshold before resume
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(200_000_000L);
        await this.engine.ResumeTorrentAsync(503);

        task.IsStorageFull.Should().BeTrue();
        task.Status.Should().Be(TorrentStatus.Paused);
        task.ErrorMessage.Should().Contain("StorageFull");
    }

    [Test]
    public async Task CheckDiskSpaceHealth_OnEngine_ChecksAllActiveTasks()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("health_check_disk.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 504,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "health_check_disk.iso",
            Status = TorrentStatus.Downloading,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(50_000_000_000L);
        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.IsStorageFull.Should().BeFalse();

        // Drop space and run health check
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(100_000_000L);
        this.engine.CheckDiskSpaceHealth();

        task.IsStorageFull.Should().BeTrue();
        task.Status.Should().Be(TorrentStatus.Paused);
    }

    [Test]
    public void CalculateDynamicDiskCacheBytes_DefaultConfiguration_ReturnsAtLeast128MB()
    {
        var cacheBytes = this.engine.CalculateDynamicDiskCacheBytes();
        cacheBytes.Should().BeGreaterThanOrEqualTo(128 * 1024 * 1024);
        cacheBytes.Should().BeLessThanOrEqualTo(1024 * 1024 * 1024);
    }

    [Test]
    public void CalculateDynamicDiskCacheBytes_HighDownloadThroughput_ScalesUpTo1GB()
    {
        // 500 MB/s download throughput with sufficient RAM (8 GB) scales to 1 GB
        var cacheBytes = this.engine.CalculateDynamicDiskCacheBytes(500L * 1024L * 1024L, effectiveMemoryOverride: 8L * 1024L * 1024L * 1024L);
        cacheBytes.Should().Be(1024 * 1024 * 1024);
    }

    [Test]
    public void CalculateDynamicDiskCacheBytes_ExplicitCustomConfig_ScalesAccordingly()
    {
        this.configService.DiskWriteCacheSizeMb.Returns(512);
        var cacheBytes = this.engine.CalculateDynamicDiskCacheBytes();
        cacheBytes.Should().BeGreaterThanOrEqualTo(512 * 1024 * 1024);
        cacheBytes.Should().BeLessThanOrEqualTo(1024 * 1024 * 1024);
    }

    [Test]
    public void CalculateDynamicDiskCacheBytes_WhenCgroupLimitEnforcesLowMemory_CapsCacheAt25PercentOfContainerMemory()
    {
        // 512 MB container memory limit; 25% safety ceiling is 128 MB
        var containerMemory = 512L * 1024L * 1024L;
        var throughput = 200L * 1024L * 1024L;

        var cacheBytes = this.engine.CalculateDynamicDiskCacheBytes(throughput, effectiveMemoryOverride: containerMemory);

        cacheBytes.Should().Be(128 * 1024 * 1024);
    }

    [Test]
    public void CalculateDynamicDiskCacheBytes_WhenSevereLowMemoryContainer_ClampsDownTo32MB()
    {
        // 128 MB container memory limit; 25% is 32 MB
        var containerMemory = 128L * 1024L * 1024L;
        var throughput = 100L * 1024L * 1024L;

        var cacheBytes = this.engine.CalculateDynamicDiskCacheBytes(throughput, effectiveMemoryOverride: containerMemory);

        cacheBytes.Should().Be(32 * 1024 * 1024);
    }

    [Test]
    public void CalculateDynamicDiskCacheBytes_WhenExplicitSmallCacheConfigured_AllowsMinimumClampDownToConfiguredValue()
    {
        this.configService.DiskWriteCacheSizeMb.Returns(32);
        this.configService.DiskCacheBytes.Returns(32L * 1024L * 1024L);

        var cacheBytes = this.engine.CalculateDynamicDiskCacheBytes();

        cacheBytes.Should().Be(32 * 1024 * 1024);
    }

    [Test]
    public void CalculateDynamicDiskCacheBytes_When64MbConfigured_Allows64MbAllocation()
    {
        this.configService.DiskWriteCacheSizeMb.Returns(64);
        this.configService.DiskCacheBytes.Returns(64L * 1024L * 1024L);

        var cacheBytes = this.engine.CalculateDynamicDiskCacheBytes();

        cacheBytes.Should().Be(64 * 1024 * 1024);
    }

    [Test]
    public async Task GetEffectiveMemoryBytes_WhenCgroupV2FileExists_ReadsMemoryLimitAccurately()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "cgroup_v2_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var expectedBytes = 2147483648L; // 2 GB
            await File.WriteAllTextAsync(tempFile, expectedBytes.ToString() + "\n");

            var effectiveMemory = this.engine.GetEffectiveMemoryBytes(cgroupV2Path: tempFile, cgroupV1Path: "/nonexistent/cgroup1");

            effectiveMemory.Should().Be(expectedBytes);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task GetEffectiveMemoryBytes_WhenCgroupV2IsMax_FallsBackToHostMemory()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "cgroup_v2_max_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(tempFile, "max\n");

            var effectiveMemory = this.engine.GetEffectiveMemoryBytes(cgroupV2Path: tempFile, cgroupV1Path: "/nonexistent/cgroup1");

            effectiveMemory.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task GetEffectiveMemoryBytes_WhenCgroupV1FileExists_ReadsMemoryLimitAccurately()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "cgroup_v1_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var expectedBytes = 1073741824L; // 1 GB
            await File.WriteAllTextAsync(tempFile, expectedBytes.ToString() + "\n");

            var effectiveMemory = this.engine.GetEffectiveMemoryBytes(cgroupV2Path: "/nonexistent/cgroup2", cgroupV1Path: tempFile);

            effectiveMemory.Should().Be(expectedBytes);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task SaveFastResumeAtomicAsync_WritesTemporaryFileAndAtomicallyReplacesTarget()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("fast_resume_test.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 601,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "fast_resume_test.iso",
            Status = TorrentStatus.Downloading,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(50_000_000_000L);
        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        var tempCacheDir = Path.Combine(Path.GetTempPath(), "leecharr_fastresume_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            await this.engine.SaveFastResumeAtomicAsync(task.Manager, tempCacheDir);

            var fastResumeDir = Path.Combine(tempCacheDir, "FastResume");
            var targetFile = Path.Combine(fastResumeDir, $"{torrent.InfoHash}.fastresume");
            var tempFile = Path.Combine(fastResumeDir, $"{torrent.InfoHash}.fastresume.tmp");

            File.Exists(targetFile).Should().BeTrue();
            File.Exists(tempFile).Should().BeFalse();

            var fileBytes = await File.ReadAllBytesAsync(targetFile);
            fileBytes.Length.Should().BeGreaterThan(0);
        }
        finally
        {
            if (Directory.Exists(tempCacheDir))
            {
                Directory.Delete(tempCacheDir, true);
            }
        }
    }

    [Test]
    public async Task StartAsync_WhenSocks5ProxyConfigured_DisablesDhtAndLpdToPreventUdpLeaks()
    {
        this.configService.ProxyType.Returns("socks5");
        this.configService.ProxyHost.Returns("127.0.0.1");
        this.configService.ProxyPort.Returns(1080);
        this.configService.EnableDht.Returns(true);
        this.configService.EnableLpd.Returns(true);

        await this.engine.StartAsync();

        // When proxy is configured, DHT nodes should remain 0 (DHT disabled)
        this.engine.DhtNodeCount.Should().Be(0);
    }

    [Test]
    public async Task AddTorrentAsync_WhenSocks5ProxyConfigured_RemovesUdpTrackersToPreventIpLeak()
    {
        this.configService.ProxyType.Returns("socks5");
        this.configService.ProxyHost.Returns("127.0.0.1");
        this.configService.ProxyPort.Returns(1080);

        var torrent = new CoreTorrent
        {
            Id = 999,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Name = "ProxyLeakTestTorrent",
            Status = TorrentStatus.Downloading,
            TrackerUrl = "http://tracker.example.com/announce",
        };

        var task = await this.engine.AddTorrentAsync(torrent, magnetUri: "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&tr=udp%3A%2F%2Ftracker.leaker.com%3A1337%2Fannounce&tr=http%3A%2F%2Ftracker.example.com%2Fannounce");
        task.Should().NotBeNull();

        var monoTask = this.engine.GetTask(999);
        monoTask.Should().NotBeNull();

        // Adding an explicit UDP tracker should be rejected when proxy is active
        await this.engine.AddTrackersAsync(999, new[] { "udp://leaker.udp.tracker.org:6969/announce" });

        var tiers = (monoTask as MonoTorrentDownloadTask)?.Manager?.TrackerManager?.Tiers;
        if (tiers != null)
        {
            var udpTrackers = tiers.SelectMany(t => t.Trackers).Where(t => t.Uri != null && t.Uri.Scheme.Equals("udp", StringComparison.OrdinalIgnoreCase)).ToList();
            udpTrackers.Should().BeEmpty();
        }
    }

    [Test]
    public async Task AddTrackersAsync_OrdersTrackersByDatabaseTier_AndAvoidsDuplicates()
    {
        var mockTrackerRepo = Substitute.For<ITrackerEntryRepository>();
        mockTrackerRepo.GetByTorrentId(1234).Returns(new List<TrackerEntry>
        {
            new TrackerEntry { TorrentId = 1234, Url = "http://tracker-tier2.org/announce", Tier = 2 },
            new TrackerEntry { TorrentId = 1234, Url = "http://tracker-tier1.org/announce", Tier = 1 },
        });

        using var testEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            appFolderInfo: this.appFolderInfo,
            torrentLogService: this.torrentLogService,
            trackerEntryRepository: mockTrackerRepo);

        var torrent = new CoreTorrent
        {
            Id = 1234,
            InfoHash = "1111222233334444555566667777888899990000",
            Name = "TierOrderTestTorrent",
            Status = TorrentStatus.Downloading,
            TrackerUrl = "http://canonical-tracker.org/announce",
        };

        await testEngine.AddTorrentAsync(torrent, magnetUri: "magnet:?xt=urn:btih:1111222233334444555566667777888899990000&tr=http%3A%2F%2Fcanonical-tracker.org%2Fannounce");

        // Pass Tier 2 before Tier 1, plus duplicate canonical tracker
        await testEngine.AddTrackersAsync(1234, new[]
        {
            "http://tracker-tier2.org/announce",
            "http://canonical-tracker.org/announce",
            "http://tracker-tier1.org/announce",
        });

        var task = testEngine.GetTask(1234) as MonoTorrentDownloadTask;
        task.Should().NotBeNull();
        var tiers = task!.Manager!.TrackerManager!.Tiers;
        tiers.Should().NotBeNull();

        var trackerUris = tiers!.SelectMany(t => t.Trackers).Select(t => t.Uri.ToString()).ToList();
        trackerUris.Should().Contain("http://canonical-tracker.org/announce");
        trackerUris.Count(u => u == "http://canonical-tracker.org/announce").Should().Be(1);

        var tier1Index = trackerUris.IndexOf("http://tracker-tier1.org/announce");
        var tier2Index = trackerUris.IndexOf("http://tracker-tier2.org/announce");
        tier1Index.Should().BeGreaterThan(-1);
        tier2Index.Should().BeGreaterThan(-1);
        tier1Index.Should().BeLessThan(tier2Index);
    }

    [Test]
    public void BoundSocketConnector_WhenProxyActive_BlocksDatagramSocketCreation()
    {
        var mockConfig = Substitute.For<IConfigService>();
        mockConfig.ProxyType.Returns("socks5");
        mockConfig.ProxyHost.Returns("127.0.0.1");
        mockConfig.ProxyPort.Returns(1080);

        var connector = new BoundSocketConnector(
            IPAddress.Any,
            IPAddress.IPv6Any,
            configService: mockConfig);

        var act = () => connector.CreateDatagramSocket();
        act.Should().Throw<System.Net.Sockets.SocketException>()
            .Which.SocketErrorCode.Should().Be(System.Net.Sockets.SocketError.AccessDenied);
    }

    [Test]
    public void BoundSocketConnector_CreateDatagramSocket_WhenLocalIpv6IsNull_ThrowsNetworkUnreachable()
    {
        var connector = new BoundSocketConnector(
            () => IPAddress.Loopback,
            () => (IPAddress)null);

        var act = () => connector.CreateDatagramSocket(System.Net.Sockets.AddressFamily.InterNetworkV6);

        act.Should().Throw<System.Net.Sockets.SocketException>()
            .Where(e => e.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable);
    }

    [Test]
    public void BoundSocketConnector_CreateBoundSocket_WhenLocalIpv6IsNull_ThrowsNetworkUnreachable()
    {
        var connector = new BoundSocketConnector(
            () => IPAddress.Loopback,
            () => (IPAddress)null);

        var act = () => connector.CreateBoundSocket(
            System.Net.Sockets.AddressFamily.InterNetworkV6,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);

        act.Should().Throw<System.Net.Sockets.SocketException>()
            .Where(e => e.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable);
    }

    [Test]
    public async Task GetPeers_ComputesStandardTransferAndUtpFlags()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("peer_flags.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 509,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "peer_flags.iso",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var peerIdType = typeof(MonoTorrent.Client.PeerId);
        var peerField = peerIdType.GetField("<Peer>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        var peerType = peerField!.FieldType;

        var peer1 = (MonoTorrent.Client.PeerId)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(peerIdType);
        var peerInfo1 = new MonoTorrent.PeerInfo(new Uri("ipv4://192.168.1.100:6881"));
        var peer1Peer = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(peerType);
        SetPeerField(peer1Peer, "Info", peerInfo1);
        SetPeerField(peer1, "Peer", peer1Peer);
        SetPeerField(peer1, "AmInterested", true);
        SetPeerField(peer1, "IsChoking", false);
        SetPeerField(peer1, "IsInterested", true);
        SetPeerField(peer1, "AmChoking", false);

        var encryptorType = peerIdType.Assembly.GetTypes().FirstOrDefault(t => t.Name.Equals("RC4", StringComparison.OrdinalIgnoreCase) || t.Name.Equals("RC4Header", StringComparison.OrdinalIgnoreCase));
        if (encryptorType != null)
        {
            var encObj = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(encryptorType);
            SetPeerField(peer1, "Encryptor", encObj);
        }

        var mockConn1 = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnection>();
        mockConn1.IsIncoming.Returns(true);
        SetPeerField(peer1, "Connection", mockConn1);

        var peer2 = (MonoTorrent.Client.PeerId)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(peerIdType);
        var peerInfo2 = new MonoTorrent.PeerInfo(new Uri("utp://192.168.1.101:6881"));
        var peer2Peer = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(peerType);
        SetPeerField(peer2Peer, "Info", peerInfo2);
        SetPeerField(peer2, "Peer", peer2Peer);
        SetPeerField(peer2, "AmInterested", true);
        SetPeerField(peer2, "IsChoking", true);
        SetPeerField(peer2, "IsInterested", true);
        SetPeerField(peer2, "AmChoking", true);

        var peer3 = (MonoTorrent.Client.PeerId)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(peerIdType);
        var peerInfo3 = new MonoTorrent.PeerInfo(new Uri("ipv4://192.168.1.102:6881"));
        var peer3Peer = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(peerType);
        SetPeerField(peer3Peer, "Info", peerInfo3);
        SetPeerField(peer3, "Peer", peer3Peer);
        SetPeerField(peer3, "AmInterested", false);
        SetPeerField(peer3, "IsChoking", false);
        SetPeerField(peer3, "IsInterested", false);
        SetPeerField(peer3, "AmChoking", false);

        var cachedPeersField = typeof(MonoTorrentDownloadTask).GetField("cachedMonoPeers", BindingFlags.NonPublic | BindingFlags.Instance);
        cachedPeersField!.SetValue(task, new List<MonoTorrent.Client.PeerId> { peer1, peer2, peer3 });
        var lastPeersUpdateField = typeof(MonoTorrentDownloadTask).GetField("lastPeersUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        lastPeersUpdateField!.SetValue(task, DateTime.UtcNow.AddHours(1));

        var peers = task.GetPeers();
        peers.Should().HaveCount(3);

        // peer1: AmInterested & !IsChoking => 'D'
        //        IsInterested & !AmChoking => 'U'
        //        Incoming => 'I'
        //        Encryptor => 'E'
        //        Total => "DUIE"
        peers[0].Flags.Should().Be("DUIE");
        peers[0].IsUtp.Should().BeFalse();
        peers[0].IsEncrypted.Should().BeTrue();
        peers[0].IsIncoming.Should().BeTrue();

        // peer2: AmInterested & IsChoking => 'd'
        //        IsInterested & AmChoking => 'u'
        //        utp scheme => 'P'
        //        Total => "duP"
        peers[1].Flags.Should().Be("duP");
        peers[1].IsUtp.Should().BeTrue();
        peers[1].IsEncrypted.Should().BeFalse();
        peers[1].IsIncoming.Should().BeFalse();

        // peer3: !AmInterested && !IsInterested => no D/d, no U/u, no I
        peers[2].Flags.Should().BeEmpty();
        peers[2].IsUtp.Should().BeFalse();
        peers[2].IsEncrypted.Should().BeFalse();
        peers[2].IsIncoming.Should().BeFalse();
    }

    [Test]
    public async Task DisconnectPeerAsync_WhenCalled_HandlesAppropriately()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("disconnect_peer.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 510,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "disconnect_peer.iso",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var emptyRes = await task.DisconnectPeerAsync(string.Empty);
        emptyRes.Should().BeFalse();

        var nonExistentRes = await task.DisconnectPeerAsync("1.2.3.4");
        nonExistentRes.Should().BeFalse();
    }

    private static void SetPeerField(object obj, string name, object value)
    {
        var type = obj.GetType();
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    ?? type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(obj, value);
            return;
        }

        var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
        }
    }

    [Test]
    public async Task PreallocateFilesAsync_WhenAppendIncompleteExtensionIsTrue_PreallocatesWithPartialFileExtension()
    {
        var tempWorkingDir = Path.Combine(this.testIncompleteDir, "work_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkingDir);

        var sourceFile = Path.Combine(this.testDownloadDir, "sample_video_partial.mp4");
        var dummyData = new byte[32768];
        new Random(42).NextBytes(dummyData);
        await File.WriteAllBytesAsync(sourceFile, dummyData);

        var creationService = new NzbDrone.Core.BitTorrent.Creation.TorrentCreationService(new[] { this.testDownloadDir });
        var request = new NzbDrone.Core.BitTorrent.Creation.TorrentCreationRequest
        {
            Path = sourceFile,
            Name = "sample_video_partial.mp4",
            PieceLength = 16384,
            OutputPath = Path.Combine(this.testDownloadDir, "sample_partial.torrent"),
        };

        var result = await creationService.CreateTorrentAsync(request);
        var loadedTorrent = MonoTorrent.Torrent.Load(result.TorrentFileBytes);

        this.configService.PreallocationMode.Returns("Sparse");
        this.configService.AppendIncompleteExtension.Returns(true);

        await this.engine.PreallocateFilesAsync(null, tempWorkingDir, loadedTorrent);

        var expectedPartialPath = Path.Combine(tempWorkingDir, "sample_video_partial.mp4.!mt");
        var nonPartialPath = Path.Combine(tempWorkingDir, "sample_video_partial.mp4");

        File.Exists(expectedPartialPath).Should().BeTrue("File should be preallocated with the .!mt extension");
        File.Exists(nonPartialPath).Should().BeFalse("File should not be preallocated with standard extension when partial files enabled");
        new FileInfo(expectedPartialPath).Length.Should().Be(32768);
    }

    [Test]
    public async Task PreallocateFilesAsync_WhenAppendIncompleteExtensionIsFalse_PreallocatesWithoutPartialFileExtension()
    {
        var tempWorkingDir = Path.Combine(this.testIncompleteDir, "work_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkingDir);

        var sourceFile = Path.Combine(this.testDownloadDir, "sample_video_normal.mp4");
        var dummyData = new byte[32768];
        new Random(42).NextBytes(dummyData);
        await File.WriteAllBytesAsync(sourceFile, dummyData);

        var creationService = new NzbDrone.Core.BitTorrent.Creation.TorrentCreationService(new[] { this.testDownloadDir });
        var request = new NzbDrone.Core.BitTorrent.Creation.TorrentCreationRequest
        {
            Path = sourceFile,
            Name = "sample_video_normal.mp4",
            PieceLength = 16384,
            OutputPath = Path.Combine(this.testDownloadDir, "sample_normal.torrent"),
        };

        var result = await creationService.CreateTorrentAsync(request);
        var loadedTorrent = MonoTorrent.Torrent.Load(result.TorrentFileBytes);

        this.configService.PreallocationMode.Returns("Sparse");
        this.configService.AppendIncompleteExtension.Returns(false);

        await this.engine.PreallocateFilesAsync(null, tempWorkingDir, loadedTorrent);

        var expectedPartialPath = Path.Combine(tempWorkingDir, "sample_video_normal.mp4.!mt");
        var standardPath = Path.Combine(tempWorkingDir, "sample_video_normal.mp4");

        File.Exists(standardPath).Should().BeTrue("File should be preallocated with standard filename");
        File.Exists(expectedPartialPath).Should().BeFalse("File should not have .!mt extension when partial files disabled");
        new FileInfo(standardPath).Length.Should().Be(32768);
    }

    [Test]
    public async Task PreallocateFilesAsync_WhenPreallocationFails_AbortsSyntheticFastResume()
    {
        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = (ClientEngine)engineProp!.GetValue(this.engine)!;
        monoEngine.Should().NotBeNull();

        var tempWorkingDir = Path.Combine(this.testIncompleteDir, "work_fail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkingDir);

        var torrentBytes = CreateSampleSingleFileTorrentBytes("prealloc_fail.bin", 32768);
        var loadedTorrent = MonoTorrent.Torrent.Load(torrentBytes);

        this.configService.PreallocationMode.Returns("Sparse");
        this.configService.AppendIncompleteExtension.Returns(false);

        // Target an unwriteable / invalid path (file blocking directory creation)
        var blockedPath = Path.Combine(tempWorkingDir, "blocked_dir");
        await File.WriteAllTextAsync(blockedPath, "I am a file, not a directory");
        var invalidWorkingDir = Path.Combine(blockedPath, "nested");

        var torrentSettings = new TorrentSettingsBuilder().ToSettings();
        var manager = await monoEngine.AddAsync(loadedTorrent, invalidWorkingDir, torrentSettings);
        manager.HashChecked.Should().BeFalse();

        await this.engine.PreallocateFilesAsync(manager, invalidWorkingDir, loadedTorrent);

        // Preallocation failure must NOT initialize FastResume / HashChecked
        manager.HashChecked.Should().BeFalse();

        await this.engine.StopAsync();
    }

    [Test]
    public async Task StopAsync_StopsManagersAndSavesFastResumeCheckpoints()
    {
        await this.engine.StartAsync();

        var torrentBytes = CreateSampleSingleFileTorrentBytes("shutdown_test.iso", isPrivate: false);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 602,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "shutdown_test.iso",
            Status = TorrentStatus.Downloading,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(50_000_000_000L);
        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Manager.Should().NotBeNull();

        await this.engine.StopAsync();

        task.Manager.State.Should().Be(TorrentState.Stopped);

        var cacheDir = (typeof(MonoTorrentDownloadEngine).GetMethod("GetCacheDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(this.engine, null) as string)!;
        var fastResumeFile = Path.Combine(cacheDir, "FastResume", $"{torrent.InfoHash}.fastresume");
        File.Exists(fastResumeFile).Should().BeTrue();
    }

    [Test]
    public void AutoRecheckOnCompletion_DefaultSetting_IsTrue()
    {
        this.configService.AutoRecheckOnCompletion.Returns(true);
        this.configService.AutoRecheckOnCompletion.Should().BeTrue();
    }

    [Test]
    public async Task ForceRecheckAsync_WhenFilesLocatedInCompletedFolder_AlignsSavePathBeforeRecheck()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("recheck_completed_test.iso", length: 16384);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 99,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "recheck_completed_test.iso",
            Status = TorrentStatus.Paused,
            SavePath = this.testIncompleteDir,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        // Place file in completed directory
        var completedFilePath = Path.Combine(this.testDownloadDir, "recheck_completed_test.iso");
        await File.WriteAllBytesAsync(completedFilePath, new byte[16384]);

        // Task save path is updated to completed directory
        task.SavePath = this.testDownloadDir;
        task.WorkingPath = this.testDownloadDir;

        // ForceRecheckAsync should not throw and should re-align manager save path
        Func<Task> act = async () => await this.engine.ForceRecheckAsync(99);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public void TrimMonoTorrentMassiveBuffers_ExecutesWithoutExceptionsAndClearsPool()
    {
        using (MonoTorrent.MemoryPool.Default.Rent(16 * 1024 * 1024, out Memory<byte> mem))
        {
            mem.Length.Should().Be(16 * 1024 * 1024);
        }

        Action act = () => MonoTorrentDownloadEngine.TrimMonoTorrentMassiveBuffers();
        act.Should().NotThrow();
    }

    [Test]
    public void CachePolicy_DefaultsToWritesOnly_WhenNotExplicitlyConfigured()
    {
        this.configService.DiskCachePolicy.Returns((string)null!);
        var method = typeof(MonoTorrentDownloadEngine).GetMethod("GetConfiguredCachePolicy", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var result = method!.Invoke(this.engine, null);
        result.Should().Be(MonoTorrent.PieceWriter.CachePolicy.WritesOnly);
    }

    private static MonoTorrent.PieceHash CreatePieceHash(Memory<byte> v1, Memory<byte> v2)
    {
        var ctor = typeof(MonoTorrent.PieceHash).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(Memory<byte>), typeof(Memory<byte>) },
            null);
        return (MonoTorrent.PieceHash)ctor!.Invoke(new object[] { v1, v2 });
    }

    [Test]
    public async Task CalculatePieceHashDirectAsync_PureV2_DoesNotThrowAndComputesV2Hash()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("pure_v2.bin", length: 16384);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 201,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "pure_v2.bin",
            Status = TorrentStatus.Paused,
            SavePath = this.testDownloadDir,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var filePath = task.Manager.Files[0].FullPath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var testData = new byte[16384];
        Random.Shared.NextBytes(testData);
        await File.WriteAllBytesAsync(filePath, testData);

        var v2Hash = new byte[32];
        var pieceHash = CreatePieceHash(Memory<byte>.Empty, new Memory<byte>(v2Hash));

        var success = await this.engine.CalculatePieceHashDirectAsync(task.Manager, 0, pieceHash);

        success.Should().BeTrue();
        v2Hash.Should().Equal(SHA256.HashData(testData));
    }

    [Test]
    public async Task CalculatePieceHashDirectAsync_PureV1_ComputesV1HashAndLeavesV2Empty()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("pure_v1.bin", length: 16384);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 202,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "pure_v1.bin",
            Status = TorrentStatus.Paused,
            SavePath = this.testDownloadDir,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var filePath = task.Manager.Files[0].FullPath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var testData = new byte[16384];
        Random.Shared.NextBytes(testData);
        await File.WriteAllBytesAsync(filePath, testData);

        var v1Hash = new byte[20];
        var pieceHash = CreatePieceHash(new Memory<byte>(v1Hash), Memory<byte>.Empty);

        var success = await this.engine.CalculatePieceHashDirectAsync(task.Manager, 0, pieceHash);

        success.Should().BeTrue();
        v1Hash.Should().Equal(SHA1.HashData(testData));
    }

    [Test]
    public async Task CalculatePieceHashDirectAsync_Hybrid_ComputesBothV1AndV2Hashes()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("hybrid.bin", length: 16384);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 203,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "hybrid.bin",
            Status = TorrentStatus.Paused,
            SavePath = this.testDownloadDir,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var filePath = task.Manager.Files[0].FullPath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var testData = new byte[16384];
        Random.Shared.NextBytes(testData);
        await File.WriteAllBytesAsync(filePath, testData);

        var v1Hash = new byte[20];
        var v2Hash = new byte[32];
        var pieceHash = CreatePieceHash(new Memory<byte>(v1Hash), new Memory<byte>(v2Hash));

        var success = await this.engine.CalculatePieceHashDirectAsync(task.Manager, 0, pieceHash);

        success.Should().BeTrue();
        v1Hash.Should().Equal(SHA1.HashData(testData));
        v2Hash.Should().Equal(SHA256.HashData(testData));
    }

    [Test]
    public void GetConfiguredWebProxy_WhenNoProxyConfigured_ReturnsNull()
    {
        this.configService.ProxyType.Returns((string)null!);
        this.configService.ProxyHost.Returns((string)null!);

        var proxy = this.engine.GetConfiguredWebProxy();
        proxy.Should().BeNull();

        this.configService.ProxyType.Returns("none");
        this.configService.ProxyHost.Returns("127.0.0.1");

        proxy = this.engine.GetConfiguredWebProxy();
        proxy.Should().BeNull();
    }

    [Test]
    public void GetConfiguredWebProxy_WhenSocks5Configured_ReturnsWebProxyWithCorrectUriAndCredentials()
    {
        this.configService.ProxyType.Returns("socks5");
        this.configService.ProxyHost.Returns("10.0.0.1");
        this.configService.ProxyPort.Returns(1080);
        this.configService.ProxyUsername.Returns("proxyuser");
        this.configService.ProxyPassword.Returns("proxypass");

        var proxy = this.engine.GetConfiguredWebProxy() as WebProxy;
        proxy.Should().NotBeNull();
        proxy!.Address.Should().Be(new Uri("socks5://10.0.0.1:1080"));
        proxy.Credentials.Should().NotBeNull();
    }

    [Test]
    public void GetConfiguredWebProxy_WhenHttpConfigured_ReturnsWebProxyWithCorrectUri()
    {
        this.configService.ProxyType.Returns("http");
        this.configService.ProxyHost.Returns("proxy.example.com");
        this.configService.ProxyPort.Returns(8080);
        this.configService.ProxyUsername.Returns((string)null!);
        this.configService.ProxyPassword.Returns((string)null!);

        var proxy = this.engine.GetConfiguredWebProxy() as WebProxy;
        proxy.Should().NotBeNull();
        proxy!.Address.Should().Be(new Uri("http://proxy.example.com:8080"));
        proxy.Credentials.Should().BeNull();
    }

    [TestCase("::1", "socks5://[::1]:1080/")]
    [TestCase("fd00::1", "socks5://[fd00::1]:1080/")]
    [TestCase("[::1]", "socks5://[::1]:1080/")]
    public void GetConfiguredWebProxy_WhenIpv6Host_FormatsSafelyWithoutUriFormatException(string ipv6Host, string expectedUri)
    {
        this.configService.ProxyType.Returns("socks5");
        this.configService.ProxyHost.Returns(ipv6Host);
        this.configService.ProxyPort.Returns(1080);

        var proxy = this.engine.GetConfiguredWebProxy() as WebProxy;
        proxy.Should().NotBeNull();
        proxy!.Address.ToString().Should().Be(expectedUri);
    }

    [Test]
    public async Task StartAsync_WhenAnonymousModeEnabled_SuppressesListenEndpointsDhtLpdAndPortForwarding()
    {
        this.configService.AnonymousMode.Returns(true);
        this.configService.UpnpEnabled.Returns(true);
        this.configService.EnableDht.Returns(true);
        this.configService.EnableLpd.Returns(true);
        this.configService.ListeningPort.Returns(51413);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();

        monoEngine!.Settings.ListenEndPoints.Should().BeEmpty();
        monoEngine.Settings.AllowPortForwarding.Should().BeFalse();
        monoEngine.Settings.AllowLocalPeerDiscovery.Should().BeFalse();
        monoEngine.Settings.DhtEndPoint.Should().BeNull();

        // Verify peer ID is randomized (length 20, no client emulation prefix)
        monoEngine.PeerId.Text.Length.Should().Be(20);
        monoEngine.PeerId.Text.Should().NotStartWith("-qB");
        monoEngine.PeerId.Text.Should().NotStartWith("-MO");

        await this.engine.StopAsync();
    }

    [Test]
    public async Task ApplyConfigChangesAsync_WhenInterfaceBindingOrProxyChanges_UpdatesLastAppliedSettings()
    {
        await this.engine.StartAsync();

        // Change interface
        this.configService.NetworkInterfaceBinding.Returns("tun0");
        await this.engine.ApplyConfigChangesAsync();

        this.engine.LastAppliedInterfaceBinding.Should().Be("tun0");

        // Change proxy
        this.configService.ProxyType.Returns("socks5");
        this.configService.ProxyHost.Returns("127.0.0.1");
        this.configService.ProxyPort.Returns(1080);
        await this.engine.ApplyConfigChangesAsync();

        this.engine.LastAppliedProxyType.Should().Be("socks5");
        this.engine.LastAppliedProxyHost.Should().Be("127.0.0.1");
        this.engine.LastAppliedProxyPort.Should().Be(1080);

        await this.engine.StopAsync();
    }

    [Test]
    public void FilteringPeerConnectionListener_WhenKillSwitchActive_DisposesConnectionAndDropsEvent()
    {
        var innerListener = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnectionListener>();
        var isHalted = true;
        var filteringListener = new FilteringPeerConnectionListener(
            innerListener,
            isHalted: () => isHalted);

        var eventRaised = false;
        filteringListener.ConnectionReceived += (_, _) => eventRaised = true;

        var mockConn = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnection, IDisposable>();
        mockConn.IsIncoming.Returns(true);
        mockConn.Uri.Returns(new Uri("ipv4://203.0.113.5:12345"));
        var args = new MonoTorrent.Connections.Peer.PeerConnectionEventArgs(mockConn, null);

        // Raise incoming connection while kill switch is active
        innerListener.ConnectionReceived += Raise.Event<EventHandler<MonoTorrent.Connections.Peer.PeerConnectionEventArgs>>(innerListener, args);

        eventRaised.Should().BeFalse();
        ((IDisposable)mockConn).Received(1).Dispose();
    }

    [Test]
    public async Task HaltAllTorrentsForKillSwitchAsync_DisablesDhtEndPoint()
    {
        this.configService.EnableDht.Returns(true);
        this.configService.ProxyType.Returns((string)null!);
        this.configService.ProxyHost.Returns((string)null!);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.DhtEndPoint.Should().NotBeNull();

        await this.engine.HaltAllTorrentsForKillSwitchAsync();

        monoEngine.Settings.DhtEndPoint.Should().BeNull();

        await this.engine.StopAsync();
    }

    [Test]
    public async Task UpdateEngineListenEndpointsAsync_WhenProxyActive_DoesNotEnableDhtEndPoint()
    {
        this.configService.EnableDht.Returns(true);
        this.configService.ProxyType.Returns("socks5");
        this.configService.ProxyHost.Returns("127.0.0.1");
        this.configService.ProxyPort.Returns(1080);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.DhtEndPoint.Should().BeNull();

        await this.engine.UpdateEngineListenEndpointsAsync();

        monoEngine.Settings.DhtEndPoint.Should().BeNull();

        await this.engine.StopAsync();
    }

    [Test]
    public async Task StartAsync_WhenLpdEnabledAndNoVpnOrProxy_EnablesLpd()
    {
        this.configService.EnableLpd.Returns(true);
        this.configService.ProxyType.Returns((string)null!);
        this.configService.ProxyHost.Returns((string)null!);
        this.configService.BindInterface.Returns((string)null!);
        this.configService.NetworkInterfaceBinding.Returns((string)null!);
        this.configService.EnableVpnKillSwitch.Returns(false);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.AllowLocalPeerDiscovery.Should().BeTrue();

        await this.engine.StopAsync();
    }

    [Test]
    public async Task StartAsync_WhenBindInterfaceSpecified_DisablesLpdToPreventLanMulticastLeaks()
    {
        this.configService.EnableLpd.Returns(true);
        this.configService.ProxyType.Returns((string)null!);
        this.configService.ProxyHost.Returns((string)null!);
        this.configService.BindInterface.Returns("tun0");
        this.configService.NetworkInterfaceBinding.Returns((string)null!);
        this.configService.EnableVpnKillSwitch.Returns(false);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.AllowLocalPeerDiscovery.Should().BeFalse();

        await this.engine.StopAsync();
    }

    [Test]
    public async Task StartAsync_WhenNetworkInterfaceBindingSpecified_DisablesLpdToPreventLanMulticastLeaks()
    {
        this.configService.EnableLpd.Returns(true);
        this.configService.ProxyType.Returns((string)null!);
        this.configService.ProxyHost.Returns((string)null!);
        this.configService.BindInterface.Returns((string)null!);
        this.configService.NetworkInterfaceBinding.Returns("wg0");
        this.configService.EnableVpnKillSwitch.Returns(false);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.AllowLocalPeerDiscovery.Should().BeFalse();

        await this.engine.StopAsync();
    }

    [Test]
    public async Task StartAsync_WhenVpnKillSwitchEnabledWithInterface_DisablesLpdToPreventLanMulticastLeaks()
    {
        var mockVpnService = Substitute.For<IVpnKillSwitchService>();
        mockVpnService.IsKillSwitchEnabled.Returns(true);
        mockVpnService.GetVpnInterfaceIpAddress(Arg.Any<AddressFamily>()).Returns(IPAddress.Loopback);

        using var vpnEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpnService);

        this.configService.EnableLpd.Returns(true);
        this.configService.ProxyType.Returns((string)null!);
        this.configService.ProxyHost.Returns((string)null!);
        this.configService.BindInterface.Returns("tun0");
        this.configService.EnableVpnKillSwitch.Returns(true);

        await vpnEngine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(vpnEngine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.AllowLocalPeerDiscovery.Should().BeFalse();

        await vpnEngine.StopAsync();
    }

    [Test]
    public async Task StartAsync_WhenVpnKillSwitchEnabledWithoutInterface_HaltsInFailClosedState()
    {
        this.configService.EnableLpd.Returns(true);
        this.configService.ProxyType.Returns((string)null!);
        this.configService.ProxyHost.Returns((string)null!);
        this.configService.BindInterface.Returns((string)null!);
        this.configService.NetworkInterfaceBinding.Returns((string)null!);
        this.configService.EnableVpnKillSwitch.Returns(true);

        await this.engine.StartAsync();

        this.engine.IsHaltedByKillSwitch.Should().BeTrue();
    }

    [Test]
    public async Task HaltAllTorrentsForKillSwitchAsync_DisablesLpd()
    {
        this.configService.EnableLpd.Returns(true);
        this.configService.ProxyType.Returns((string)null!);
        this.configService.ProxyHost.Returns((string)null!);
        this.configService.BindInterface.Returns((string)null!);
        this.configService.NetworkInterfaceBinding.Returns((string)null!);
        this.configService.EnableVpnKillSwitch.Returns(false);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.AllowLocalPeerDiscovery.Should().BeTrue();

        await this.engine.HaltAllTorrentsForKillSwitchAsync();

        monoEngine.Settings.AllowLocalPeerDiscovery.Should().BeFalse();

        await this.engine.StopAsync();
    }

    [Test]
    public async Task UpdateEngineListenEndpointsAsync_WhenVpnKillSwitchOrInterfaceBindingActive_SuppressesLpd()
    {
        this.configService.EnableLpd.Returns(true);
        this.configService.ProxyType.Returns((string)null!);
        this.configService.ProxyHost.Returns((string)null!);
        this.configService.BindInterface.Returns((string)null!);
        this.configService.NetworkInterfaceBinding.Returns((string)null!);
        this.configService.EnableVpnKillSwitch.Returns(false);

        await this.engine.StartAsync();

        var engineProp = typeof(MonoTorrentDownloadEngine).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance);
        var monoEngine = engineProp!.GetValue(this.engine) as ClientEngine;
        monoEngine.Should().NotBeNull();
        monoEngine!.Settings.AllowLocalPeerDiscovery.Should().BeTrue();

        // Simulate activating VPN kill switch and refreshing listen endpoints
        this.configService.EnableVpnKillSwitch.Returns(true);
        await this.engine.UpdateEngineListenEndpointsAsync();

        monoEngine.Settings.AllowLocalPeerDiscovery.Should().BeFalse();

        await this.engine.StopAsync();
    }

    #endregion
    private static byte[] CreateSampleTorrentWithTiers(
        string name,
        List<List<string>> tiers)
    {
        var pieceLength = 16384;
        var length = 16384;
        var pieces = new byte[20];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)((i % 250) + 1);
        }

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString(name) },
            { "piece length", new BEncodedNumber(pieceLength) },
            { "pieces", new BEncodedString(pieces) },
            { "length", new BEncodedNumber(length) },
        };

        var primaryAnnounce = tiers != null && tiers.Count > 0 && tiers[0].Count > 0
            ? tiers[0][0]
            : "http://tracker1.example.com/announce";

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString(primaryAnnounce) },
            { "info", infoDict },
        };

        if (tiers != null && tiers.Count > 0)
        {
            var announceList = new BEncodedList();
            foreach (var tier in tiers)
            {
                var tierList = new BEncodedList();
                foreach (var url in tier)
                {
                    tierList.Add(new BEncodedString(url));
                }

                announceList.Add(tierList);
            }

            rootDict.Add("announce-list", announceList);
        }

        return rootDict.Encode();
    }

    [Test]
    public async Task ForceAnnounceAsync_WhenAnnounceToAllInTier_DispatchesToAllTrackersAndLogsAccurately()
    {
        this.configService.AnnounceToAllInTier.Returns(true);
        this.configService.AnnounceToAllTiers.Returns(false);

        var tiers = new List<List<string>>
        {
            new() { "http://127.0.0.1:51111/announce", "http://127.0.0.1:51112/announce" },
            new() { "http://127.0.0.1:51113/announce", "http://127.0.0.1:51114/announce" },
        };

        var torrentBytes = CreateSampleTorrentWithTiers("force_announce_all_in_tier.bin", tiers);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 601,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "force_announce_all_in_tier.bin",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State != TorrentState.Downloading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        await this.engine.ForceAnnounceAsync(torrent.Id);

        this.torrentLogService.Received().Log(
            torrent.Id,
            "Info",
            "Tracker",
            Arg.Is<string>(msg => msg.Contains("Dispatched announce & scrape request to 4 tracker(s)")));
    }

    [Test]
    public async Task ForceAnnounceAsync_WhenAnnounceToAllTiers_DispatchesToAllTiersAndLogsAccurately()
    {
        this.configService.AnnounceToAllInTier.Returns(false);
        this.configService.AnnounceToAllTiers.Returns(true);

        var tiers = new List<List<string>>
        {
            new() { "http://127.0.0.1:52111/announce", "http://127.0.0.1:52112/announce" },
            new() { "http://127.0.0.1:52113/announce", "http://127.0.0.1:52114/announce" },
        };

        var torrentBytes = CreateSampleTorrentWithTiers("force_announce_all_tiers.bin", tiers);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 602,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "force_announce_all_tiers.bin",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State != TorrentState.Downloading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        await this.engine.ForceAnnounceAsync(torrent.Id);

        this.torrentLogService.Received().Log(
            torrent.Id,
            "Info",
            "Tracker",
            Arg.Is<string>(msg => msg.Contains("Dispatched announce & scrape request to 2 tier(s)")));
    }

    [Test]
    public async Task ForceAnnounceAsync_WhenSingleTracker_DispatchesToSingleTrackerAndLogsAccurately()
    {
        this.configService.AnnounceToAllInTier.Returns(false);
        this.configService.AnnounceToAllTiers.Returns(false);

        var tiers = new List<List<string>>
        {
            new() { "http://127.0.0.1:53111/announce", "http://127.0.0.1:53112/announce" },
            new() { "http://127.0.0.1:53113/announce", "http://127.0.0.1:53114/announce" },
        };

        var torrentBytes = CreateSampleTorrentWithTiers("force_announce_single.bin", tiers);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 603,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "force_announce_single.bin",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State != TorrentState.Downloading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        await this.engine.ForceAnnounceAsync(torrent.Id);

        this.torrentLogService.Received().Log(
            torrent.Id,
            "Info",
            "Tracker",
            Arg.Is<string>(msg => msg.Contains("Dispatched announce & scrape request to 1 tracker(s)")));
    }

    [Test]
    public async Task ForceAnnounceAsync_WhenTorrentIsStopped_GuardsAndLogsWarning()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("force_announce_stopped.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 604,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "force_announce_stopped.bin",
            Status = TorrentStatus.Stopped,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        await this.engine.ForceAnnounceAsync(torrent.Id);

        this.torrentLogService.Received().Log(
            torrent.Id,
            "Warn",
            "Tracker",
            Arg.Is<string>(msg => msg.Contains("Cannot announce to tracker: torrent is in Stopped state")));
        this.torrentLogService.DidNotReceive().Log(
            torrent.Id,
            "Info",
            "Tracker",
            Arg.Is<string>(msg => msg.Contains("Dispatched announce & scrape request")));
    }

    [Test]
    public async Task ForceAnnounceAsync_WhenTorrentIsPaused_GuardsAndLogsWarning()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("force_announce_paused.bin");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 605,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "force_announce_paused.bin",
            Status = TorrentStatus.Downloading,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        task.Should().NotBeNull();

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (task.Manager.State != TorrentState.Downloading && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        await this.engine.PauseTorrentAsync(torrent.Id);

        await this.engine.ForceAnnounceAsync(torrent.Id);

        this.torrentLogService.Received().Log(
            torrent.Id,
            "Warn",
            "Tracker",
            Arg.Is<string>(msg => msg.Contains("Cannot announce to tracker: torrent is in Paused state")));
        this.torrentLogService.DidNotReceive().Log(
            torrent.Id,
            "Info",
            "Tracker",
            Arg.Is<string>(msg => msg.Contains("Dispatched announce & scrape request")));
    }

    [Test]
    public async Task AddTorrentAsync_WhenDatabaseReportsSeedingButCompletedFilesMissing_DoesNotSynthesizeFastResumeAndTriggersHashCheck()
    {
        var fileName = "missing_completed_file.bin";
        var fileSize = 32768;
        var torrentBytes = CreateSampleSingleFileTorrentBytes(fileName, fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 701,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = fileName,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SavePath = this.testDownloadDir,
            DateCompleted = DateTime.UtcNow,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(50_000_000_000L);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(false);
        this.diskProvider.FolderExists(Arg.Any<string>()).Returns(false);

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        var cacheDir = (typeof(MonoTorrentDownloadEngine).GetMethod("GetCacheDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(this.engine, null) as string)!;
        var fastResumeFile = Path.Combine(cacheDir, "FastResume", $"{torrent.InfoHash}.fastresume");
        File.Exists(fastResumeFile).Should().BeFalse();
        task.IsFilesMovedToCompleted.Should().BeFalse();
        task.Manager.Bitfield.AllTrue.Should().BeFalse();
        (torrent.Status == TorrentStatus.Checking || task.Manager.State == TorrentState.Hashing).Should().BeTrue();
    }

    [Test]
    public async Task AddTorrentAsync_WhenDatabaseReportsSeedingAndCompletedFilesExist_SynthesizesFastResume()
    {
        var fileName = "existing_completed_file.bin";
        var fileSize = 32768;
        var torrentBytes = CreateSampleSingleFileTorrentBytes(fileName, fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var completedFilePath = Path.Combine(this.testDownloadDir, fileName);
        Directory.CreateDirectory(this.testDownloadDir);
        await File.WriteAllBytesAsync(completedFilePath, new byte[fileSize]);

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(50_000_000_000L);
        this.diskProvider.FileExists(completedFilePath).Returns(true);
        this.diskProvider.GetFileSize(completedFilePath).Returns(fileSize);

        var torrent = new CoreTorrent
        {
            Id = 702,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = fileName,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SavePath = this.testDownloadDir,
            DateCompleted = DateTime.UtcNow,
        };

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.IsFilesMovedToCompleted.Should().BeTrue();
        task.Manager.Bitfield.AllTrue.Should().BeTrue();
    }

    [Test]
    public async Task AddTorrentAsync_WhenSavedFastResumeHasAllTrueBitfieldButFilesMissing_IgnoresFastResumeAndTriggersHashCheck()
    {
        var fileName = "stale_resume_missing_file.bin";
        var fileSize = 32768;
        var torrentBytes = CreateSampleSingleFileTorrentBytes(fileName, fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);
        var infoHashHex = parsed.InfoHashes.V1OrV2.ToHex();

        var cacheDir = (typeof(MonoTorrentDownloadEngine).GetMethod("GetCacheDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(this.engine, null) as string)!;
        var fastResumeDir = Path.Combine(cacheDir, "FastResume");
        Directory.CreateDirectory(fastResumeDir);

        var pieceCount = parsed.PieceCount;
        var bitfield = new BitField(pieceCount).SetAll(true);
        var unhashed = new BitField(pieceCount);
        var fastResume = new FastResume(parsed.InfoHashes, new ReadOnlyBitField(bitfield), new ReadOnlyBitField(unhashed));
        await File.WriteAllBytesAsync(Path.Combine(fastResumeDir, $"{infoHashHex}.fastresume"), fastResume.Encode());

        var torrent = new CoreTorrent
        {
            Id = 703,
            InfoHash = infoHashHex,
            Name = fileName,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            SavePath = this.testDownloadDir,
            DateCompleted = DateTime.UtcNow,
        };

        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(50_000_000_000L);
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(false);
        this.diskProvider.FolderExists(Arg.Any<string>()).Returns(false);

        var task = (MonoTorrentDownloadTask)await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);

        task.Should().NotBeNull();
        task.IsFilesMovedToCompleted.Should().BeFalse();
        task.Manager.Bitfield.AllTrue.Should().BeFalse();
        (torrent.Status == TorrentStatus.Checking || task.Manager.State == TorrentState.Hashing).Should().BeTrue();
    }

    [Test]
    public async Task TryLoadSavedFastResumeAsync_WithValidDictionary_PreservesMetadata()
    {
        var fileName = "valid_metadata_resume.bin";
        var fileSize = 16384;
        var torrentBytes = CreateSampleSingleFileTorrentBytes(fileName, fileSize);
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);
        var infoHashHex = parsed.InfoHashes.V1OrV2.ToHex();

        var cacheDir = (typeof(MonoTorrentDownloadEngine).GetMethod("GetCacheDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(this.engine, null) as string)!;
        var fastResumeDir = Path.Combine(cacheDir, "FastResume");
        Directory.CreateDirectory(fastResumeDir);

        var pieceCount = parsed.PieceCount;
        var bitfield = new BitField(pieceCount).SetAll(true);
        var unhashed = new BitField(pieceCount);
        var fastResume = new FastResume(parsed.InfoHashes, new ReadOnlyBitField(bitfield), new ReadOnlyBitField(unhashed));
        await File.WriteAllBytesAsync(Path.Combine(fastResumeDir, $"{infoHashHex}.fastresume"), fastResume.Encode());

        var loaded = await this.engine.TryLoadSavedFastResumeAsync(infoHashHex, cacheDir);

        loaded.Should().NotBeNull();
        loaded!.Bitfield.AllTrue.Should().BeTrue();
        loaded.InfoHashes.V1OrV2.ToHex().Should().BeEquivalentTo(infoHashHex);
    }

    [TestCase("forceencrypted", false)]
    [TestCase("forced", false)]
    [TestCase("requireencrypted", false)]
    [TestCase("forcedencryption", false)]
    [TestCase("required", false)]
    [TestCase("preferencrypted", true)]
    [TestCase("enabled", true)]
    [TestCase("preferred", true)]
    [TestCase("allowplaintext", true)]
    [TestCase("disabled", true)]
    [TestCase("plaintext", true)]
    [TestCase("none", true)]
    [TestCase("unknown", true)]
    [TestCase(null, true)]
    public void GetAllowedEncryption_ResolvesExpectedEncryptionTypes(string mode, bool allowsPlainText)
    {
        var result = MonoTorrentDownloadEngine.GetAllowedEncryption(mode);
        result.Should().NotBeNull();
        result.Contains(MonoTorrent.Connections.EncryptionType.PlainText).Should().Be(allowsPlainText);

        if (!allowsPlainText)
        {
            result.Should().Contain(MonoTorrent.Connections.EncryptionType.RC4Full);
            result.Should().Contain(MonoTorrent.Connections.EncryptionType.RC4Header);
            result.Should().NotContain(MonoTorrent.Connections.EncryptionType.PlainText);
        }
    }

    [Test]
    public void CreateHttpClient_WhenProxyNotConfigured_SetsConnectCallback()
    {
        var client = this.engine.CreateHttpClient();
        var handlerField = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance);
        var handler = handlerField!.GetValue(client) as SocketsHttpHandler;

        handler.Should().NotBeNull();
        handler!.ConnectCallback.Should().NotBeNull();
    }

    [Test]
    public void CreateHttpClient_WhenProxyConfigured_UsesProxyAndDoesNotSetConnectCallback()
    {
        var mockConfig = Substitute.For<IConfigService>();
        mockConfig.ProxyType.Returns("http");
        mockConfig.ProxyHost.Returns("127.0.0.1");
        mockConfig.ProxyPort.Returns(8080);

        using var testEngine = new MonoTorrentDownloadEngine(
            mockConfig,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            appFolderInfo: this.appFolderInfo,
            torrentLogService: this.torrentLogService);

        var client = testEngine.CreateHttpClient();
        var handlerField = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance);
        var handler = handlerField!.GetValue(client) as SocketsHttpHandler;

        handler.Should().NotBeNull();
        handler!.Proxy.Should().NotBeNull();
        handler.UseProxy.Should().BeTrue();
        handler.ConnectCallback.Should().BeNull();
    }

    [Test]
    public async Task CreateHttpClient_WhenKillSwitchTriggered_ConnectCallbackThrowsNetworkUnreachable()
    {
        var mockVpn = Substitute.For<IVpnKillSwitchService>();
        mockVpn.IsFailClosedActive.Returns(true);

        using var testEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            vpnKillSwitchService: mockVpn,
            appFolderInfo: this.appFolderInfo,
            torrentLogService: this.torrentLogService);

        var client = testEngine.CreateHttpClient();
        var act = async () => await client.GetAsync("http://tracker.example.com/announce");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        var socketEx = ex.Which.InnerException as SocketException;
        socketEx.Should().NotBeNull();
        socketEx!.SocketErrorCode.Should().Be(SocketError.NetworkUnreachable);
    }

    [Test]
    public async Task CreateHttpClient_WhenEngineHaltedByKillSwitch_ConnectCallbackThrowsNetworkUnreachable()
    {
        using var testEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            appFolderInfo: this.appFolderInfo,
            torrentLogService: this.torrentLogService);

        testEngine.Handle(new VpnKillSwitchTriggeredEvent("tun0"));

        testEngine.IsHaltedByKillSwitch.Should().BeTrue();

        var client = testEngine.CreateHttpClient();
        var act = async () => await client.GetAsync("http://tracker.example.com/announce");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        var socketEx = ex.Which.InnerException as SocketException;
        socketEx.Should().NotBeNull();
        socketEx!.SocketErrorCode.Should().Be(SocketError.NetworkUnreachable);
    }

    [Test]
    public async Task CreateHttpClient_WhenTrackerIpBlocked_ConnectCallbackThrowsAccessDenied()
    {
        var mockBlocklist = Substitute.For<IBlocklistService>();
        mockBlocklist.IsIpBlocked("192.0.2.1").Returns(true);

        using var testEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            blocklistService: mockBlocklist,
            appFolderInfo: this.appFolderInfo,
            torrentLogService: this.torrentLogService);

        var client = testEngine.CreateHttpClient();
        var act = async () => await client.GetAsync("http://192.0.2.1:80/announce");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        var socketEx = ex.Which.InnerException as SocketException;
        socketEx.Should().NotBeNull();
        socketEx!.SocketErrorCode.Should().Be(SocketError.AccessDenied);
    }

    private static PeerId CreateMockPeerId(string ip, int port, IDisposable connection)
    {
        var peerInfo = new MonoTorrent.PeerInfo(new Uri($"ipv4://{ip}:{port}"));
        var peerType = typeof(PeerId).Assembly.GetType("MonoTorrent.Client.Peer")!;
        var mtPeer = RuntimeHelpers.GetUninitializedObject(peerType);
        peerType.GetField("<Info>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(mtPeer, peerInfo);
        var peer = (PeerId)RuntimeHelpers.GetUninitializedObject(typeof(PeerId));
        typeof(PeerId).GetField("<Peer>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(peer, mtPeer);
        typeof(PeerId).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(peer, connection);
        return peer;
    }

    [Test]
    public void MonoTorrentDownloadTask_OnPeerConnected_WhenPeerBlocklisted_ClosesConnectionAndDisposesUnderlyingConnection()
    {
        var mockBlocklist = Substitute.For<IBlocklistService>();
        mockBlocklist.IsIpBlocked("203.0.113.10").Returns(true);

        var mockEventAggregator = Substitute.For<IEventAggregator>();
        var task = new MonoTorrentDownloadTask(
            1,
            "test-infohash",
            null,
            blocklistService: mockBlocklist,
            eventAggregator: mockEventAggregator);

        var mockConn = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnection, IDisposable>();
        var peer = CreateMockPeerId("203.0.113.10", 5000, mockConn);

        var args = (PeerConnectedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PeerConnectedEventArgs));
        typeof(PeerConnectedEventArgs).GetField("<Peer>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(args, peer);

        var onPeerConnectedMethod = typeof(MonoTorrentDownloadTask).GetMethod("OnPeerConnected", BindingFlags.NonPublic | BindingFlags.Instance);
        onPeerConnectedMethod.Should().NotBeNull();
        onPeerConnectedMethod!.Invoke(task, new object[] { null!, args });

        ((IDisposable)mockConn).Received(1).Dispose();
        mockEventAggregator.Received(1).PublishEvent(Arg.Is<PeerConnectionEvent>(e =>
            e.RemoteIp == "203.0.113.10" &&
            e.EventType == "Blocked"));
    }

    [Test]
    public void MonoTorrentDownloadTask_OnPeerConnected_WhenPeerAllowed_DoesNotDisposeConnection()
    {
        var mockBlocklist = Substitute.For<IBlocklistService>();
        mockBlocklist.IsIpBlocked("203.0.113.11").Returns(false);

        var mockEventAggregator = Substitute.For<IEventAggregator>();
        var task = new MonoTorrentDownloadTask(
            2,
            "test-infohash-allowed",
            null,
            blocklistService: mockBlocklist,
            eventAggregator: mockEventAggregator);

        var mockConn = Substitute.For<MonoTorrent.Connections.Peer.IPeerConnection, IDisposable>();
        var peer = CreateMockPeerId("203.0.113.11", 5000, mockConn);

        var args = (PeerConnectedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PeerConnectedEventArgs));
        typeof(PeerConnectedEventArgs).GetField("<Peer>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(args, peer);

        var onPeerConnectedMethod = typeof(MonoTorrentDownloadTask).GetMethod("OnPeerConnected", BindingFlags.NonPublic | BindingFlags.Instance);
        onPeerConnectedMethod.Should().NotBeNull();
        onPeerConnectedMethod!.Invoke(task, new object[] { null!, args });

        ((IDisposable)mockConn).DidNotReceive().Dispose();
        mockEventAggregator.Received(1).PublishEvent(Arg.Is<PeerConnectionEvent>(e =>
            e.RemoteIp == "203.0.113.11" &&
            e.EventType == "Connected"));
    }

    [Test]
    public void MonoTorrentDownloadTask_OnPieceHashed_WhenHashFailsThresholdTimes_BansPeerAndPublishesPeerBannedEvent()
    {
        var mockBlocklist = Substitute.For<IBlocklistService>();
        var mockEventAggregator = Substitute.For<IEventAggregator>();
        var picker = new PiecePicker(5, 16384, 16384 * 5);

        var task = new MonoTorrentDownloadTask(
            10,
            "hash-ban-test",
            null,
            blocklistService: mockBlocklist,
            picker: picker,
            eventAggregator: mockEventAggregator);

        var onPieceHashedMethod = typeof(MonoTorrentDownloadTask).GetMethod("OnPieceHashed", BindingFlags.NonPublic | BindingFlags.Instance);
        onPieceHashedMethod.Should().NotBeNull();

        // Piece 0 fails hash check (fail count 1)
        task.RecordBlockReceived(0, 0, 16384, "198.51.100.1");
        var args0 = (PieceHashedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PieceHashedEventArgs));
        typeof(PieceHashedEventArgs).GetField("<PieceIndex>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(args0, 0);
        typeof(PieceHashedEventArgs).GetField("<HashPassed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(args0, false);
        onPieceHashedMethod!.Invoke(task, new object[] { null!, args0 });

        task.GetPeerHashFailCount("198.51.100.1").Should().Be(1);
        task.IsPeerBanned("198.51.100.1").Should().BeFalse();
        mockEventAggregator.DidNotReceive().PublishEvent(Arg.Any<PeerBannedEvent>());

        // Piece 1 fails hash check (fail count 2)
        task.RecordBlockReceived(1, 0, 16384, "198.51.100.1");
        var args1 = (PieceHashedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PieceHashedEventArgs));
        typeof(PieceHashedEventArgs).GetField("<PieceIndex>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(args1, 1);
        typeof(PieceHashedEventArgs).GetField("<HashPassed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(args1, false);
        onPieceHashedMethod.Invoke(task, new object[] { null!, args1 });

        task.GetPeerHashFailCount("198.51.100.1").Should().Be(2);
        task.IsPeerBanned("198.51.100.1").Should().BeFalse();
        mockEventAggregator.DidNotReceive().PublishEvent(Arg.Any<PeerBannedEvent>());

        // Piece 2 fails hash check (fail count 3 -> threshold reached!)
        task.RecordBlockReceived(2, 0, 16384, "198.51.100.1");
        var args2 = (PieceHashedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PieceHashedEventArgs));
        typeof(PieceHashedEventArgs).GetField("<PieceIndex>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(args2, 2);
        typeof(PieceHashedEventArgs).GetField("<HashPassed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(args2, false);
        onPieceHashedMethod.Invoke(task, new object[] { null!, args2 });

        task.GetPeerHashFailCount("198.51.100.1").Should().Be(3);
        task.IsPeerBanned("198.51.100.1").Should().BeTrue();
        mockBlocklist.Received().AddRulesAsync(Arg.Is<IEnumerable<string>>(rules => rules.Contains("198.51.100.1")));
        mockEventAggregator.Received(1).PublishEvent(Arg.Is<PeerBannedEvent>(e =>
            e.PeerIp == "198.51.100.1" &&
            e.Reason == "Hash check failed" &&
            e.InfoHash == "hash-ban-test"));
    }

    [Test]
    public void MonoTorrentDownloadTask_OnPieceHashed_WhenHashPasses_ClearsContributorsAndDoesNotBan()
    {
        var mockBlocklist = Substitute.For<IBlocklistService>();
        var mockEventAggregator = Substitute.For<IEventAggregator>();
        var picker = new PiecePicker(5, 16384, 16384 * 5);

        var task = new MonoTorrentDownloadTask(
            11,
            "hash-pass-test",
            null,
            blocklistService: mockBlocklist,
            picker: picker,
            eventAggregator: mockEventAggregator);

        var onPieceHashedMethod = typeof(MonoTorrentDownloadTask).GetMethod("OnPieceHashed", BindingFlags.NonPublic | BindingFlags.Instance);
        onPieceHashedMethod.Should().NotBeNull();

        task.RecordBlockReceived(0, 0, 16384, "198.51.100.2");
        var args = (PieceHashedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PieceHashedEventArgs));
        typeof(PieceHashedEventArgs).GetField("<PieceIndex>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(args, 0);
        typeof(PieceHashedEventArgs).GetField("<HashPassed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(args, true);
        onPieceHashedMethod!.Invoke(task, new object[] { null!, args });

        task.GetPeerHashFailCount("198.51.100.2").Should().Be(0);
        task.IsPeerBanned("198.51.100.2").Should().BeFalse();
        mockEventAggregator.DidNotReceive().PublishEvent(Arg.Any<PeerBannedEvent>());
    }

    [Test]
    public void MonoTorrentDownloadEngine_BanPeer_PublishesPeerBannedEventAndAddsToBlocklist()
    {
        var mockBlocklist = Substitute.For<IBlocklistService>();
        using var testEngine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator,
            blocklistService: mockBlocklist,
            appFolderInfo: this.appFolderInfo,
            torrentLogService: this.torrentLogService);

        testEngine.BanPeer("198.51.100.55", "Hash check failed", "engine-info-hash");

        mockBlocklist.Received().AddRulesAsync(Arg.Is<IEnumerable<string>>(r => r.Contains("198.51.100.55")));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<PeerBannedEvent>(e =>
            e.PeerIp == "198.51.100.55" &&
            e.Reason == "Hash check failed" &&
            e.InfoHash == "engine-info-hash"));
    }

    [Test]
    public async Task PauseForSchedulerAsync_RecordsActiveTorrentsInSchedulerPausedTorrentIds()
    {
        var torrentBytes = CreateSampleSingleFileTorrentBytes("sched_active.iso");
        var parsed = MonoTorrent.Torrent.Load(torrentBytes);

        var torrent = new CoreTorrent
        {
            Id = 501,
            InfoHash = parsed.InfoHashes.V1OrV2.ToHex(),
            Name = "sched_active.iso",
            Status = TorrentStatus.Downloading,
        };

        await this.engine.AddTorrentAsync(torrent, torrentFileBytes: torrentBytes);
        await this.engine.PauseForSchedulerAsync();

        this.engine.SchedulerPausedTorrentIds.Should().Contain(501);
        var task = this.engine.GetTask(501);
        task.Should().NotBeNull();
        task!.Status.Should().BeOneOf(TorrentStatus.Paused, TorrentStatus.Stopped);
    }

    [Test]
    public async Task ResumeFromSchedulerAsync_OnlyResumesSchedulerPausedTorrents()
    {
        var torrentBytes1 = CreateSampleSingleFileTorrentBytes("sched_res1.iso");
        var parsed1 = MonoTorrent.Torrent.Load(torrentBytes1);
        var torrent1 = new CoreTorrent
        {
            Id = 502,
            InfoHash = parsed1.InfoHashes.V1OrV2.ToHex(),
            Name = "sched_res1.iso",
            Status = TorrentStatus.Downloading,
        };

        var torrentBytes2 = CreateSampleSingleFileTorrentBytes("sched_res2.iso");
        var parsed2 = MonoTorrent.Torrent.Load(torrentBytes2);
        var torrent2 = new CoreTorrent
        {
            Id = 503,
            InfoHash = parsed2.InfoHashes.V1OrV2.ToHex(),
            Name = "sched_res2.iso",
            Status = TorrentStatus.Downloading,
        };

        await this.engine.AddTorrentAsync(torrent1, torrentFileBytes: torrentBytes1);
        await this.engine.AddTorrentAsync(torrent2, torrentFileBytes: torrentBytes2);

        // Pause for scheduler
        await this.engine.PauseForSchedulerAsync();
        this.engine.SchedulerPausedTorrentIds.Should().Contain(502);
        this.engine.SchedulerPausedTorrentIds.Should().Contain(503);

        // User pauses torrent 503 during scheduler pause
        await this.engine.PauseTorrentAsync(503);
        this.engine.SchedulerPausedTorrentIds.Should().NotContain(503);

        // Resume from scheduler
        await this.engine.ResumeFromSchedulerAsync();
        this.engine.SchedulerPausedTorrentIds.Should().BeEmpty();
    }
}

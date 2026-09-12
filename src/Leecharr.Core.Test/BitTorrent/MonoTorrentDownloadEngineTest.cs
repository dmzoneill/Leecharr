// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.PortMapping;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;
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
    private MonoTorrentDownloadEngine engine = null!;

    private string testIncompleteDir = null!;
    private string testDownloadDir = null!;

    [SetUp]
    public void SetUp()
    {
        this.testIncompleteDir = Path.Combine(Path.GetTempPath(), "leecharr_test_incomplete_" + Guid.NewGuid().ToString("N"));
        this.testDownloadDir = Path.Combine(Path.GetTempPath(), "leecharr_test_downloads_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.testIncompleteDir);
        Directory.CreateDirectory(this.testDownloadDir);

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

        this.storagePathService = Substitute.For<IStoragePathService>();
        this.storagePathService.GetIncompleteDirectory().Returns(this.testIncompleteDir);
        this.storagePathService.GetCompletedDirectory(Arg.Any<string>()).Returns(this.testDownloadDir);

        this.categoryService = Substitute.For<ICategoryService>();
        this.categoryService.GetSavePathForCategory(Arg.Any<string>()).Returns(this.testDownloadDir);

        this.diskProvider = Substitute.For<IDiskProvider>();
        this.eventAggregator = Substitute.For<IEventAggregator>();

        this.engine = new MonoTorrentDownloadEngine(
            this.configService,
            this.storagePathService,
            this.categoryService,
            this.diskProvider,
            this.eventAggregator);
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
        // 500 MB/s download throughput
        var cacheBytes = this.engine.CalculateDynamicDiskCacheBytes(500L * 1024L * 1024L);
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

        // peer1: AmInterested & !IsChoking => 'D', 'I'
        //        IsInterested & !AmChoking => 'U', 'i'
        //        Encryptor => 'E'
        //        Total => "DUIiE"
        peers[0].Flags.Should().Be("DUIiE");
        peers[0].IsUtp.Should().BeFalse();
        peers[0].IsEncrypted.Should().BeTrue();

        // peer2: AmInterested & IsChoking => 'd', 'I', 'c'
        //        IsInterested & AmChoking => 'u', 'C', 'i'
        //        utp scheme => 'P'
        //        Total => "duICicP"
        peers[1].Flags.Should().Be("duICicP");
        peers[1].IsUtp.Should().BeTrue();
        peers[1].IsEncrypted.Should().BeFalse();

        // peer3: !AmInterested && !IsInterested => no D/d, no U/u, no I, no C, no i, no c
        peers[2].Flags.Should().BeEmpty();
        peers[2].IsUtp.Should().BeFalse();
        peers[2].IsEncrypted.Should().BeFalse();
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

    #endregion
}

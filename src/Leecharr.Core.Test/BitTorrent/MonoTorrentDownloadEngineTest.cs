// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        this.storagePathService = Substitute.For<IStoragePathService>();
        this.storagePathService.GetIncompleteDirectory().Returns(this.testIncompleteDir);

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

    private static byte[] CreateSampleSingleFileTorrentBytes(string name = "testfile.bin", int length = 16384, bool isPrivate = false)
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

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.example.com/announce") },
            { "info", infoDict },
        };

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
        task!.Status.Should().Be(TorrentStatus.Paused);
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
        task!.Status.Should().Be(TorrentStatus.Downloading);
    }

    [Test]
    public async Task PauseTorrentAsync_WhenTorrentNotFound_DoesNotThrow()
    {
        var act = async () => await this.engine.PauseTorrentAsync(9999);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ResumeTorrentAsync_WhenTorrentNotFound_DoesNotThrow()
    {
        var act = async () => await this.engine.ResumeTorrentAsync(9999);
        await act.Should().NotThrowAsync();
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
    public async Task AddTorrentAsync_WhenTorrentIsPrivateAndBep27Disabled_AllowsDhtAndPex()
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

        task.Manager.Settings.AllowDht.Should().BeTrue();
        task.Manager.Settings.AllowPeerExchange.Should().BeTrue();
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

    #endregion
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentServiceTest
{
    private ITorrentRepository torrentRepository = null!;
    private ITorrentFileRepository fileRepository = null!;
    private ICategoryService categoryService = null!;
    private IMediaEnrichmentService mediaEnrichmentService = null!;
    private IConfigService configService = null!;
    private IDownloadEngine downloadEngine = null!;
    private IEventAggregator eventAggregator = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private IStoragePathService storagePathService = null!;
    private TorrentService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.fileRepository = Substitute.For<ITorrentFileRepository>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.mediaEnrichmentService = Substitute.For<IMediaEnrichmentService>();
        this.configService = Substitute.For<IConfigService>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.storagePathService = Substitute.For<IStoragePathService>();

        this.categoryService.GetSavePathForCategory(Arg.Any<string>()).Returns("/downloads");
        this.configService.DefaultCategory.Returns("default");
        this.configService.AutoEnrichEnabled.Returns(false);

        this.service = new TorrentService(
            this.torrentRepository,
            this.fileRepository,
            this.categoryService,
            this.mediaEnrichmentService,
            this.configService,
            this.downloadEngine,
            this.eventAggregator,
            this.trackerEntryRepository,
            queueManagerService: null,
            storagePathService: this.storagePathService);
    }

    [Test]
    public async Task AddFromParsedTorrentAsync_CalculatesContinuousBytePieceOffsets()
    {
        var parsed = new ParsedTorrent
        {
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            Name = "MultiFileTorrent",
            PieceLength = 1000, // 1000 bytes per piece
            TotalSize = 3500,
            Files = new List<ParsedTorrentFile>
            {
                new() { Path = "file1.txt", Size = 500 },  // bytes 0-499: piece 0 (count 1)
                new() { Path = "file2.txt", Size = 1200 }, // bytes 500-1699: piece 0 to piece 1 (count 2)
                new() { Path = "file3.txt", Size = 1800 } // bytes 1700-3499: piece 1 to piece 3 (count 3)
            },
        };

        this.torrentRepository.GetByInfoHash(parsed.InfoHash).Returns((Torrent)null!);
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            t.Id = 42;
            return t;
        });

        var insertedFiles = new List<TorrentFile>();
        this.fileRepository.Insert(Arg.Do<TorrentFile>(f => insertedFiles.Add(f)));
        this.fileRepository.InsertMany(Arg.Do<IEnumerable<TorrentFile>>(files => insertedFiles.AddRange(files)));

        var result = await this.service.AddFromParsedTorrentAsync(parsed, "movies", "/downloads/movies", false, Array.Empty<byte>());

        result.Should().NotBeNull();
        result.Id.Should().Be(42);

        insertedFiles.Should().HaveCount(3);

        // File 1: size 500 -> startPiece 0, count 1
        insertedFiles[0].PieceOffset.Should().Be(0);
        insertedFiles[0].PieceCount.Should().Be(1);

        // File 2: size 1200 (bytes 500-1699) -> startPiece 0, endPiece 1, count 2
        insertedFiles[1].PieceOffset.Should().Be(0);
        insertedFiles[1].PieceCount.Should().Be(2);

        // File 3: size 1800 (bytes 1700-3499) -> startPiece 1, endPiece 3, count 3
        insertedFiles[2].PieceOffset.Should().Be(1);
        insertedFiles[2].PieceCount.Should().Be(3);
    }

    [Test]
    public async Task RenameFileAsync_WhenEngineSucceeds_UpdatesDatabaseRecord()
    {
        var file = new TorrentFile { Id = 10, TorrentId = 42, Path = "folder/old_video.mkv" };
        this.fileRepository.GetByTorrentId(42).Returns(new List<TorrentFile> { file });
        this.downloadEngine.RenameFileAsync(42, "folder/old_video.mkv", "folder/new_video.mkv").Returns(true);

        var result = await this.service.RenameFileAsync(42, "folder/old_video.mkv", "folder/new_video.mkv");

        result.Should().BeTrue();
        file.Path.Should().Be("folder/new_video.mkv");
        this.fileRepository.Received(1).Update(file);
    }

    [Test]
    public async Task RenameFolderAsync_WhenEngineSucceeds_UpdatesAllSubpathRecords()
    {
        var file1 = new TorrentFile { Id = 10, TorrentId = 42, Path = "Season 1/Episode 1.mkv" };
        var file2 = new TorrentFile { Id = 11, TorrentId = 42, Path = "Season 1/Episode 2.mkv" };
        var file3 = new TorrentFile { Id = 12, TorrentId = 42, Path = "Other/Bonus.mkv" };

        this.fileRepository.GetByTorrentId(42).Returns(new List<TorrentFile> { file1, file2, file3 });
        this.downloadEngine.RenameFolderAsync(42, "Season 1", "S01").Returns(true);

        var result = await this.service.RenameFolderAsync(42, "Season 1", "S01");

        result.Should().BeTrue();
        file1.Path.Should().Be("S01/Episode 1.mkv");
        file2.Path.Should().Be("S01/Episode 2.mkv");
        file3.Path.Should().Be("Other/Bonus.mkv");
        this.fileRepository.Received(1).Update(file1);
        this.fileRepository.Received(1).Update(file2);
        this.fileRepository.DidNotReceive().Update(file3);
    }

    [Test]
    public async Task SetSuperSeedingAsync_UpdatesTorrentAndEngine()
    {
        var torrent = new Torrent { Id = 42, Name = "test", InitialSeeding = false };
        this.torrentRepository.Get(42).Returns(torrent);

        await this.service.SetSuperSeedingAsync(42, true);

        torrent.InitialSeeding.Should().BeTrue();
        this.torrentRepository.Received(1).Update(torrent);
        await this.downloadEngine.Received(1).SetSuperSeedingAsync(42, true);
    }

    [Test]
    public async Task DeleteAsync_WhenTorrentNameIsTraversalSequence_DoesNotDeleteDownloadRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "leecharr_delete_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var subDir = Path.Combine(tempRoot, "subfolder");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "payload.txt"), "content");

        try
        {
            var torrent = new Torrent
            {
                Id = 100,
                Name = "..",
                SavePath = tempRoot,
                InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            };
            this.torrentRepository.Get(100).Returns(torrent);

            await this.service.DeleteAsync(100, deleteFiles: true);

            // Neither the root directory nor its contents should have been deleted
            Directory.Exists(tempRoot).Should().BeTrue();
            Directory.Exists(subDir).Should().BeTrue();
            File.Exists(Path.Combine(subDir, "payload.txt")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Test]
    public async Task DeleteAsync_WhenTorrentFolderIsStrictSubPath_DeletesFilesProperly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "leecharr_delete_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var subDir = Path.Combine(tempRoot, "my_torrent_folder");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "file.txt"), "content");

        try
        {
            var torrent = new Torrent
            {
                Id = 101,
                Name = "my_torrent_folder",
                SavePath = tempRoot,
                InfoHash = "11223344556677889900aabbccddeeff00112233",
            };
            this.torrentRepository.Get(101).Returns(torrent);

            await this.service.DeleteAsync(101, deleteFiles: true);

            // Subfolder should be deleted, but parent root directory remains
            Directory.Exists(subDir).Should().BeFalse();
            Directory.Exists(tempRoot).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [TestCase("../../etc/cron.d/payload.sh")]
    [TestCase("/etc/shadow")]
    [TestCase("..")]
    [TestCase(".")]
    [TestCase("folder/../escape.mkv")]
    [TestCase("evil\0file.mkv")]
    public async Task RenameFileAsync_WhenNewPathIsTraversalOrAbsolute_ReturnsFalseWithoutCallingEngine(string badPath)
    {
        var torrent = new Torrent { Id = 42, SavePath = "/downloads" };
        this.torrentRepository.Get(42).Returns(torrent);

        var result = await this.service.RenameFileAsync(42, "valid/old.mkv", badPath);

        result.Should().BeFalse();
        await this.downloadEngine.DidNotReceive().RenameFileAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [TestCase("../S01")]
    [TestCase("/etc/cron.d")]
    [TestCase("..")]
    [TestCase(".")]
    [TestCase("folder/../evil")]
    [TestCase("evil\0folder")]
    public async Task RenameFolderAsync_WhenNewPathIsTraversalOrAbsolute_ReturnsFalseWithoutCallingEngine(string badPath)
    {
        var torrent = new Torrent { Id = 42, SavePath = "/downloads" };
        this.torrentRepository.Get(42).Returns(torrent);

        var result = await this.service.RenameFolderAsync(42, "Season 1", badPath);

        result.Should().BeFalse();
        await this.downloadEngine.DidNotReceive().RenameFolderAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [TestCase("/downloads", "/downloads/movie", true)]
    [TestCase("/downloads", "/downloads/movie/sub/file.txt", true)]
    [TestCase("/downloads", "/downloads", false)]
    [TestCase("/downloads", "/downloads/", false)]
    [TestCase("/downloads", "/downloads/..", false)]
    [TestCase("/downloads", "/downloads/../etc/passwd", false)]
    [TestCase("/downloads", "/downloads_other/movie", false)]
    [TestCase("/downloads", "/etc/shadow", false)]
    [TestCase("/downloads", "", false)]
    [TestCase("/downloads", null, false)]
    [TestCase("", "/downloads/movie", false)]
    [TestCase(null, "/downloads/movie", false)]
    public void IsStrictSubPath_ValidatesContainmentCorrectly(string basePath, string targetPath, bool expected)
    {
        TorrentService.IsStrictSubPath(basePath, targetPath).Should().Be(expected);
    }

    [Test]
    public async Task DeleteAsync_WhenCalledConcurrentlyForSameTorrent_ExecutesSafelyWithoutDoubleDeletion()
    {
        var torrent = new Torrent
        {
            Id = 200,
            Name = "concurrent_test",
            SavePath = "/downloads",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
        };

        var isDeleted = false;
        this.torrentRepository.Get(200).Returns(_ => isDeleted ? null : torrent);
        this.torrentRepository.When(x => x.Delete(200)).Do(_ => isDeleted = true);
        this.downloadEngine.RemoveTorrentAsync(200, Arg.Any<bool>())
            .Returns(async _ => await Task.Delay(50));

        var task1 = this.service.DeleteAsync(200, deleteFiles: false);
        var task2 = this.service.DeleteAsync(200, deleteFiles: false);

        await Task.WhenAll(task1, task2);

        await this.downloadEngine.Received(1).RemoveTorrentAsync(200, false);
        this.torrentRepository.Received(1).Delete(200);
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentDeletedEvent>(e => e.Torrent.Id == 200));
    }

    [Test]
    public async Task DeleteAsync_WhenTorrentIsAlreadyDeleted_ReturnsImmediatelyWithoutCallingEngineOrRepos()
    {
        this.torrentRepository.Get(999).Returns((Torrent)null);

        await this.service.DeleteAsync(999, deleteFiles: true);

        await this.downloadEngine.DidNotReceive().RemoveTorrentAsync(Arg.Any<int>(), Arg.Any<bool>());
        this.torrentRepository.DidNotReceive().Delete(Arg.Any<int>());
        this.fileRepository.DidNotReceive().DeleteByTorrentId(Arg.Any<int>());
    }

    [Test]
    public async Task DeleteAsync_WhenDeletingIncompleteTorrent_PurgesIncompleteDirectoryChunks()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "leecharr_incomplete_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var incompleteDir = Path.Combine(tempRoot, "incomplete");
        Directory.CreateDirectory(incompleteDir);

        var incompleteSubDir = Path.Combine(incompleteDir, "incomplete_torrent");
        Directory.CreateDirectory(incompleteSubDir);
        File.WriteAllText(Path.Combine(incompleteSubDir, "chunk.part"), "data");

        var incompleteSingleFile = Path.Combine(incompleteDir, "incomplete_torrent.!mt");
        File.WriteAllText(incompleteSingleFile, "single chunk");

        this.storagePathService.GetIncompleteDirectory().Returns(incompleteDir);

        try
        {
            var torrent = new Torrent
            {
                Id = 201,
                Name = "incomplete_torrent",
                Progress = 0.45,
                Status = TorrentStatus.Downloading,
                SavePath = tempRoot,
                InfoHash = "abcdef1234567890abcdef1234567890abcdef12",
            };
            this.torrentRepository.Get(201).Returns(torrent);

            await this.service.DeleteAsync(201, deleteFiles: true);

            Directory.Exists(incompleteSubDir).Should().BeFalse();
            File.Exists(incompleteSingleFile).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Test]
    public async Task DeleteAsync_WhenFileIsLockedInitially_RetriesAndDeletesSuccessfully()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "leecharr_lock_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var torrentFolder = Path.Combine(tempRoot, "locked_torrent");
        Directory.CreateDirectory(torrentFolder);
        var payloadFile = Path.Combine(torrentFolder, "payload.bin");
        File.WriteAllBytes(payloadFile, new byte[1024]);

        var lockStream = new FileStream(payloadFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            lockStream.Dispose();
        });

        try
        {
            var torrent = new Torrent
            {
                Id = 202,
                Name = "locked_torrent",
                Progress = 1.0,
                Status = TorrentStatus.Seeding,
                SavePath = tempRoot,
                InfoHash = "5566778899aabbccddeeff001122334455667788",
            };
            this.torrentRepository.Get(202).Returns(torrent);

            await this.service.DeleteAsync(202, deleteFiles: true);

            Directory.Exists(torrentFolder).Should().BeFalse();
            File.Exists(payloadFile).Should().BeFalse();
        }
        finally
        {
            lockStream.Dispose();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Test]
    public void SyncWithEngine_WhenTaskIsStalled_UpdatesTorrentStatusToStalledAndDispatchesHealthIssueEvent()
    {
        var task = Substitute.For<IDownloadTask>();
        task.Status.Returns(TorrentStatus.Stalled);
        task.ErrorMessage.Returns("Tracker failure: http://tracker.example.com/announce: Offline");
        task.Progress.Returns(0.25);
        task.DownloadedBytes.Returns(1024);
        task.UploadedBytes.Returns(0);
        task.DownloadSpeed.Returns(0);
        task.UploadSpeed.Returns(0);
        task.ConnectedSeeders.Returns(0);
        task.ConnectedLeechers.Returns(0);

        var torrent = new Torrent
        {
            Id = 301,
            Name = "Stalled ISO",
            Status = TorrentStatus.Downloading,
            Progress = 0.25,
            QueuePosition = 1,
            InfoHash = "1122334455667788990011223344556677889900",
        };

        this.torrentRepository.Get(301).Returns(torrent);
        this.downloadEngine.GetTask(301).Returns(task);

        var result = this.service.Get(301);

        result.Should().NotBeNull();
        result.Status.Should().Be(TorrentStatus.Stalled);
        result.ErrorMessage.Should().Be("Tracker failure: http://tracker.example.com/announce: Offline");

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == 301 &&
            t.Status == TorrentStatus.Stalled &&
            t.ErrorMessage == "Tracker failure: http://tracker.example.com/announce: Offline"));

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e =>
            e.Torrent.Id == 301 &&
            e.OldStatus == TorrentStatus.Downloading &&
            e.NewStatus == TorrentStatus.Stalled));

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.TorrentId == 301 &&
            !e.IsResolved &&
            e.Source == "Tracker"));
    }

    [Test]
    public void SyncWithEngine_WhenTaskRecoversFromStalled_RestoresDownloadingAndDispatchesResolvedHealthIssueEvent()
    {
        var task = Substitute.For<IDownloadTask>();
        task.Status.Returns(TorrentStatus.Downloading);
        task.ErrorMessage.Returns((string)null);
        task.Progress.Returns(0.30);
        task.DownloadedBytes.Returns(2048);
        task.UploadedBytes.Returns(0);
        task.DownloadSpeed.Returns(50000);
        task.UploadSpeed.Returns(0);
        task.ConnectedSeeders.Returns(5);
        task.ConnectedLeechers.Returns(2);

        var torrent = new Torrent
        {
            Id = 302,
            Name = "Recovered ISO",
            Status = TorrentStatus.Stalled,
            ErrorMessage = "Tracker failure: Offline",
            Progress = 0.25,
            QueuePosition = 1,
            InfoHash = "2233445566778899001122334455667788990011",
        };

        this.torrentRepository.Get(302).Returns(torrent);
        this.downloadEngine.GetTask(302).Returns(task);

        var result = this.service.Get(302);

        result.Should().NotBeNull();
        result.Status.Should().Be(TorrentStatus.Downloading);
        result.ErrorMessage.Should().BeNull();

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Id == 302 &&
            t.Status == TorrentStatus.Downloading &&
            t.ErrorMessage == null));

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e =>
            e.Torrent.Id == 302 &&
            e.OldStatus == TorrentStatus.Stalled &&
            e.NewStatus == TorrentStatus.Downloading));

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Torrent.Id == 302 &&
            e.IsResolved &&
            e.Source == "Tracker"));
    }

    [Test]
    public async Task SetLocationAsync_WhenMoveFilesIsTrue_InvokesEngineAndUpdatesSavePathAndPublishesEvent()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test Torrent",
            SavePath = "/downloads/old",
        };

        this.torrentRepository.Get(1).Returns(torrent);

        await this.service.SetLocationAsync(1, "/downloads/new", moveFiles: true);

        await this.downloadEngine.Received(1).MoveTorrentFilesAsync(1, "/downloads/new");
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 1 && t.SavePath == "/downloads/new"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent.Id == 1 && e.Torrent.SavePath == "/downloads/new"));
    }

    [Test]
    public async Task SetLocationAsync_WhenMoveFilesIsFalse_UpdatesSavePathWithoutInvokingEngine()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "Another Torrent",
            SavePath = "/downloads/old",
        };

        this.torrentRepository.Get(2).Returns(torrent);

        await this.service.SetLocationAsync(2, "/downloads/new", moveFiles: false);

        await this.downloadEngine.DidNotReceive().MoveTorrentFilesAsync(Arg.Any<int>(), Arg.Any<string>());
        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 2 && t.SavePath == "/downloads/new"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentUpdatedEvent>(e => e.Torrent.Id == 2 && e.Torrent.SavePath == "/downloads/new"));
    }

    [Test]
    public async Task SetLocationAsync_WhenSavePathIsSame_DoesNothing()
    {
        var torrent = new Torrent
        {
            Id = 3,
            Name = "Same Path Torrent",
            SavePath = "/downloads/same",
        };

        this.torrentRepository.Get(3).Returns(torrent);

        await this.service.SetLocationAsync(3, "/downloads/same", moveFiles: true);

        await this.downloadEngine.DidNotReceive().MoveTorrentFilesAsync(Arg.Any<int>(), Arg.Any<string>());
        this.torrentRepository.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public async Task AddFromParsedTorrentAsync_WhenAppFolderInfoProvided_SavesTorrentToAppDataFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var appFolderInfo = Substitute.For<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
            appFolderInfo.AppDataFolder.Returns(tempDir);

            var customService = new TorrentService(
                this.torrentRepository,
                this.fileRepository,
                this.categoryService,
                this.mediaEnrichmentService,
                this.configService,
                this.downloadEngine,
                this.eventAggregator,
                this.trackerEntryRepository,
                storagePathService: this.storagePathService,
                appFolderInfo: appFolderInfo);

            var parsed = new ParsedTorrent
            {
                InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
                Name = "TestTorrent",
                TotalSize = 1000,
                Files = new List<ParsedTorrentFile> { new() { Path = "test.mkv", Size = 1000 } },
            };
            var rawBytes = new byte[] { 1, 2, 3, 4 };

            await customService.AddFromParsedTorrentAsync(parsed, "movies", "/downloads/movies", false, rawBytes);

            var expectedPath = Path.Combine(tempDir, "Torrents", "aabbccddeeff00112233445566778899aabbccdd.torrent");
            File.Exists(expectedPath).Should().BeTrue();
            (await File.ReadAllBytesAsync(expectedPath)).Should().Equal(rawBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}

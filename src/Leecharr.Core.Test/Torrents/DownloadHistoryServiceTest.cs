// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class DownloadHistoryServiceTest
{
    private IDownloadHistoryRepository historyRepository = null!;
    private ITorrentRepository torrentRepository = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private IDownloadEngine downloadEngine = null!;
    private IEventAggregator eventAggregator = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private ICategoryService categoryService = null!;
    private IStoragePathService storagePathService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ITorrentFileRepository fileRepository = null!;
    private IConfigService configService = null!;
    private DownloadHistoryService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.historyRepository = Substitute.For<IDownloadHistoryRepository>();
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.storagePathService = Substitute.For<IStoragePathService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.fileRepository = Substitute.For<ITorrentFileRepository>();
        this.configService = Substitute.For<IConfigService>();

        this.configService.DefaultCategory.Returns("movies");
        this.configService.DownloadDir.Returns("/downloads");
        this.categoryService.GetSavePathForCategory(Arg.Any<string>(), Arg.Any<string>())
            .Returns(args => $"/downloads/{(string)args[0]}");

        this.service = new DownloadHistoryService(
            this.historyRepository,
            this.torrentRepository,
            this.trackerEntryRepository,
            this.downloadEngine,
            this.eventAggregator,
            this.safeHttpClientService,
            this.categoryService,
            this.storagePathService,
            this.torrentFileParser,
            this.fileRepository,
            this.configService);
    }

    [Test]
    public void RecordTorrentAdded_InsertsNewHistoryEntry()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test.Movie.2024.1080p",
            InfoHash = "abcdef0123456789",
            TotalSize = 1024 * 1024 * 500,
            Category = "movies",
            Progress = 0.5,
            Uploaded = 100,
            Downloaded = 50,
            Ratio = 2.0,
        };

        this.historyRepository.FindByInfoHash(torrent.InfoHash).Returns((DownloadHistory)null!);
        this.historyRepository.Insert(Arg.Any<DownloadHistory>()).Returns(args => (DownloadHistory)args[0]);

        var result = this.service.RecordTorrentAdded(torrent, source: "Radarr");

        result.Should().NotBeNull();
        result.Title.Should().Be("Test.Movie.2024.1080p");
        result.InfoHash.Should().Be("abcdef0123456789");
        result.Source.Should().Be("Radarr");
        result.Status.Should().Be("Active");
    }

    [Test]
    public void RecordTorrentRemoved_UpdatesHistoryStatusToRemoved()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Test.Show.S01E01",
            InfoHash = "1234567890abcdef",
            TotalSize = 1000,
            Progress = 1.0,
            Uploaded = 2000,
            Downloaded = 1000,
            Ratio = 2.0,
        };

        var existing = new DownloadHistory
        {
            Id = 10,
            TorrentId = 1,
            InfoHash = "1234567890abcdef",
            Title = "Test.Show.S01E01",
            Status = "Active",
        };

        this.historyRepository.FindByTorrentId(1).Returns(existing);

        this.service.RecordTorrentRemoved(torrent, "Deleted from library");

        existing.Status.Should().Be("Removed");
        existing.RemovalReason.Should().Be("Deleted from library");
        existing.TorrentId.Should().BeNull();
        this.historyRepository.Received(1).Update(existing);
    }

    [Test]
    public void RecordTorrentRemoved_WhenTrackersQueryIsEmpty_PreservesExistingTrackersAndPrimaryTracker()
    {
        var torrent = new Torrent
        {
            Id = 401,
            Name = "PreserveTrackersTest",
            InfoHash = "trackershash123",
        };

        var existing = new DownloadHistory
        {
            Id = 50,
            TorrentId = 401,
            InfoHash = "trackershash123",
            Title = "PreserveTrackersTest",
            Status = "Active",
            PrimaryTracker = "udp://existing-tracker.org:1337/announce",
            Trackers = new List<string> { "udp://existing-tracker.org:1337/announce", "http://existing-tracker2.org/announce" },
        };

        this.historyRepository.FindByTorrentId(401).Returns(existing);
        this.trackerEntryRepository.GetByTorrentId(401).Returns(new List<TrackerEntry>());

        this.service.RecordTorrentRemoved(torrent, "Deleted from library");

        existing.Status.Should().Be("Removed");
        existing.Trackers.Should().NotBeNull();
        existing.Trackers.Should().HaveCount(2);
        existing.Trackers.Should().Contain("udp://existing-tracker.org:1337/announce");
        existing.Trackers.Should().Contain("http://existing-tracker2.org/announce");
        existing.PrimaryTracker.Should().Be("udp://existing-tracker.org:1337/announce");
        this.historyRepository.Received(1).Update(existing);
    }

    [Test]
    public void RecordTorrentAdded_WhenTrackersQueryIsEmpty_PreservesExistingTrackers()
    {
        var torrent = new Torrent
        {
            Id = 402,
            Name = "PreserveTrackersAdded",
            InfoHash = "trackershash456",
        };

        var existing = new DownloadHistory
        {
            Id = 51,
            TorrentId = 402,
            InfoHash = "trackershash456",
            Title = "PreserveTrackersAdded",
            Status = "Removed",
            PrimaryTracker = "udp://existing-tracker.org:1337/announce",
            Trackers = new List<string> { "udp://existing-tracker.org:1337/announce" },
        };

        this.historyRepository.FindByInfoHash("trackershash456").Returns(existing);
        this.trackerEntryRepository.GetByTorrentId(402).Returns(new List<TrackerEntry>());

        this.service.RecordTorrentAdded(torrent);

        existing.Status.Should().Be("Active");
        existing.Trackers.Should().HaveCount(1);
        existing.Trackers[0].Should().Be("udp://existing-tracker.org:1337/announce");
        existing.PrimaryTracker.Should().Be("udp://existing-tracker.org:1337/announce");
        this.historyRepository.Received(1).Update(existing);
    }

    [Test]
    public void ReAdd_ThrowsWhenAlreadyInLibrary()
    {
        var history = new DownloadHistory
        {
            Id = 5,
            InfoHash = "dup123",
            Title = "Duplicate Release",
        };

        this.historyRepository.Get(5).Returns(history);
        this.torrentRepository.ExistsByInfoHash("dup123").Returns(true);

        Action act = () => this.service.ReAdd(5);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already in the active library*");
    }

    [Test]
    public void ReAdd_InsertsTorrentAndUpdatesHistory()
    {
        var history = new DownloadHistory
        {
            Id = 5,
            InfoHash = "unique123",
            Title = "Unique Release",
            TotalSize = 5000,
            PrimaryTracker = "udp://tracker.opentrackr.org:1337/announce",
        };

        this.historyRepository.Get(5).Returns(history);
        this.torrentRepository.ExistsByInfoHash("unique123").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 42;
            return t;
        });

        var added = this.service.ReAdd(5);

        added.Should().NotBeNull();
        added.Id.Should().Be(42);
        added.InfoHash.Should().Be("unique123");
        added.SavePath.Should().Be("/downloads/movies");
        added.Category.Should().Be("movies");
        history.TorrentId.Should().Be(42);
        history.Status.Should().Be("Active");
        this.historyRepository.Received(1).Update(history);
    }

    [Test]
    public void ReAdd_ResolvesSavePath_AndDoesNotPolluteCategoryWithIndexerSource()
    {
        var history = new DownloadHistory
        {
            Id = 15,
            InfoHash = "indexerhash999",
            Title = "Indexer Release",
            TotalSize = 8000,
            Source = "1337x",
            PrimaryTracker = "udp://tracker.opentrackr.org:1337/announce",
        };

        this.historyRepository.Get(15).Returns(history);
        this.torrentRepository.ExistsByInfoHash("indexerhash999").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.categoryService.GetByName("1337x").Returns((Category)null!);
        this.configService.DefaultCategory.Returns("movies");
        this.categoryService.GetSavePathForCategory("movies", "/downloads").Returns("/downloads/movies");

        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 99;
            return t;
        });

        var added = this.service.ReAdd(15);

        added.Should().NotBeNull();
        added.Category.Should().Be("movies");
        added.SavePath.Should().Be("/downloads/movies");
    }

    [Test]
    public void ReAdd_PreservesValidExistingCategory()
    {
        var history = new DownloadHistory
        {
            Id = 16,
            InfoHash = "tvshowhash123",
            Title = "TV Show Release",
            TotalSize = 12000,
            Source = "tv",
            PrimaryTracker = "udp://tracker.opentrackr.org:1337/announce",
        };

        this.historyRepository.Get(16).Returns(history);
        this.torrentRepository.ExistsByInfoHash("tvshowhash123").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.categoryService.GetByName("tv").Returns(new Category { Id = 2, Name = "tv", SavePath = "/media/tv" });
        this.categoryService.GetSavePathForCategory("tv", Arg.Any<string>()).Returns("/media/tv");

        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 101;
            return t;
        });

        var added = this.service.ReAdd(16);

        added.Should().NotBeNull();
        added.Category.Should().Be("tv");
        added.SavePath.Should().Be("/media/tv");
    }

    [Test]
    public void ReAdd_WhenCompletedInHistory_WhenFilesMissingFromDisk_SetsDownloadingStatusAndZeroProgress()
    {
        var history = new DownloadHistory
        {
            Id = 17,
            InfoHash = "completedhash555",
            Title = "Completed Show",
            TotalSize = 50000,
            Status = "Completed",
            DateCompleted = DateTime.UtcNow.AddDays(-1),
            Downloaded = 50000,
            Uploaded = 100000,
            Ratio = 2.0,
        };

        this.historyRepository.Get(17).Returns(history);
        this.torrentRepository.ExistsByInfoHash("completedhash555").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 102;
            return t;
        });

        var added = this.service.ReAdd(17);

        added.Should().NotBeNull();
        added.Status.Should().Be(TorrentStatus.Downloading);
        added.Progress.Should().Be(0.0);
        added.Downloaded.Should().Be(0);
        added.DateCompleted.Should().BeNull();
    }

    [Test]
    public void ReAdd_WhenCompletedInHistory_WhenFilesExistOnDisk_SetsSeedingStatusAndFullProgress()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var payloadFile = Path.Combine(tempDir, "Completed Show");
            File.WriteAllText(payloadFile, "completed payload content");

            this.categoryService.GetSavePathForCategory(Arg.Any<string>(), Arg.Any<string>())
                .Returns(tempDir);

            var history = new DownloadHistory
            {
                Id = 17,
                InfoHash = "completedhash555",
                Title = "Completed Show",
                TotalSize = 50000,
                Status = "Completed",
                DateCompleted = DateTime.UtcNow.AddDays(-1),
                Downloaded = 50000,
                Uploaded = 100000,
                Ratio = 2.0,
            };

            this.historyRepository.Get(17).Returns(history);
            this.torrentRepository.ExistsByInfoHash("completedhash555").Returns(false);
            this.torrentRepository.All().Returns(new List<Torrent>());
            this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
            {
                var t = (Torrent)args[0];
                t.Id = 102;
                return t;
            });

            var added = this.service.ReAdd(17);

            added.Should().NotBeNull();
            added.Status.Should().Be(TorrentStatus.Seeding);
            added.Progress.Should().Be(1.0);
            added.Downloaded.Should().Be(50000);
            added.DateCompleted.Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task ReAddAsync_WithTorrentBytes_ParsesFilesAndInsertsTorrentFileRecords()
    {
        var history = new DownloadHistory
        {
            Id = 18,
            InfoHash = "parsedbyteshash",
            Title = "Parsed Torrent Release",
            TotalSize = 10000,
            DownloadUrl = "https://tracker.example.com/download/test.torrent",
        };

        var fakeBytes = new byte[] { 0x64, 0x38, 0x3a, 0x61, 0x6e, 0x6e };
        var parsed = new ParsedTorrent
        {
            Name = "Parsed Torrent Release",
            InfoHash = "parsedbyteshash",
            TotalSize = 10000,
            PieceCount = 10,
            PieceLength = 1000,
            AnnounceUrl = "udp://tracker.open.org:1337/announce",
            Files = new List<ParsedTorrentFile>
            {
                new ParsedTorrentFile { Path = "Parsed Torrent Release/video.mkv", Size = 9000 },
                new ParsedTorrentFile { Path = "Parsed Torrent Release/sample.nfo", Size = 1000 },
            },
        };

        this.historyRepository.Get(18).Returns(history);
        this.torrentRepository.ExistsByInfoHash("parsedbyteshash").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.safeHttpClientService.DownloadBytesAsync("https://tracker.example.com/download/test.torrent")
            .Returns(Task.FromResult(fakeBytes));
        this.torrentFileParser.Parse(fakeBytes).Returns(parsed);

        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 103;
            return t;
        });

        var added = await this.service.ReAddAsync(18);

        added.Should().NotBeNull();
        added.Id.Should().Be(103);
        added.PieceCount.Should().Be(10);
        added.PieceLength.Should().Be(1000);
        this.fileRepository.Received(1).InsertMany(Arg.Is<List<TorrentFile>>(files =>
            files.Count == 2 &&
            files[0].Path == "Parsed Torrent Release/video.mkv" &&
            files[0].Size == 9000 &&
            files[0].Priority == 3 &&
            files[1].Path == "Parsed Torrent Release/sample.nfo" &&
            files[1].Size == 1000 &&
            files[1].Priority == 3));
    }

    [Test]
    public async Task ReAddAsync_WithDownloadUrl_DownloadsTorrentBytesAndPassesToEngine()
    {
        var history = new DownloadHistory
        {
            Id = 6,
            InfoHash = "dlurlhash123",
            Title = "DownloadUrl Release",
            TotalSize = 10000,
            DownloadUrl = "https://tracker.example.com/download/test.torrent",
            MagnetUrl = null,
        };

        var fakeBytes = new byte[] { 0x64, 0x38, 0x3a };

        this.historyRepository.Get(6).Returns(history);
        this.torrentRepository.ExistsByInfoHash("dlurlhash123").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 43;
            return t;
        });

        this.safeHttpClientService.DownloadBytesAsync("https://tracker.example.com/download/test.torrent")
            .Returns(Task.FromResult(fakeBytes));

        var added = await this.service.ReAddAsync(6);

        added.Should().NotBeNull();
        added.Id.Should().Be(43);
        await this.downloadEngine.Received(1).AddTorrentAsync(added, fakeBytes, null);
    }

    [Test]
    public async Task ReAddAsync_WithDownloadUrl_WhenDownloadFails_FallsBackToConstructedMagnet()
    {
        var history = new DownloadHistory
        {
            Id = 7,
            InfoHash = "fallbackhash123",
            Title = "Fallback Release",
            TotalSize = 20000,
            DownloadUrl = "https://tracker.example.com/download/missing.torrent",
            MagnetUrl = null,
            PrimaryTracker = "udp://tracker.open.org:1337",
        };

        this.historyRepository.Get(7).Returns(history);
        this.torrentRepository.ExistsByInfoHash("fallbackhash123").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 44;
            return t;
        });

        this.safeHttpClientService.DownloadBytesAsync("https://tracker.example.com/download/missing.torrent")
            .ThrowsAsync(new HttpRequestException("404 Not Found"));

        var added = await this.service.ReAddAsync(7);

        added.Should().NotBeNull();
        await this.downloadEngine.Received(1).AddTorrentAsync(
            added,
            null,
            Arg.Is<string>(m => m.Contains("magnet:?xt=urn:btih:fallbackhash123") && m.Contains("tr=udp%3A%2F%2Ftracker.open.org%3A1337")));
    }

    [Test]
    public async Task ReAddAsync_WithMagnetUrl_PassesMagnetToEngineDirectly()
    {
        var history = new DownloadHistory
        {
            Id = 8,
            InfoHash = "magurlhash123",
            Title = "Magnet Release",
            MagnetUrl = "magnet:?xt=urn:btih:magurlhash123&dn=Magnet%20Release",
            DownloadUrl = "https://tracker.example.com/download/not_used.torrent",
        };

        this.historyRepository.Get(8).Returns(history);
        this.torrentRepository.ExistsByInfoHash("magurlhash123").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 45;
            return t;
        });

        var added = await this.service.ReAddAsync(8);

        added.Should().NotBeNull();
        await this.safeHttpClientService.DidNotReceiveWithAnyArgs().DownloadBytesAsync(Arg.Any<string>());
        await this.downloadEngine.Received(1).AddTorrentAsync(added, null, history.MagnetUrl);
    }

    [Test]
    public void RecordTorrentAdded_WithIndexerAttribution_UpdatesExistingEntry()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "Grabbed.Release.1080p",
            InfoHash = "grabbedhash123",
            TotalSize = 1000,
        };

        var existing = new DownloadHistory
        {
            Id = 20,
            TorrentId = 1,
            InfoHash = "grabbedhash123",
            Title = "Grabbed.Release.1080p",
            Source = "Manual",
            IndexerName = null,
            DownloadUrl = null,
            MagnetUrl = null,
            Status = "Active",
        };

        this.historyRepository.FindByInfoHash("grabbedhash123").Returns(existing);

        var result = this.service.RecordTorrentAdded(
            torrent,
            source: "Prowlarr (TrackerName)",
            magnetUrl: "magnet:?xt=urn:btih:grabbedhash123",
            downloadUrl: "https://prowlarr.local/dl/1",
            indexerName: "Prowlarr (TrackerName)");

        result.Should().BeSameAs(existing);
        existing.Source.Should().Be("Prowlarr (TrackerName)");
        existing.IndexerName.Should().Be("Prowlarr (TrackerName)");
        existing.DownloadUrl.Should().Be("https://prowlarr.local/dl/1");
        existing.MagnetUrl.Should().Be("magnet:?xt=urn:btih:grabbedhash123");
        this.historyRepository.Received(1).Update(existing);
    }

    [Test]
    public void RecordTorrentAdded_WithIndexerAttribution_InsertsNewEntryWithIndexer()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "New.Release.1080p",
            InfoHash = "newhash456",
            TotalSize = 2000,
        };

        this.historyRepository.FindByInfoHash("newhash456").Returns((DownloadHistory)null!);
        this.historyRepository.Insert(Arg.Any<DownloadHistory>()).Returns(args => (DownloadHistory)args[0]);

        var result = this.service.RecordTorrentAdded(
            torrent,
            source: "TorznabTracker",
            magnetUrl: null,
            downloadUrl: "https://torznab.local/dl/2",
            indexerName: "TorznabTracker");

        result.Should().NotBeNull();
        result.Source.Should().Be("TorznabTracker");
        result.IndexerName.Should().Be("TorznabTracker");
        result.DownloadUrl.Should().Be("https://torznab.local/dl/2");
        result.MagnetUrl.Should().BeNull();
        this.historyRepository.Received(1).Insert(Arg.Is<DownloadHistory>(h =>
            h.IndexerName == "TorznabTracker" &&
            h.DownloadUrl == "https://torznab.local/dl/2" &&
            h.Source == "TorznabTracker"));
    }

    [Test]
    public async Task ReAddTorrentAsync_WhenAppFolderInfoProvided_ReadsCachedTorrentFromAppDataFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr_hist_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var torrentsDir = Path.Combine(tempDir, "Torrents");
            Directory.CreateDirectory(torrentsDir);
            var hash = "11223344556677889900aabbccddeeff11223344";
            var torrentFile = Path.Combine(torrentsDir, $"{hash}.torrent");
            var expectedBytes = new byte[] { 9, 8, 7, 6 };
            await File.WriteAllBytesAsync(torrentFile, expectedBytes);

            var appFolderInfo = Substitute.For<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
            appFolderInfo.AppDataFolder.Returns(tempDir);

            var customService = new DownloadHistoryService(
                this.historyRepository,
                this.torrentRepository,
                this.trackerEntryRepository,
                this.downloadEngine,
                this.eventAggregator,
                this.safeHttpClientService,
                appFolderInfo: appFolderInfo);

            var entry = new DownloadHistory
            {
                Id = 10,
                InfoHash = hash,
                Title = "Test ReAdd",
            };

            this.historyRepository.Get(10).Returns(entry);
            this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(call =>
            {
                var t = (Torrent)call[0];
                t.Id = 100;
                return t;
            });

            var result = await customService.ReAddAsync(10);

            result.Should().NotBeNull();
            await this.downloadEngine.Received(1).AddTorrentAsync(Arg.Any<Torrent>(), Arg.Is<byte[]>(b => b.Length == 4), Arg.Any<string>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task ReAddAsync_PreservesAllTrackers_AndInsertsAllTrackersIntoRepositoryAndEngine()
    {
        var trackers = new List<string>
        {
            "udp://tracker1.example.com:1337/announce",
            "udp://tracker2.example.com:6969/announce",
            "http://tracker3.example.com/announce",
        };

        var history = new DownloadHistory
        {
            Id = 99,
            InfoHash = "multitrackerhash123",
            Title = "Multi Tracker Release",
            TotalSize = 5000,
            PrimaryTracker = trackers[0],
            Trackers = trackers,
            IsPrivate = false,
        };

        this.historyRepository.Get(99).Returns(history);
        this.torrentRepository.ExistsByInfoHash("multitrackerhash123").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.trackerEntryRepository.GetByTorrentId(77).Returns(trackers.Select(u => new TrackerEntry { TorrentId = 77, Url = u }).ToList());
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 77;
            return t;
        });

        var added = await this.service.ReAddAsync(99);

        added.Should().NotBeNull();
        added.Id.Should().Be(77);

        // Verify all trackers inserted into trackerEntryRepository
        this.trackerEntryRepository.Received(1).InsertMany(Arg.Is<List<TrackerEntry>>(list =>
            list.Count == 3 &&
            list[0].Url == trackers[0] &&
            list[1].Url == trackers[1] &&
            list[2].Url == trackers[2]));

        // Verify all trackers passed to download engine
        await this.downloadEngine.Received(1).AddTrackersAsync(77, Arg.Is<List<string>>(list =>
            list.Count == 3 &&
            list.Contains(trackers[0]) &&
            list.Contains(trackers[1]) &&
            list.Contains(trackers[2])));
    }

    [Test]
    public async Task ReAddAsync_PreservesIsPrivateBep27Flag_AndSetsEnginePrivateStatus()
    {
        var history = new DownloadHistory
        {
            Id = 100,
            InfoHash = "privatetrackerhash123",
            Title = "Private Tracker Release",
            TotalSize = 10000,
            PrimaryTracker = "https://private-tracker.org/announce?passkey=secret123",
            Trackers = new List<string> { "https://private-tracker.org/announce?passkey=secret123" },
            IsPrivate = true,
        };

        this.historyRepository.Get(100).Returns(history);
        this.torrentRepository.ExistsByInfoHash("privatetrackerhash123").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 88;
            return t;
        });

        var added = await this.service.ReAddAsync(100);

        added.Should().NotBeNull();
        added.Id.Should().Be(88);
        added.IsPrivate.Should().BeTrue();

        this.torrentRepository.Received(1).Insert(Arg.Is<Torrent>(t => t.IsPrivate == true));
        await this.downloadEngine.Received(1).SetTorrentPrivateStatusAsync(88, true);
    }

    [Test]
    public async Task ReAddAsync_WhenEngineThrows_RollsBackDatabaseRecordsAndRethrows()
    {
        var history = new DownloadHistory
        {
            Id = 20,
            InfoHash = "enginefailhash123",
            Title = "Engine Fail Release",
            TotalSize = 25000,
            Status = "Removed",
            TorrentId = null,
            DateRemoved = DateTime.UtcNow.AddDays(-2),
            Trackers = new List<string> { "udp://tracker.test.org:1337/announce" },
            PrimaryTracker = "udp://tracker.test.org:1337/announce",
        };

        this.historyRepository.Get(20).Returns(history);
        this.torrentRepository.ExistsByInfoHash("enginefailhash123").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 999;
            return t;
        });

        this.downloadEngine.AddTorrentAsync(Arg.Any<Torrent>(), Arg.Any<byte[]>(), Arg.Any<string>())
            .ThrowsAsync(new IOException("Disk is full"));

        Func<Task> act = async () => await this.service.ReAddAsync(20);

        await act.Should().ThrowAsync<IOException>().WithMessage("Disk is full");

        this.trackerEntryRepository.Received(1).DeleteByTorrentId(999);
        this.fileRepository.Received(1).DeleteByTorrentId(999);
        this.torrentRepository.Received(1).Delete(999);

        history.TorrentId.Should().BeNull();
        history.Status.Should().Be("Removed");
        history.DateRemoved.Should().NotBeNull();
        this.historyRepository.Received().Update(Arg.Is<DownloadHistory>(h =>
            h.Id == 20 && h.TorrentId == null && h.Status == "Removed"));

        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<TorrentAddedEvent>());
    }

    [Test]
    public void GetAll_ReturnsPagedHistory()
    {
        var records = new List<DownloadHistory>
        {
            new DownloadHistory { Id = 1, Title = "Movie 1", Status = "Active" },
            new DownloadHistory { Id = 2, Title = "Movie 2", Status = "Active" },
        };

        this.historyRepository.GetHistory("movie", "Active", 25, 50).Returns(records);

        var result = this.service.GetAll("movie", "Active", 25, 50);

        result.Should().BeSameAs(records);
        this.historyRepository.Received(1).GetHistory("movie", "Active", 25, 50);
    }

    [Test]
    public void Get_ReturnsEntryById()
    {
        var entry = new DownloadHistory { Id = 10, Title = "Title 10" };
        this.historyRepository.Get(10).Returns(entry);

        var result = this.service.Get(10);

        result.Should().BeSameAs(entry);
        this.historyRepository.Received(1).Get(10);
    }

    [Test]
    public void GetByInfoHash_ReturnsEntryByInfoHash()
    {
        var entry = new DownloadHistory { Id = 10, InfoHash = "hash123" };
        this.historyRepository.FindByInfoHash("hash123").Returns(entry);

        var result = this.service.GetByInfoHash("hash123");

        result.Should().BeSameAs(entry);
        this.historyRepository.Received(1).FindByInfoHash("hash123");
    }

    [Test]
    public void Delete_WhenTorrentExistsInLibrary_DoesNotDeleteTorrentFile_AndDeletesHistoryRecord()
    {
        var entry = new DownloadHistory { Id = 12, InfoHash = "existinghash" };
        this.historyRepository.Get(12).Returns(entry);
        this.torrentRepository.ExistsByInfoHash("existinghash").Returns(true);

        this.service.Delete(12);

        this.historyRepository.Received(1).Delete(12);
    }

    [Test]
    public void Delete_WhenTorrentNotInLibrary_CleansUpCachedTorrentFile_AndDeletesHistoryRecord()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr_del_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var torrentsDir = Path.Combine(tempDir, "Torrents");
            Directory.CreateDirectory(torrentsDir);
            var hash = "cachedhash123";
            var torrentFile = Path.Combine(torrentsDir, $"{hash}.torrent");
            File.WriteAllText(torrentFile, "fake torrent content");

            var appFolderInfo = Substitute.For<IAppFolderInfo>();
            appFolderInfo.AppDataFolder.Returns(tempDir);

            var customService = new DownloadHistoryService(
                this.historyRepository,
                this.torrentRepository,
                this.trackerEntryRepository,
                this.downloadEngine,
                this.eventAggregator,
                this.safeHttpClientService,
                this.categoryService,
                this.storagePathService,
                this.torrentFileParser,
                this.fileRepository,
                this.configService,
                appFolderInfo: appFolderInfo);

            this.historyRepository.Get(15).Returns(new DownloadHistory { Id = 15, InfoHash = hash });
            this.torrentRepository.ExistsByInfoHash(hash).Returns(false);

            customService.Delete(15);

            File.Exists(torrentFile).Should().BeFalse();
            this.historyRepository.Received(1).Delete(15);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void ClearAll_CleansUpCachedFilesAndDeletesAll()
    {
        var entries = new List<DownloadHistory>
        {
            new DownloadHistory { Id = 1, InfoHash = "hash1" },
            new DownloadHistory { Id = 2, InfoHash = string.Empty },
        };
        this.historyRepository.GetHistory(limit: 10000).Returns(entries);

        this.service.ClearAll();

        this.historyRepository.Received(1).DeleteAll();
    }

    [Test]
    public void PruneHistory_WhenRetentionDaysZeroOrNegative_DoesNotCallDelete()
    {
        this.service.PruneHistory(0);
        this.service.PruneHistory(-10);

        this.historyRepository.DidNotReceiveWithAnyArgs().DeleteOlderThan(Arg.Any<DateTime>());
    }

    [Test]
    public void PruneHistory_WhenRetentionDaysPositive_CallsDeleteOlderThanCutoff()
    {
        this.service.PruneHistory(60);

        this.historyRepository.Received(1).DeleteOlderThan(Arg.Is<DateTime>(d =>
            d <= DateTime.UtcNow.AddDays(-59) && d >= DateTime.UtcNow.AddDays(-61)));
    }

    [Test]
    public void RecordTorrentAdded_WhenTorrentIsNull_ReturnsNull()
    {
        var result = this.service.RecordTorrentAdded(null);

        result.Should().BeNull();
    }

    [Test]
    public void RecordTorrentAdded_WhenTrackersEmptyAndTrackerUrlSet_UsesTrackerUrl()
    {
        var torrent = new Torrent
        {
            Id = 301,
            Name = "TrackerUrl Torrent",
            InfoHash = "trackerurlhash",
            TrackerUrl = "http://mytracker.org/announce",
            TotalSize = 500,
        };

        this.historyRepository.FindByInfoHash("trackerurlhash").Returns((DownloadHistory)null!);
        this.trackerEntryRepository.GetByTorrentId(301).Returns(new List<TrackerEntry>());
        this.historyRepository.Insert(Arg.Any<DownloadHistory>()).Returns(args => (DownloadHistory)args[0]);

        var result = this.service.RecordTorrentAdded(torrent);

        result.Should().NotBeNull();
        result.PrimaryTracker.Should().Be("http://mytracker.org/announce");
        result.Trackers.Should().Contain("http://mytracker.org/announce");
    }

    [Test]
    public void RecordTorrentAdded_WhenProgressIsComplete_SetsDateCompleted()
    {
        var torrent = new Torrent
        {
            Id = 302,
            Name = "Completed Torrent",
            InfoHash = "completedhash",
            Progress = 1.0,
        };

        this.historyRepository.FindByInfoHash("completedhash").Returns((DownloadHistory)null!);
        this.historyRepository.Insert(Arg.Any<DownloadHistory>()).Returns(args => (DownloadHistory)args[0]);

        var result = this.service.RecordTorrentAdded(torrent);

        result.DateCompleted.Should().NotBeNull();
    }

    [Test]
    public void RecordTorrentUpdated_WhenTorrentIsNull_DoesNothing()
    {
        this.service.RecordTorrentUpdated(null);

        this.historyRepository.DidNotReceiveWithAnyArgs().Update(Arg.Any<DownloadHistory>());
    }

    [Test]
    public void RecordTorrentUpdated_WhenEntryNotFound_DoesNothing()
    {
        var torrent = new Torrent { Id = 501, InfoHash = "notfoundhash" };
        this.historyRepository.FindByTorrentId(501).Returns((DownloadHistory)null!);
        this.historyRepository.FindByInfoHash("notfoundhash").Returns((DownloadHistory)null!);

        this.service.RecordTorrentUpdated(torrent);

        this.historyRepository.DidNotReceiveWithAnyArgs().Update(Arg.Any<DownloadHistory>());
    }

    [Test]
    public void RecordTorrentUpdated_WhenEntryFound_UpdatesStatsAndTrackersAndDateCompleted()
    {
        var torrent = new Torrent
        {
            Id = 502,
            InfoHash = "updatedhash",
            Uploaded = 5000,
            Downloaded = 2500,
            Ratio = 2.0,
            IsPrivate = true,
            Progress = 1.0,
        };

        var existing = new DownloadHistory
        {
            Id = 80,
            TorrentId = 502,
            InfoHash = "updatedhash",
            DateCompleted = null,
            Status = "Active",
        };

        this.historyRepository.FindByTorrentId(502).Returns(existing);
        this.trackerEntryRepository.GetByTorrentId(502).Returns(new List<TrackerEntry>
        {
            new TrackerEntry { Url = "udp://tracker.updated.org:1337" },
        });

        this.service.RecordTorrentUpdated(torrent);

        existing.Uploaded.Should().Be(5000);
        existing.Downloaded.Should().Be(2500);
        existing.Ratio.Should().Be(2.0);
        existing.IsPrivate.Should().BeTrue();
        existing.DateCompleted.Should().NotBeNull();
        existing.Status.Should().Be("Completed");
        existing.PrimaryTracker.Should().Be("udp://tracker.updated.org:1337");
        this.historyRepository.Received(1).Update(existing);
    }

    [Test]
    public void RecordTorrentRemoved_WhenTorrentIsNull_DoesNothing()
    {
        this.service.RecordTorrentRemoved(null);

        this.historyRepository.DidNotReceiveWithAnyArgs().Insert(Arg.Any<DownloadHistory>());
        this.historyRepository.DidNotReceiveWithAnyArgs().Update(Arg.Any<DownloadHistory>());
    }

    [Test]
    public void RecordTorrentRemoved_WhenEntryDoesNotExist_InsertsNewRemovedEntryWithSeedingTimeAndMediaMetadata()
    {
        var dateAdded = DateTime.UtcNow.AddMinutes(-30);
        var torrent = new Torrent
        {
            Id = 601,
            Name = "Deleted Torrent",
            InfoHash = "delhash",
            TotalSize = 9000,
            DateAdded = dateAdded,
            Progress = 1.0,
            Uploaded = 18000,
            Downloaded = 9000,
            Ratio = 2.0,
            IsPrivate = true,
        };

        this.historyRepository.FindByTorrentId(601).Returns((DownloadHistory)null!);
        this.historyRepository.FindByInfoHash("delhash").Returns((DownloadHistory)null!);

        var mediaMetaRepo = Substitute.For<ITorrentMediaMetadataRepository>();
        mediaMetaRepo.GetByTorrentId(601).Returns(new TorrentMediaMetadata
        {
            TorrentId = 601,
            Title = "Deleted Torrent Movie",
        });

        var serviceWithMeta = new DownloadHistoryService(
            this.historyRepository,
            this.torrentRepository,
            this.trackerEntryRepository,
            this.downloadEngine,
            this.eventAggregator,
            this.safeHttpClientService,
            mediaMetadataRepository: mediaMetaRepo);

        serviceWithMeta.RecordTorrentRemoved(torrent, "Cleaned up");

        this.historyRepository.Received(1).Insert(Arg.Is<DownloadHistory>(h =>
            h.Title == "Deleted Torrent" &&
            h.Status == "Removed" &&
            h.RemovalReason == "Cleaned up" &&
            h.SeedingTime > 0 &&
            h.DataJson != null &&
            h.DataJson.Contains("Deleted Torrent Movie")));
    }

    [Test]
    public void RecordTorrentRemoved_WhenEntryExists_UpdatesTorrentIdToNullAndSetsMediaMetadata()
    {
        var torrent = new Torrent
        {
            Id = 602,
            Name = "Existing Movie",
            InfoHash = "existingdelhash",
            Uploaded = 500,
            Downloaded = 500,
            Ratio = 1.0,
        };

        var existing = new DownloadHistory
        {
            Id = 85,
            TorrentId = 602,
            InfoHash = "existingdelhash",
            Status = "Active",
        };

        this.historyRepository.FindByTorrentId(602).Returns(existing);

        var mediaMetaRepo = Substitute.For<ITorrentMediaMetadataRepository>();
        mediaMetaRepo.GetByTorrentId(602).Returns(new TorrentMediaMetadata
        {
            TorrentId = 602,
            Title = "Existing Movie Title",
        });

        var serviceWithMeta = new DownloadHistoryService(
            this.historyRepository,
            this.torrentRepository,
            this.trackerEntryRepository,
            this.downloadEngine,
            this.eventAggregator,
            this.safeHttpClientService,
            mediaMetadataRepository: mediaMetaRepo);

        serviceWithMeta.RecordTorrentRemoved(torrent, "User requested");

        existing.TorrentId.Should().BeNull();
        existing.Status.Should().Be("Removed");
        existing.RemovalReason.Should().Be("User requested");
        existing.DataJson.Should().Contain("Existing Movie Title");
        this.historyRepository.Received(1).Update(existing);
    }

    [Test]
    public void ReAdd_WhenEntryNotFound_ThrowsArgumentException()
    {
        this.historyRepository.Get(777).Returns((DownloadHistory)null!);

        Action act = () => this.service.ReAdd(777);

        act.Should().Throw<ArgumentException>().WithMessage("*not found*");
    }

    [Test]
    public async Task ReAddAsync_WithParsedAnnounceListMultipleTiers_InsertsTrackerEntriesWithConfiguredInterval()
    {
        var history = new DownloadHistory
        {
            Id = 778,
            InfoHash = "multitierhash",
            Title = "Multi Tier Torrent",
            TotalSize = 10000,
            DownloadUrl = "https://tracker.example.com/multi.torrent",
        };

        var fakeBytes = new byte[] { 0x64, 0x38 };
        var parsed = new ParsedTorrent
        {
            Name = "Multi Tier Torrent",
            InfoHash = "multitierhash",
            TotalSize = 10000,
            AnnounceList = new List<List<string>>
            {
                new List<string> { "udp://tier0.example.com:1337/announce" },
                new List<string> { "udp://tier1.example.com:1337/announce" },
            },
        };

        this.historyRepository.Get(778).Returns(history);
        this.torrentRepository.ExistsByInfoHash("multitierhash").Returns(false);
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.configService.TrackerAnnounceInterval.Returns(3600);
        this.safeHttpClientService.DownloadBytesAsync("https://tracker.example.com/multi.torrent")
            .Returns(Task.FromResult(fakeBytes));
        this.torrentFileParser.Parse(fakeBytes).Returns(parsed);

        this.torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 110;
            return t;
        });

        var added = await this.service.ReAddAsync(778);

        added.Should().NotBeNull();
        this.trackerEntryRepository.Received(1).InsertMany(Arg.Is<List<TrackerEntry>>(entries =>
            entries.Count == 2 &&
            entries[0].Tier == 0 &&
            entries[0].AnnounceInterval == 3600 &&
            entries[1].Tier == 1 &&
            entries[1].AnnounceInterval == 3600));
    }

    [Test]
    public void Update_WhenHistoryIsNull_DoesNothing_WhenNotNull_CallsUpdate()
    {
        this.service.Update(null!);
        this.historyRepository.DidNotReceiveWithAnyArgs().Update(Arg.Any<DownloadHistory>());

        var item = new DownloadHistory { Id = 33 };
        this.service.Update(item);
        this.historyRepository.Received(1).Update(item);
    }

    [Test]
    public void ReconcileAllTorrents_BackfillsMissingAndUpdatesExisting()
    {
        var t1 = new Torrent
        {
            Id = 701,
            InfoHash = "newreconcilehash",
            Name = "T1",
            TotalSize = 1000,
            IsPrivate = false,
        };
        var t2 = new Torrent
        {
            Id = 702,
            InfoHash = "existingreconcilehash",
            Name = "T2",
            TotalSize = 2000,
            IsPrivate = true,
        };
        var t3 = new Torrent
        {
            Id = 703,
            InfoHash = string.Empty,
            Name = "EmptyHash",
        };

        var existingH2 = new DownloadHistory
        {
            Id = 90,
            TorrentId = null,
            InfoHash = "existingreconcilehash",
            IsPrivate = false,
            Trackers = new List<string>(),
        };

        this.torrentRepository.All().Returns(new List<Torrent> { t1, t2, t3 });
        this.historyRepository.FindByInfoHash("newreconcilehash").Returns((DownloadHistory)null!);
        this.historyRepository.FindByInfoHash("existingreconcilehash").Returns(existingH2);
        this.trackerEntryRepository.GetByTorrentId(702).Returns(new List<TrackerEntry>
        {
            new TrackerEntry { Url = "udp://recon-tracker.org:1337" },
        });

        var backfilled = this.service.ReconcileAllTorrents();

        backfilled.Should().Be(1);
        this.historyRepository.Received(1).Insert(Arg.Is<DownloadHistory>(h =>
            h.TorrentId == 701 &&
            h.InfoHash == "newreconcilehash" &&
            h.Source == "Public Tracker"));

        existingH2.TorrentId.Should().Be(702);
        existingH2.Status.Should().Be("Active");
        existingH2.IsPrivate.Should().BeTrue();
        existingH2.Trackers.Should().Contain("udp://recon-tracker.org:1337");
        this.historyRepository.Received(1).Update(existingH2);
    }

    [Test]
    public void Handle_TorrentAddedEvent_NullAndValid_Behaviors()
    {
        this.service.Handle((TorrentAddedEvent)null!);
        this.service.Handle(new TorrentAddedEvent { Torrent = null });
        this.historyRepository.DidNotReceiveWithAnyArgs().Insert(Arg.Any<DownloadHistory>());

        var t = new Torrent
        {
            Id = 801,
            InfoHash = "handleaddhash",
            Name = "Add Torrent",
            TotalSize = 500,
        };
        this.historyRepository.FindByInfoHash("handleaddhash").Returns((DownloadHistory)null!);

        this.service.Handle(new TorrentAddedEvent { Torrent = t });

        this.historyRepository.Received(1).Insert(Arg.Is<DownloadHistory>(h => h.TorrentId == 801));
    }

    [Test]
    public void Handle_TorrentDeletedEvent_NullAndValid_Behaviors()
    {
        this.service.Handle((TorrentDeletedEvent)null!);
        this.service.Handle(new TorrentDeletedEvent { Torrent = null });

        var t = new Torrent { Id = 802, InfoHash = "handledelhash" };
        var existing = new DownloadHistory { Id = 95, TorrentId = 802, InfoHash = "handledelhash" };
        this.historyRepository.FindByTorrentId(802).Returns(existing);

        this.service.Handle(new TorrentDeletedEvent { Torrent = t });

        existing.Status.Should().Be("Removed");
        existing.RemovalReason.Should().Be("Deleted from active library");
        this.historyRepository.Received(1).Update(existing);
    }

    [Test]
    public void Handle_TorrentDownloadCompletedEvent_UpdatesStatus()
    {
        this.service.Handle((TorrentDownloadCompletedEvent)null!);
        this.service.Handle(new TorrentDownloadCompletedEvent { Torrent = null });

        var t = new Torrent
        {
            Id = 803,
            InfoHash = "handlecompletehash",
            Downloaded = 1000,
            Uploaded = 2000,
            Ratio = 2.0,
        };
        this.historyRepository.FindByInfoHash("handlecompletehash").Returns((DownloadHistory)null!);

        this.service.Handle(new TorrentDownloadCompletedEvent { Torrent = t });
        this.historyRepository.DidNotReceiveWithAnyArgs().Update(Arg.Any<DownloadHistory>());

        var existing = new DownloadHistory
        {
            Id = 96,
            InfoHash = "handlecompletehash",
            Status = "Active",
        };
        this.historyRepository.FindByInfoHash("handlecompletehash").Returns(existing);

        this.service.Handle(new TorrentDownloadCompletedEvent { Torrent = t });

        existing.Status.Should().Be("Completed");
        existing.DateCompleted.Should().NotBeNull();
        existing.Downloaded.Should().Be(1000);
        existing.Uploaded.Should().Be(2000);
        existing.Ratio.Should().Be(2.0);
        this.historyRepository.Received(1).Update(existing);
    }

    [Test]
    public void Handle_TorrentStatusChangedEvent_UpdatesStatus()
    {
        this.service.Handle((TorrentStatusChangedEvent)null!);
        this.service.Handle(new TorrentStatusChangedEvent { Torrent = null });

        var t = new Torrent
        {
            Id = 804,
            InfoHash = "statuschangehash",
            Uploaded = 100,
            Downloaded = 50,
            Ratio = 2.0,
        };

        var existing = new DownloadHistory
        {
            Id = 97,
            InfoHash = "statuschangehash",
            DateCompleted = null,
        };
        this.historyRepository.FindByInfoHash("statuschangehash").Returns(existing);

        this.service.Handle(new TorrentStatusChangedEvent
        {
            Torrent = t,
            NewStatus = TorrentStatus.Downloading,
        });
        this.historyRepository.DidNotReceive().Update(existing);

        this.service.Handle(new TorrentStatusChangedEvent
        {
            Torrent = t,
            NewStatus = TorrentStatus.Seeding,
            Reason = "Seeding completed",
        });
        existing.DateCompleted.Should().NotBeNull();
        existing.RemovalReason.Should().Be("Seeding completed");
        existing.Uploaded.Should().Be(100);
        this.historyRepository.Received(1).Update(existing);

        this.service.Handle(new TorrentStatusChangedEvent
        {
            Torrent = t,
            NewStatus = TorrentStatus.Error,
            Reason = "I/O Error",
        });
        existing.RemovalReason.Should().Be("I/O Error");
    }
}

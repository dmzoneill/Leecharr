// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Http;
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
    public void ReAdd_WhenCompletedInHistory_SetsSeedingStatusAndFullProgress()
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
        added.Status.Should().Be(TorrentStatus.Seeding);
        added.Progress.Should().Be(1.0);
        added.Downloaded.Should().Be(50000);
        added.DateCompleted.Should().NotBeNull();
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
            files[1].Path == "Parsed Torrent Release/sample.nfo" &&
            files[1].Size == 1000));
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
}


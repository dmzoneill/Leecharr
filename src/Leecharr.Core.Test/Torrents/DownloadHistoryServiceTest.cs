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
        this.service = new DownloadHistoryService(
            this.historyRepository,
            this.torrentRepository,
            this.trackerEntryRepository,
            this.downloadEngine,
            this.eventAggregator,
            this.safeHttpClientService);
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
        history.TorrentId.Should().Be(42);
        history.Status.Should().Be("Active");
        this.historyRepository.Received(1).Update(history);
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
                appFolderInfo);

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

            var result = await customService.ReAddTorrentAsync(10);

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
}

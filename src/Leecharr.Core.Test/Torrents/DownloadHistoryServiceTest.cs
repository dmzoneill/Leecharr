using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class DownloadHistoryServiceTest
{
    private IDownloadHistoryRepository _historyRepository = null!;
    private ITorrentRepository _torrentRepository = null!;
    private ITrackerEntryRepository _trackerEntryRepository = null!;
    private DownloadHistoryService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _historyRepository = Substitute.For<IDownloadHistoryRepository>();
        _torrentRepository = Substitute.For<ITorrentRepository>();
        _trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        _service = new DownloadHistoryService(_historyRepository, _torrentRepository, _trackerEntryRepository);
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
            Ratio = 2.0
        };

        _historyRepository.FindByInfoHash(torrent.InfoHash).Returns((DownloadHistory)null!);
        _historyRepository.Insert(Arg.Any<DownloadHistory>()).Returns(args => (DownloadHistory)args[0]);

        var result = _service.RecordTorrentAdded(torrent, source: "Radarr");

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
            Ratio = 2.0
        };

        var existing = new DownloadHistory
        {
            Id = 10,
            TorrentId = 1,
            InfoHash = "1234567890abcdef",
            Title = "Test.Show.S01E01",
            Status = "Active"
        };

        _historyRepository.FindByTorrentId(1).Returns(existing);

        _service.RecordTorrentRemoved(torrent, "Deleted from library");

        existing.Status.Should().Be("Removed");
        existing.RemovalReason.Should().Be("Deleted from library");
        existing.TorrentId.Should().BeNull();
        _historyRepository.Received(1).Update(existing);
    }

    [Test]
    public void ReAdd_ThrowsWhenAlreadyInLibrary()
    {
        var history = new DownloadHistory
        {
            Id = 5,
            InfoHash = "dup123",
            Title = "Duplicate Release"
        };

        _historyRepository.Get(5).Returns(history);
        _torrentRepository.ExistsByInfoHash("dup123").Returns(true);

        Action act = () => _service.ReAdd(5);

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
            PrimaryTracker = "udp://tracker.opentrackr.org:1337/announce"
        };

        _historyRepository.Get(5).Returns(history);
        _torrentRepository.ExistsByInfoHash("unique123").Returns(false);
        _torrentRepository.All().Returns(new List<Torrent>());
        _torrentRepository.Insert(Arg.Any<Torrent>()).Returns(args =>
        {
            var t = (Torrent)args[0];
            t.Id = 42;
            return t;
        });

        var added = _service.ReAdd(5);

        added.Should().NotBeNull();
        added.Id.Should().Be(42);
        added.InfoHash.Should().Be("unique123");
        history.TorrentId.Should().Be(42);
        history.Status.Should().Be("Active");
        _historyRepository.Received(1).Update(history);
    }
}

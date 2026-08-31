// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
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
    private DownloadHistoryService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.historyRepository = Substitute.For<IDownloadHistoryRepository>();
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.service = new DownloadHistoryService(this.historyRepository, this.torrentRepository, this.trackerEntryRepository, this.downloadEngine, this.eventAggregator);
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
}

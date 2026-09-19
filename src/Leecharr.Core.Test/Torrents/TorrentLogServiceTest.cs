// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentLogServiceTest
{
    private TorrentLogService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new TorrentLogService();
        this.service.ClearAll();
    }

    [Test]
    public void Log_AddsLogEntryForTorrent()
    {
        this.service.Log(1, "INFO", "Tracker", "Announced to tracker successfully");

        var logs = this.service.GetLogs(1);
        logs.Should().HaveCount(1);
        logs[0].TorrentId.Should().Be(1);
        logs[0].Level.Should().Be("INFO");
        logs[0].Source.Should().Be("Tracker");
        logs[0].Message.Should().Be("Announced to tracker successfully");
        logs[0].Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public void GetLogs_ReturnsEmptyList_WhenNoLogsExist()
    {
        var logs = this.service.GetLogs(999);
        logs.Should().BeEmpty();
    }

    [Test]
    public void GetLogs_RespectsLimit()
    {
        for (var i = 1; i <= 20; i++)
        {
            this.service.Log(1, "INFO", "Engine", $"Message {i}");
        }

        var logs = this.service.GetLogs(1, limit: 5);
        logs.Should().HaveCount(5);
        logs.First().Message.Should().Be("Message 20");
        logs.Last().Message.Should().Be("Message 16");
    }

    [Test]
    public void Log_CapsEntriesAtMaximumPerTorrent()
    {
        for (var i = 1; i <= 300; i++)
        {
            this.service.Log(1, "DEBUG", "Tracker", $"Event {i}");
        }

        var logs = this.service.GetLogs(1, limit: 1000);
        logs.Should().HaveCount(250);
        logs.First().Message.Should().Be("Event 300");
        logs.Last().Message.Should().Be("Event 51");
    }

    [Test]
    public void Handle_TorrentAddedEvent_LogsInfo()
    {
        var torrent = new Torrent { Id = 5, Name = "Ubuntu Linux 24.04 ISO", DateAdded = DateTime.UtcNow };
        this.service.Handle(new TorrentAddedEvent { Torrent = torrent });

        var logs = this.service.GetLogs(5);
        logs.Should().NotBeEmpty();
        logs.Should().Contain(l => l.Message.Contains("Ubuntu Linux 24.04 ISO") && l.Source == "Engine");
    }

    [Test]
    public void Handle_TorrentStatusChangedEvent_LogsStateTransition()
    {
        var torrent = new Torrent { Id = 7, Name = "Debian 12" };
        this.service.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Downloading,
            NewStatus = TorrentStatus.Paused,
        });

        var logs = this.service.GetLogs(7);
        logs.Should().HaveCount(1);
        logs[0].Message.Should().Contain("Downloading -> Paused");
    }

    [Test]
    public void Handle_TorrentDownloadCompletedEvent_LogsCompletion()
    {
        var torrent = new Torrent { Id = 10, Name = "Arch Linux" };
        this.service.Handle(new TorrentDownloadCompletedEvent(torrent));

        var logs = this.service.GetLogs(10);
        logs.Should().HaveCount(1);
        logs[0].Message.Should().Contain("completed");
    }

    [Test]
    public void Handle_TorrentDeletedEvent_ClearsLogsForTorrent()
    {
        this.service.Log(15, "INFO", "Tracker", "Some log");
        this.service.GetLogs(15).Should().HaveCount(1);

        var torrent = new Torrent { Id = 15, Name = "Delete Me" };
        this.service.Handle(new TorrentDeletedEvent { Torrent = torrent, DeleteFiles = false });

        this.service.GetLogs(15).Should().BeEmpty();
    }

    [Test]
    public void Log_PersistsToRepository_WhenRepositoryProvided()
    {
        var repo = Substitute.For<ITorrentEventLogRepository>();
        var serviceWithRepo = new TorrentLogService(repo);

        serviceWithRepo.Log(20, "WARN", "Disk", "Disk space low");

        repo.Received(1).Insert(Arg.Is<TorrentEventLog>(e =>
            e.TorrentId == 20 &&
            e.Level == "WARN" &&
            e.Source == "Disk" &&
            e.Message == "Disk space low"));

        var logs = serviceWithRepo.GetLogs(20);
        logs.Should().HaveCount(1);
        logs[0].Message.Should().Be("Disk space low");
    }

    [Test]
    public void GetLogs_FallsBackToRepository_WhenMemoryCacheIsEmpty()
    {
        var repo = Substitute.For<ITorrentEventLogRepository>();
        repo.GetLogsForTorrent(25, 10).Returns(new List<TorrentEventLog>
        {
            new()
            {
                Id = 101,
                TorrentId = 25,
                Level = "Info",
                Source = "Engine",
                Message = "Recovered from database",
                Timestamp = DateTime.UtcNow,
            },
        });

        var serviceWithRepo = new TorrentLogService(repo);

        var logs = serviceWithRepo.GetLogs(25, 10);
        logs.Should().HaveCount(1);
        logs[0].Message.Should().Be("Recovered from database");
        repo.Received(1).GetLogsForTorrent(25, 10);
    }

    [Test]
    public void ClearLogs_PurgesBothMemoryAndRepository()
    {
        var repo = Substitute.For<ITorrentEventLogRepository>();
        var serviceWithRepo = new TorrentLogService(repo);

        serviceWithRepo.Log(30, "Info", "Engine", "To be cleared");
        serviceWithRepo.ClearLogs(30);

        repo.Received(1).DeleteForTorrent(30);
        serviceWithRepo.GetLogs(30).Should().BeEmpty();
    }

    [Test]
    public void ClearAll_PurgesBothMemoryAndRepository()
    {
        var repo = Substitute.For<ITorrentEventLogRepository>();
        var serviceWithRepo = new TorrentLogService(repo);

        serviceWithRepo.Log(35, "Info", "Engine", "To be cleared all");
        serviceWithRepo.ClearAll();

        repo.Received(1).DeleteAll();
        serviceWithRepo.GetLogs(35).Should().BeEmpty();
    }

    [Test]
    public void Handle_TorrentDeletedEvent_PurgesBothMemoryAndRepository()
    {
        var repo = Substitute.For<ITorrentEventLogRepository>();
        var serviceWithRepo = new TorrentLogService(repo);

        serviceWithRepo.Log(40, "Info", "Engine", "To be deleted");

        var torrent = new Torrent { Id = 40, Name = "Deleted Torrent" };
        serviceWithRepo.Handle(new TorrentDeletedEvent { Torrent = torrent });

        repo.Received(1).DeleteForTorrent(40);
        serviceWithRepo.GetLogs(40).Should().BeEmpty();
    }

    [Test]
    public void Log_EvictsOldestTorrents_WhenCapacityExceeded()
    {
        this.service.ClearAll();

        for (var i = 1; i <= 1060; i++)
        {
            this.service.Log(i, "Info", "Engine", $"Log for torrent {i}");
        }

        // Torrent 1 was the earliest added and should have been evicted by LRU pruning
        this.service.GetLogs(1).Should().BeEmpty();

        // The latest torrent should definitely be present
        this.service.GetLogs(1060).Should().HaveCount(1);
    }
}

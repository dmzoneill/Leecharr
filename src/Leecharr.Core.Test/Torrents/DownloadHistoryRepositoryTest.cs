// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class DownloadHistoryRepositoryTest
{
    private string dbPath = null!;
    private DownloadHistoryRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-downloadhistory-repo-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={this.dbPath};";

        var serviceProvider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitialSetup).Assembly).For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .BuildServiceProvider(false);

        using (var scope = serviceProvider.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
        }

        TableRegistration.RegisterTables();
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);
        this.repository = new DownloadHistoryRepository(database);
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(this.dbPath))
        {
            try
            {
                File.Delete(this.dbPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void GetHistory_WithQuery_MatchesCaseInsensitivelyAcrossTitleInfoHashTrackerAndIndexer()
    {
        var entry1 = new DownloadHistory
        {
            Title = "Ubuntu.24.04.LTS.Desktop",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            PrimaryTracker = "https://tracker.ubuntu.com/announce",
            IndexerName = "Canonical Releases",
            Status = "Active",
            DateAdded = DateTime.UtcNow.AddMinutes(-10),
        };

        var entry2 = new DownloadHistory
        {
            Title = "Debian.12.Bookworm.Netinst",
            InfoHash = "1122334455667788990011223344556677889900",
            PrimaryTracker = "https://tracker.debian.org/announce",
            IndexerName = "Debian Project",
            Status = "Completed",
            DateAdded = DateTime.UtcNow.AddMinutes(-5),
        };

        this.repository.Insert(entry1);
        this.repository.Insert(entry2);

        // Match Title with lower query
        var titleMatch = this.repository.GetHistory(query: "ubuntu");
        titleMatch.Should().HaveCount(1);
        titleMatch[0].Title.Should().Be("Ubuntu.24.04.LTS.Desktop");

        // Match Title with upper query
        var titleUpperMatch = this.repository.GetHistory(query: "UBUNTU");
        titleUpperMatch.Should().HaveCount(1);
        titleUpperMatch[0].Title.Should().Be("Ubuntu.24.04.LTS.Desktop");

        // Match InfoHash with mixed case query
        var hashMatch = this.repository.GetHistory(query: "AABBCCDDEEFF");
        hashMatch.Should().HaveCount(1);
        hashMatch[0].InfoHash.Should().Be("aabbccddeeff00112233445566778899aabbccdd");

        // Match PrimaryTracker with mixed case query
        var trackerMatch = this.repository.GetHistory(query: "Tracker.Ubuntu.Com");
        trackerMatch.Should().HaveCount(1);
        trackerMatch[0].PrimaryTracker.Should().Be("https://tracker.ubuntu.com/announce");

        // Match IndexerName with uppercase query
        var indexerMatch = this.repository.GetHistory(query: "CANONICAL");
        indexerMatch.Should().HaveCount(1);
        indexerMatch[0].IndexerName.Should().Be("Canonical Releases");

        // Match Debian entry
        var debianMatch = this.repository.GetHistory(query: "bookworm");
        debianMatch.Should().HaveCount(1);
        debianMatch[0].Title.Should().Be("Debian.12.Bookworm.Netinst");
    }

    [Test]
    public void GetHistory_WithStatusAndLimit_FiltersProperly()
    {
        this.repository.Insert(new DownloadHistory
        {
            Title = "Item 1",
            InfoHash = "1111111111111111111111111111111111111111",
            Status = "Active",
            DateAdded = DateTime.UtcNow.AddMinutes(-30),
        });

        this.repository.Insert(new DownloadHistory
        {
            Title = "Item 2",
            InfoHash = "2222222222222222222222222222222222222222",
            Status = "Completed",
            DateAdded = DateTime.UtcNow.AddMinutes(-20),
        });

        this.repository.Insert(new DownloadHistory
        {
            Title = "Item 3",
            InfoHash = "3333333333333333333333333333333333333333",
            Status = "Active",
            DateAdded = DateTime.UtcNow.AddMinutes(-10),
        });

        var activeItems = this.repository.GetHistory(status: "Active");
        activeItems.Should().HaveCount(2);

        var allItems = this.repository.GetHistory(status: "all");
        allItems.Should().HaveCount(3);

        var limited = this.repository.GetHistory(limit: 1);
        limited.Should().HaveCount(1);
        limited[0].Title.Should().Be("Item 3"); // Ordered by DateAdded DESC
    }

    [Test]
    public void FindByInfoHash_NormalizesInputCase()
    {
        var entry = new DownloadHistory
        {
            Title = "Test Movie",
            InfoHash = "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
            Status = "Active",
            DateAdded = DateTime.UtcNow,
        };

        this.repository.Insert(entry);

        var found = this.repository.FindByInfoHash("abcdef0123456789abcdef0123456789abcdef01");
        found.Should().NotBeNull();
        found.Title.Should().Be("Test Movie");

        var foundUpper = this.repository.FindByInfoHash("ABCDEF0123456789ABCDEF0123456789ABCDEF01");
        foundUpper.Should().NotBeNull();
        foundUpper.Title.Should().Be("Test Movie");
    }

    [Test]
    public void FindByTorrentId_ReturnsMatchingEntry()
    {
        var entry = new DownloadHistory
        {
            TorrentId = 42,
            Title = "Torrent 42",
            InfoHash = "4242424242424242424242424242424242424242",
            Status = "Active",
            DateAdded = DateTime.UtcNow,
        };

        this.repository.Insert(entry);

        var found = this.repository.FindByTorrentId(42);
        found.Should().NotBeNull();
        found.Title.Should().Be("Torrent 42");

        var notFound = this.repository.FindByTorrentId(999);
        notFound.Should().BeNull();
    }

    [Test]
    public void DeleteAll_RemovesAllRecords()
    {
        this.repository.Insert(new DownloadHistory
        {
            Title = "To Delete",
            InfoHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Status = "Active",
            DateAdded = DateTime.UtcNow,
        });

        this.repository.GetHistory().Should().HaveCount(1);

        this.repository.DeleteAll();

        this.repository.GetHistory().Should().BeEmpty();
    }
}

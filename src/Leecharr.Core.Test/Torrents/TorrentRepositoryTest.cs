// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class TorrentRepositoryTest
{
    private string dbPath = null!;
    private TorrentRepository repository = null!;
    private TorrentFileRepository fileRepository = null!;
    private TrackerEntryRepository trackerRepository = null!;
    private TorrentMediaMetadataRepository metadataRepository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-torrent-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new TorrentRepository(database);
        this.fileRepository = new TorrentFileRepository(database);
        this.trackerRepository = new TrackerEntryRepository(database);
        this.metadataRepository = new TorrentMediaMetadataRepository(database);
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
    public void Delete_AtomicallyRemovesTorrentAndAllRelatedChildRecords()
    {
        var torrent = new Torrent
        {
            Name = "Test Torrent",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            Category = "movies",
            TotalSize = 1000,
            DateAdded = DateTime.UtcNow,
        };
        var inserted = this.repository.Insert(torrent);

        this.fileRepository.Insert(new TorrentFile
        {
            TorrentId = inserted.Id,
            Path = "movie.mp4",
            Size = 1000,
        });

        this.trackerRepository.Insert(new TrackerEntry
        {
            TorrentId = inserted.Id,
            Url = "http://tracker.example.com/announce",
        });

        this.metadataRepository.Insert(new TorrentMediaMetadata
        {
            TorrentId = inserted.Id,
            ArrType = "Radarr",
            Title = "Test Movie",
        });

        // Verify inserted
        this.fileRepository.GetByTorrentId(inserted.Id).Should().HaveCount(1);
        this.trackerRepository.GetByTorrentId(inserted.Id).Should().HaveCount(1);
        this.metadataRepository.GetByTorrentId(inserted.Id).Should().NotBeNull();

        // Delete torrent
        this.repository.Delete(inserted.Id);

        // Verify child records and torrent deleted
        this.repository.Get(inserted.Id).Should().BeNull();
        this.fileRepository.GetByTorrentId(inserted.Id).Should().BeEmpty();
        this.trackerRepository.GetByTorrentId(inserted.Id).Should().BeEmpty();
        this.metadataRepository.GetByTorrentId(inserted.Id).Should().BeNull();
    }

    [Test]
    public void GetByInfoHash_ReturnsMatchingTorrent()
    {
        var torrent = new Torrent
        {
            Name = "Hash Torrent",
            InfoHash = "abcdef0123456789abcdef0123456789abcdef01",
            Category = "tv",
            TotalSize = 500,
            DateAdded = DateTime.UtcNow,
        };
        this.repository.Insert(torrent);

        var result = this.repository.GetByInfoHash("ABCDEF0123456789ABCDEF0123456789ABCDEF01");

        result.Should().NotBeNull();
        result.Name.Should().Be("Hash Torrent");
    }

    [Test]
    public void GetByCategory_And_GetByStatus_ReturnMatchingTorrents()
    {
        this.repository.Insert(new Torrent
        {
            Name = "Torrent 1",
            InfoHash = "1111111111111111111111111111111111111111",
            Category = "tv",
            Status = TorrentStatus.Downloading,
            DateAdded = DateTime.UtcNow,
        });

        this.repository.Insert(new Torrent
        {
            Name = "Torrent 2",
            InfoHash = "2222222222222222222222222222222222222222",
            Category = "movies",
            Status = TorrentStatus.Seeding,
            DateAdded = DateTime.UtcNow,
        });

        var tvTorrents = this.repository.GetByCategory("tv").ToList();
        tvTorrents.Should().HaveCount(1);
        tvTorrents[0].Name.Should().Be("Torrent 1");

        var seedingTorrents = this.repository.GetByStatus(TorrentStatus.Seeding).ToList();
        seedingTorrents.Should().HaveCount(1);
        seedingTorrents[0].Name.Should().Be("Torrent 2");
    }

    [Test]
    public async Task Insert_WhenMultipleConcurrentInsertsWithoutQueuePosition_AssignsSequentialUniqueQueuePositions()
    {
        var tasks = Enumerable.Range(1, 20).Select(i => Task.Run(() =>
        {
            var torrent = new Torrent
            {
                Name = $"Concurrent Torrent {i}",
                InfoHash = $"{i:D40}",
                Category = "default",
                Status = TorrentStatus.Downloading,
                QueuePosition = 0,
                DateAdded = DateTime.UtcNow,
            };
            return this.repository.Insert(torrent);
        })).ToArray();

        var insertedTorrents = await Task.WhenAll(tasks);

        var queuePositions = insertedTorrents.Select(t => t.QueuePosition).OrderBy(p => p).ToList();
        queuePositions.Should().HaveCount(20);
        queuePositions.Distinct().Should().HaveCount(20);
        queuePositions.Should().Equal(Enumerable.Range(1, 20));
    }

    [Test]
    public void UpsertMany_WhenBatchInsertingWithoutQueuePosition_AssignsSequentialUniqueQueuePositions()
    {
        var toInsert = Enumerable.Range(1, 10).Select(i => new Torrent
        {
            Name = $"Batch Torrent {i}",
            InfoHash = $"ba{i:D38}",
            Category = "default",
            Status = TorrentStatus.Downloading,
            QueuePosition = 0,
            DateAdded = DateTime.UtcNow,
        }).ToList();

        this.repository.UpsertMany(toInsert, null);

        var all = this.repository.All().OrderBy(t => t.QueuePosition).ToList();
        all.Should().HaveCount(10);
        all.Select(t => t.QueuePosition).Should().Equal(Enumerable.Range(1, 10));
    }

    [Test]
    public void GetByInfoHash_WhenQueriedByV2InfoHash_ReturnsMatchingHybridTorrent()
    {
        var torrent = new Torrent
        {
            Name = "Hybrid Torrent",
            InfoHash = "1111111111111111111111111111111111111111",
            V2InfoHash = "2222222222222222222222222222222222222222222222222222222222222222",
            Category = "movies",
            TotalSize = 1000,
            DateAdded = DateTime.UtcNow,
        };
        this.repository.Insert(torrent);

        var byV1 = this.repository.GetByInfoHash("1111111111111111111111111111111111111111");
        byV1.Should().NotBeNull();
        byV1.Name.Should().Be("Hybrid Torrent");
        byV1.V2InfoHash.Should().Be("2222222222222222222222222222222222222222222222222222222222222222");

        var byV2 = this.repository.GetByInfoHash("2222222222222222222222222222222222222222222222222222222222222222");
        byV2.Should().NotBeNull();
        byV2.Name.Should().Be("Hybrid Torrent");

        var byV2Upper = this.repository.GetByInfoHash("2222222222222222222222222222222222222222222222222222222222222222".ToUpperInvariant());
        byV2Upper.Should().NotBeNull();
        byV2Upper.Name.Should().Be("Hybrid Torrent");
    }

    [Test]
    public void ExistsByInfoHash_WhenQueriedByV2InfoHash_ReturnsTrue()
    {
        var torrent = new Torrent
        {
            Name = "Hybrid Torrent 2",
            InfoHash = "3333333333333333333333333333333333333333",
            V2InfoHash = "4444444444444444444444444444444444444444444444444444444444444444",
            Category = "tv",
            TotalSize = 500,
            DateAdded = DateTime.UtcNow,
        };
        this.repository.Insert(torrent);

        this.repository.ExistsByInfoHash("3333333333333333333333333333333333333333").Should().BeTrue();
        this.repository.ExistsByInfoHash("4444444444444444444444444444444444444444444444444444444444444444").Should().BeTrue();
        this.repository.ExistsByInfoHash("4444444444444444444444444444444444444444444444444444444444444444".ToUpperInvariant()).Should().BeTrue();
        this.repository.ExistsByInfoHash("5555555555555555555555555555555555555555").Should().BeFalse();
    }
}

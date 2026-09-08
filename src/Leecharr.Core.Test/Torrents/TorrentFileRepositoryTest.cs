// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
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
public class TorrentFileRepositoryTest
{
    private string dbPath = null!;
    private TorrentRepository torrentRepository = null!;
    private TorrentFileRepository fileRepository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-torrentfile-repo-test-{Guid.NewGuid():N}.db");
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
        this.torrentRepository = new TorrentRepository(database);
        this.fileRepository = new TorrentFileRepository(database);
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
    public void GetByTorrentId_ReturnsFilesOrderedByIdAscending()
    {
        var torrent = new Torrent
        {
            Name = "Multi-file Torrent",
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            Category = "tv",
            TotalSize = 3000,
            DateAdded = DateTime.UtcNow,
        };
        var insertedTorrent = this.torrentRepository.Insert(torrent);

        var file1 = this.fileRepository.Insert(new TorrentFile
        {
            TorrentId = insertedTorrent.Id,
            Path = "Season 01/Episode 01.mkv",
            Size = 1000,
        });

        var file2 = this.fileRepository.Insert(new TorrentFile
        {
            TorrentId = insertedTorrent.Id,
            Path = "Season 01/Episode 02.mkv",
            Size = 1000,
        });

        var file3 = this.fileRepository.Insert(new TorrentFile
        {
            TorrentId = insertedTorrent.Id,
            Path = "Season 01/Episode 03.mkv",
            Size = 1000,
        });

        var files = this.fileRepository.GetByTorrentId(insertedTorrent.Id).ToList();

        files.Should().HaveCount(3);
        files[0].Id.Should().Be(file1.Id);
        files[0].Path.Should().Be("Season 01/Episode 01.mkv");
        files[1].Id.Should().Be(file2.Id);
        files[1].Path.Should().Be("Season 01/Episode 02.mkv");
        files[2].Id.Should().Be(file3.Id);
        files[2].Path.Should().Be("Season 01/Episode 03.mkv");
        files.Select(f => f.Id).Should().BeInAscendingOrder();
    }

    [Test]
    public void GetByTorrentId_FiltersOnlyFilesForSpecifiedTorrent()
    {
        var torrent1 = this.torrentRepository.Insert(new Torrent
        {
            Name = "Torrent 1",
            InfoHash = "1111111111111111111111111111111111111111",
            Category = "movies",
            TotalSize = 1000,
            DateAdded = DateTime.UtcNow,
        });

        var torrent2 = this.torrentRepository.Insert(new Torrent
        {
            Name = "Torrent 2",
            InfoHash = "2222222222222222222222222222222222222222",
            Category = "movies",
            TotalSize = 2000,
            DateAdded = DateTime.UtcNow,
        });

        this.fileRepository.Insert(new TorrentFile { TorrentId = torrent1.Id, Path = "movie1.mkv", Size = 1000 });
        this.fileRepository.Insert(new TorrentFile { TorrentId = torrent2.Id, Path = "movie2.mkv", Size = 2000 });

        var torrent1Files = this.fileRepository.GetByTorrentId(torrent1.Id).ToList();
        var torrent2Files = this.fileRepository.GetByTorrentId(torrent2.Id).ToList();

        torrent1Files.Should().HaveCount(1);
        torrent1Files[0].Path.Should().Be("movie1.mkv");

        torrent2Files.Should().HaveCount(1);
        torrent2Files[0].Path.Should().Be("movie2.mkv");
    }

    [Test]
    public void DeleteByTorrentId_DeletesAllFilesForSpecifiedTorrent()
    {
        var torrent = this.torrentRepository.Insert(new Torrent
        {
            Name = "Torrent to Delete",
            InfoHash = "3333333333333333333333333333333333333333",
            Category = "tv",
            TotalSize = 1000,
            DateAdded = DateTime.UtcNow,
        });

        this.fileRepository.Insert(new TorrentFile { TorrentId = torrent.Id, Path = "ep1.mkv", Size = 500 });
        this.fileRepository.Insert(new TorrentFile { TorrentId = torrent.Id, Path = "ep2.mkv", Size = 500 });

        this.fileRepository.GetByTorrentId(torrent.Id).Should().HaveCount(2);

        this.fileRepository.DeleteByTorrentId(torrent.Id);

        this.fileRepository.GetByTorrentId(torrent.Id).Should().BeEmpty();
    }

    [Test]
    public void GetByTorrentIds_ReturnsFilesGroupedByTorrentId()
    {
        var torrent1 = this.torrentRepository.Insert(new Torrent
        {
            Name = "Torrent 1",
            InfoHash = "4444444444444444444444444444444444444444",
            Category = "movies",
            TotalSize = 1000,
            DateAdded = DateTime.UtcNow,
        });

        var torrent2 = this.torrentRepository.Insert(new Torrent
        {
            Name = "Torrent 2",
            InfoHash = "5555555555555555555555555555555555555555",
            Category = "tv",
            TotalSize = 2000,
            DateAdded = DateTime.UtcNow,
        });

        this.fileRepository.Insert(new TorrentFile { TorrentId = torrent1.Id, Path = "file1.mkv", Size = 1000 });
        this.fileRepository.Insert(new TorrentFile { TorrentId = torrent2.Id, Path = "file2.mkv", Size = 1000 });
        this.fileRepository.Insert(new TorrentFile { TorrentId = torrent2.Id, Path = "file3.mkv", Size = 1000 });

        var dict = this.fileRepository.GetByTorrentIds(new[] { torrent1.Id, torrent2.Id });

        dict.Should().ContainKey(torrent1.Id);
        dict.Should().ContainKey(torrent2.Id);
        dict[torrent1.Id].Should().HaveCount(1);
        dict[torrent2.Id].Should().HaveCount(2);
    }
}

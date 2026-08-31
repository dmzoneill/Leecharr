// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class BasicRepositoryTest
{
    private string dbPath = null!;
    private BasicRepository<Torrent> repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new BasicRepository<Torrent>(database);
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
    public void Insert_And_Get_And_Update_And_Delete_Lifecycle()
    {
        var torrent = new Torrent
        {
            Name = "Ubuntu.24.04.iso",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Category = "linux",
            TotalSize = 4000000000,
            Status = TorrentStatus.Downloading,
            DateAdded = DateTime.UtcNow,
        };

        // 1. Insert
        var inserted = this.repository.Insert(torrent);
        inserted.Id.Should().BeGreaterThan(0);

        // 2. Get
        var fetched = this.repository.Get(inserted.Id);
        fetched.Should().NotBeNull();
        fetched.Name.Should().Be("Ubuntu.24.04.iso");

        // 3. Update
        fetched.Progress = 0.5;
        fetched.Status = TorrentStatus.Seeding;
        var updated = this.repository.Update(fetched);
        updated.Progress.Should().Be(0.5);

        // 4. All
        var all = this.repository.All();
        all.Should().HaveCount(1);

        // 5. Delete
        this.repository.Delete(inserted.Id);
        var deleted = this.repository.Get(inserted.Id);
        deleted.Should().BeNull();
    }

    [Test]
    public void Insert_And_Get_With_TagIds_Persists_List()
    {
        var torrent = new Torrent
        {
            Name = "Debian.iso",
            InfoHash = "abcdef0123456789abcdef0123456789abcdef01",
            Category = "linux",
            TotalSize = 2000000000,
            Status = TorrentStatus.Downloading,
            DateAdded = DateTime.UtcNow,
            TagIds = new System.Collections.Generic.List<int> { 1, 2, 42 },
        };

        var inserted = this.repository.Insert(torrent);
        inserted.Id.Should().BeGreaterThan(0);

        var fetched = this.repository.Get(inserted.Id);
        fetched.Should().NotBeNull();
        fetched.TagIds.Should().NotBeNull();
        fetched.TagIds.Should().BeEquivalentTo(new[] { 1, 2, 42 });

        fetched.TagIds.Add(99);
        this.repository.Update(fetched);

        var updatedFetched = this.repository.Get(inserted.Id);
        updatedFetched.TagIds.Should().BeEquivalentTo(new[] { 1, 2, 42, 99 });
    }
}

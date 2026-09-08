// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Messaging.Events;
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

    [Test]
    public void InsertMany_BatchesRecordsInTransaction_AssignsIdsAndPersists()
    {
        var torrents = new System.Collections.Generic.List<Torrent>();
        for (var i = 1; i <= 50; i++)
        {
            torrents.Add(new Torrent
            {
                Name = $"Batch.Torrent.{i}",
                InfoHash = $"0123456789abcdef0123456789abcdef{i:D8}",
                Category = "batch",
                TotalSize = 1000 * i,
                Status = TorrentStatus.Downloading,
                DateAdded = DateTime.UtcNow,
            });
        }

        this.repository.InsertMany(torrents);

        foreach (var t in torrents)
        {
            t.Id.Should().BeGreaterThan(0);
        }

        var all = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(this.repository.All(), t => t.Category == "batch"));
        all.Should().HaveCount(50);
    }

    [Test]
    public void Database_OpenConnection_AppliesBusyTimeout()
    {
        var connectionString = $"Data Source={this.dbPath};";
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);

        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "PRAGMA busy_timeout;";
        var busyTimeout = Convert.ToInt32(cmd.ExecuteScalar());
        busyTimeout.Should().Be(5000);

        cmd.CommandText = "PRAGMA cache_size;";
        var cacheSize = Convert.ToInt32(cmd.ExecuteScalar());
        cacheSize.Should().Be(-64000);

        cmd.CommandText = "PRAGMA synchronous;";
        var synchronous = Convert.ToInt32(cmd.ExecuteScalar());
        synchronous.Should().Be(1);
    }

    [Test]
    public void Database_OpenConnection_WithCustomBusyTimeout_AppliesCustomValue()
    {
        var connectionString = $"Data Source={this.dbPath};";
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite, busyTimeout: 12000);

        using var connection = database.OpenConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = "PRAGMA busy_timeout;";
        var busyTimeout = Convert.ToInt32(cmd.ExecuteScalar());
        busyTimeout.Should().Be(12000);
    }

    [Test]
    public void Concurrent_Inserts_Succeed_Under_Concurrency()
    {
        var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();
        for (var i = 0; i < 20; i++)
        {
            var index = i;
            tasks.Add(System.Threading.Tasks.Task.Run(() =>
            {
                this.repository.Insert(new Torrent
                {
                    Name = $"Concurrent.Torrent.{index}",
                    InfoHash = $"0123456789abcdef0123456789abcdef{index:D8}",
                    Category = "concurrent",
                    TotalSize = 1000 * (index + 1),
                    Status = TorrentStatus.Downloading,
                    DateAdded = DateTime.UtcNow,
                });
            }));
        }

        System.Threading.Tasks.Task.WaitAll(tasks.ToArray());

        var all = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(this.repository.All(), t => t.Category == "concurrent"));
        all.Should().HaveCount(20);
    }

    [Test]
    public void UpsertMany_WhenEventAggregatorThrows_TransactionIsStillCommittedAndDataPersisted()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        eventAggregator.When(ea => ea.PublishEvent(Arg.Any<ModelEvent<Torrent>>()))
            .Do(_ => throw new InvalidOperationException("Event handler crashed"));

        var connectionString = $"Data Source={this.dbPath};";
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);
        var repoWithFailingEvents = new BasicRepository<Torrent>(database, eventAggregator);

        var torrents = new System.Collections.Generic.List<Torrent>
        {
            new()
            {
                Name = "EventFail.Torrent",
                InfoHash = "1111222233334444555566667777888899990000",
                Category = "eventfail",
                TotalSize = 5000,
                Status = TorrentStatus.Downloading,
                DateAdded = DateTime.UtcNow,
            },
        };

        var act = () => repoWithFailingEvents.InsertMany(torrents);
        act.Should().Throw<InvalidOperationException>().WithMessage("Event handler crashed");

        // The transaction must have been committed before event dispatch was attempted
        var fetched = this.repository.Get(torrents[0].Id);
        fetched.Should().NotBeNull();
        fetched.Name.Should().Be("EventFail.Torrent");
    }

    [Test]
    public void TableMapping_GetInsertSql_RecognizesCustomTypeHandlers()
    {
        TableRegistration.RegisterTypeHandlers();
        var sql = TableMapping.GetInsertSql("Torrents", new Torrent());

        sql.Should().Contain("\"TagIds\"");
        sql.Should().Contain("\"Ratio\"");
        sql.Should().Contain("\"Progress\"");
    }

    [Test]
    public void ExecuteWithRetry_RetriesOnSqliteBusy_AndSucceeds()
    {
        var connectionString = $"Data Source={this.dbPath};";
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);
        var repo = new TestableRepository(database);

        var attempts = 0;
        var result = repo.TestExecuteWithRetry(conn =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new SqliteException("database is locked", 5);
            }

            return 42;
        });

        attempts.Should().Be(3);
        result.Should().Be(42);
    }

    [Test]
    public void ExecuteWithRetry_Void_RetriesOnSqliteBusy_AndSucceeds()
    {
        var connectionString = $"Data Source={this.dbPath};";
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);
        var repo = new TestableRepository(database);

        var attempts = 0;
        repo.TestExecuteWithRetry(conn =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new SqliteException("database is locked", 5);
            }
        });

        attempts.Should().Be(2);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_RetriesOnSqliteBusy_AndSucceeds()
    {
        var connectionString = $"Data Source={this.dbPath};";
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);
        var repo = new TestableRepository(database);

        var attempts = 0;
        var result = await repo.TestExecuteWithRetryAsync(async conn =>
        {
            await Task.Yield();
            attempts++;
            if (attempts < 3)
            {
                throw new SqliteException("database is locked", 5);
            }

            return "success";
        });

        attempts.Should().Be(3);
        result.Should().Be("success");
    }

    [Test]
    public async Task ExecuteWithRetryAsync_Void_RetriesOnSqliteBusy_AndSucceeds()
    {
        var connectionString = $"Data Source={this.dbPath};";
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);
        var repo = new TestableRepository(database);

        var attempts = 0;
        await repo.TestExecuteWithRetryAsync(async conn =>
        {
            await Task.Yield();
            attempts++;
            if (attempts < 2)
            {
                throw new SqliteException("database is locked", 5);
            }
        });

        attempts.Should().Be(2);
    }

    [Test]
    public void ExecuteWithRetry_ThrowsNonBusySqliteException_WithoutRetrying()
    {
        var connectionString = $"Data Source={this.dbPath};";
        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);
        var repo = new TestableRepository(database);

        var attempts = 0;
        var act = () => repo.TestExecuteWithRetry<int>(conn =>
        {
            attempts++;
            throw new SqliteException("syntax error", 1);
        });

        act.Should().Throw<SqliteException>().Where(ex => ex.SqliteErrorCode == 1);
        attempts.Should().Be(1);
    }

    private class TestableRepository : BasicRepository<Torrent>
    {
        public TestableRepository(IDatabase database, IEventAggregator eventAggregator = null)
            : base(database, eventAggregator)
        {
        }

        public TResult TestExecuteWithRetry<TResult>(Func<System.Data.IDbConnection, TResult> action) =>
            this.ExecuteWithRetry(action);

        public void TestExecuteWithRetry(Action<System.Data.IDbConnection> action) =>
            this.ExecuteWithRetry(action);

        public Task<TResult> TestExecuteWithRetryAsync<TResult>(Func<System.Data.IDbConnection, Task<TResult>> action) =>
            this.ExecuteWithRetryAsync(action);

        public Task TestExecuteWithRetryAsync(Func<System.Data.IDbConnection, Task> action) =>
            this.ExecuteWithRetryAsync(action);
    }
}

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
using NzbDrone.Core.Messaging.Commands;

namespace Leecharr.Core.Test.Messaging;

[TestFixture]
public class CommandRepositoryTest
{
    private string dbPath = null!;
    private CommandRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-cmd-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new CommandRepository(database);
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
    public void FindExisting_WhenQueuedCommandExists_ReturnsExisting()
    {
        var model = new CommandModel
        {
            Name = "RssSyncCommand",
            Body = "{\"indexerId\":1}",
            Status = CommandStatus.Queued,
            QueuedAt = DateTime.UtcNow,
        };
        this.repository.Insert(model);

        var found = this.repository.FindExisting("RssSyncCommand", "{\"indexerId\":1}");

        found.Should().NotBeNull();
        found!.Id.Should().Be(model.Id);
    }

    [Test]
    public void FindExisting_WhenRunningCommandExists_ReturnsExisting()
    {
        var model = new CommandModel
        {
            Name = "BackupCommand",
            Body = "{}",
            Status = CommandStatus.Running,
            QueuedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
        };
        this.repository.Insert(model);

        var found = this.repository.FindExisting("Backup", "{}");

        found.Should().NotBeNull();
        found!.Id.Should().Be(model.Id);
    }

    [Test]
    public void FindExisting_WhenCommandHasCompletedOrFailed_ReturnsNull()
    {
        var modelCompleted = new CommandModel
        {
            Name = "WatchFolderScan",
            Body = "{}",
            Status = CommandStatus.Completed,
            QueuedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
        };
        this.repository.Insert(modelCompleted);

        var found = this.repository.FindExisting("WatchFolderScan", "{}");

        found.Should().BeNull();
    }

    [Test]
    public void FindExisting_WhenPayloadsAreEquivalentJson_Matches()
    {
        var model = new CommandModel
        {
            Name = "TestCommand",
            Body = "{\"a\":1,\"b\":2}",
            Status = CommandStatus.Queued,
            QueuedAt = DateTime.UtcNow,
        };
        this.repository.Insert(model);

        var found = this.repository.FindExisting("TestCommand", "{\"b\":2,\"a\":1}");

        found.Should().NotBeNull();
        found!.Id.Should().Be(model.Id);
    }

    [Test]
    public void FindExisting_WhenPayloadsAreDifferent_ReturnsNull()
    {
        var model = new CommandModel
        {
            Name = "TestCommand",
            Body = "{\"a\":1}",
            Status = CommandStatus.Queued,
            QueuedAt = DateTime.UtcNow,
        };
        this.repository.Insert(model);

        var found = this.repository.FindExisting("TestCommand", "{\"a\":2}");

        found.Should().BeNull();
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class MigrationTest
{
    private string tempDbPath = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDbPath = Path.Combine(Path.GetTempPath(), $"leecharr-mig-test-{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(this.tempDbPath))
            {
                File.Delete(this.tempDbPath);
            }
        }
        catch
        {
            // Ignore during cleanup
        }
    }

    [Test]
    public void RunAllMigrations_CompletesSuccessfully()
    {
        var connectionString = $"Data Source={this.tempDbPath};";

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

        // Verify that tables were created in the database
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('Torrents', 'Categories', 'TorrentMediaMetadata', 'TrackerEntries', 'ArrConnectionDefinitions', 'SpeedSchedules', 'NotificationDefinitions', 'IndexerDefinitions', 'NetworkSettings');";

        var count = Convert.ToInt32(command.ExecuteScalar());
        count.Should().Be(9);
    }

    [Test]
    public void Migration018_CreatesForeignKeyAndPerformanceIndexes()
    {
        var connectionString = $"Data Source={this.tempDbPath};";

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

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT count(*) FROM sqlite_master 
            WHERE type='index' AND name IN (
                'IX_TorrentFiles_TorrentId',
                'IX_TrackerEntries_TorrentId',
                'IX_Torrents_Category',
                'IX_Torrents_Status',
                'IX_UserSessions_UserId_Expiry'
            );";

        var count = Convert.ToInt32(command.ExecuteScalar());
        count.Should().Be(5);
    }

    [Test]
    public void Migration021_AddsExternalUrlColumnToArrConnectionDefinitions()
    {
        var connectionString = $"Data Source={this.tempDbPath};";

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

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(ArrConnectionDefinitions);";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain("ExternalUrl");
    }

    [Test]
    public void Migration023_AddsProwlarrColumnsToIndexerDefinitions()
    {
        var connectionString = $"Data Source={this.tempDbPath};";

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

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(IndexerDefinitions);";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain("ProwlarrIndexerId");
        columns.Should().Contain("IsProwlarrManaged");
    }

    [Test]
    public void Migration024_AddsImportColumnsToTorrents()
    {
        var connectionString = $"Data Source={this.tempDbPath};";

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

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(Torrents);";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain("IsImported");
        columns.Should().Contain("ImportedAt");
        columns.Should().Contain("ImportedByArr");
        columns.Should().Contain("ImportPath");
    }
}

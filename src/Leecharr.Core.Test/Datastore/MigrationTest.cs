// Copyright (c) FeedItOut. All rights reserved.

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

    [Test]
    public void Migration025_AddsMusicbrainzAndMediaFieldsToTorrentMediaMetadata()
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
        command.CommandText = "PRAGMA table_info(TorrentMediaMetadata);";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain("MusicBrainzId");
        columns.Should().Contain("ArtistName");
        columns.Should().Contain("AlbumTitle");
        columns.Should().Contain("Cast");
        columns.Should().Contain("BannerUrl");
    }

    [Test]
    public void Migration044_AddsBannerLocalPathColumn()
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
        command.CommandText = "PRAGMA table_info(TorrentMediaMetadata);";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain("BannerLocalPath");
    }

    [Test]
    public void Migration026_CreatesMissingPerformanceIndexes()
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
                'IX_DownloadHistory_TorrentId',
                'IX_Commands_Status_QueuedAt',
                'IX_Commands_Status_EndedAt',
                'IX_TrackerBoostTrackers_Enabled_Status_LatencyMs'
            );";

        var count = Convert.ToInt32(command.ExecuteScalar());
        count.Should().Be(4);
    }

    [Test]
    public void Migration027_ExecutesSuccessfully()
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
            var act = () => runner.MigrateUp();
            act.Should().NotThrow();
        }
    }

    [Test]
    public void Migration031_AddsIsPrivateAndTrackersToDownloadHistory()
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
        command.CommandText = "PRAGMA table_info(DownloadHistory);";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain("IsPrivate");
        columns.Should().Contain("Trackers");
    }

    [Test]
    public void Migration032_CreatesUserExternalLoginsIndexes()
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
                'IX_UserExternalLogins_UserId',
                'IX_UserExternalLogins_Provider_Key'
            );";

        var count = Convert.ToInt32(command.ExecuteScalar());
        count.Should().Be(2);

        command.CommandText = @"
            INSERT INTO Users (Identifier, Username, CreatedAt, UpdatedAt)
            VALUES ('11111111-1111-1111-1111-111111111111', 'testuser', datetime('now'), datetime('now'));
            INSERT INTO UserExternalLogins (UserId, LoginProvider, ProviderKey, LinkedAt)
            VALUES (1, 'Google', 'google-key-1', datetime('now'));";
        command.ExecuteNonQuery();

        var duplicateInsert = () =>
        {
            using var dupCmd = connection.CreateCommand();
            dupCmd.CommandText = @"
                INSERT INTO UserExternalLogins (UserId, LoginProvider, ProviderKey, LinkedAt)
                VALUES (1, 'Google', 'google-key-1', datetime('now'));";
            dupCmd.ExecuteNonQuery();
        };

        duplicateInsert.Should().Throw<SqliteException>();
    }

    [Test]
    public void Migration033_CreatesQueryPerformanceCompositeIndexes()
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
                'IX_Torrents_QueuePosition',
                'IX_Torrents_Status_Category',
                'IX_TorrentFiles_TorrentId_Id',
                'IX_DownloadHistory_Status_DateAdded',
                'IX_DownloadHistory_InfoHash_Id'
            );";

        var count = Convert.ToInt32(command.ExecuteScalar());
        count.Should().Be(5);
    }

    [Test]
    public void Migration039_AddsThresholdAndSmallTorrentLimitColumnsToTorrents()
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
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        columns.Should().Contain("Threshold");
        columns.Should().Contain("SmallTorrentLimit");
    }

    [Test]
    public void Migration045_BackfillsPauseShareLimitActionToDefault()
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
            runner.MigrateUp(44);
        }

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var seed = connection.CreateCommand();
            seed.CommandText = @"
                INSERT INTO Torrents (Name, InfoHash, DateAdded, ShareLimitAction)
                VALUES ('pause-torrent', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', datetime('now'), 'Pause'),
                       ('remove-torrent', 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', datetime('now'), 'Remove');";
            seed.ExecuteNonQuery();
        }

        using (var scope = serviceProvider.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp(45);
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ShareLimitAction FROM Torrents WHERE InfoHash = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';";
        command.ExecuteScalar().Should().Be("Default");

        command.CommandText = "SELECT ShareLimitAction FROM Torrents WHERE InfoHash = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';";
        command.ExecuteScalar().Should().Be("Remove");
    }

    [Test]
    public void Migration046_BackfillsLegacyIsImportedToTrue()
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
            runner.MigrateUp(23);
        }

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var seed = connection.CreateCommand();
            seed.CommandText = @"
                INSERT INTO Torrents (Name, InfoHash, DateAdded)
                VALUES ('legacy-torrent', 'cccccccccccccccccccccccccccccccccccccccc', datetime('now'));";
            seed.ExecuteNonQuery();
        }

        using (var scope = serviceProvider.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp(24);
        }

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT IsImported FROM Torrents WHERE InfoHash = 'cccccccccccccccccccccccccccccccccccccccc';";
            Convert.ToInt32(check.ExecuteScalar()).Should().Be(0);
        }

        using (var scope = serviceProvider.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp(46);
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsImported FROM Torrents WHERE InfoHash = 'cccccccccccccccccccccccccccccccccccccccc';";
        Convert.ToInt32(command.ExecuteScalar()).Should().Be(1);
    }

    [Test]
    public void Migration047_BackfillsArrConnectionWebhookAndAutoAddToFalse()
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
            runner.MigrateUp(37);
        }

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var seed = connection.CreateCommand();
            seed.CommandText = @"
                INSERT INTO ArrConnectionDefinitions (Name, Implementation, Url, ApiKey, ArrType)
                VALUES ('legacy-sonarr', 'Sonarr', 'http://localhost:8989', 'test-key', 'sonarr');";
            seed.ExecuteNonQuery();
        }

        using (var scope = serviceProvider.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp(38);
        }

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT EnableAutomaticAdd, WebhookEnabled FROM ArrConnectionDefinitions WHERE Name = 'legacy-sonarr';";
            using var reader = check.ExecuteReader();
            reader.Read().Should().BeTrue();
            Convert.ToInt32(reader.GetValue(0)).Should().Be(0);
            Convert.ToInt32(reader.GetValue(1)).Should().Be(0);

            using var simulateOldMigration = connection.CreateCommand();
            simulateOldMigration.CommandText = @"
                UPDATE ArrConnectionDefinitions
                SET EnableAutomaticAdd = 1, WebhookEnabled = 1
                WHERE Name = 'legacy-sonarr';";
            simulateOldMigration.ExecuteNonQuery();
        }

        using (var scope = serviceProvider.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp(47);
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EnableAutomaticAdd, WebhookEnabled FROM ArrConnectionDefinitions WHERE Name = 'legacy-sonarr';";
        using var result = command.ExecuteReader();
        result.Read().Should().BeTrue();
        Convert.ToInt32(result.GetValue(0)).Should().Be(0);
        Convert.ToInt32(result.GetValue(1)).Should().Be(0);
    }
}

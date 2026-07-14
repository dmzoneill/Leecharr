using System;
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
    private string _tempDbPath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"leecharr-mig-test-{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
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
        var connectionString = $"Data Source={_tempDbPath};";

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
}

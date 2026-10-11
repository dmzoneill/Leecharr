// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.TrackerBoost;

namespace Leecharr.Core.Test.TrackerBoost;

[TestFixture]
public class TrackerBoostTrackerRepositoryTest
{
    private string dbPath = null!;
    private TrackerBoostTrackerRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-tb-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new TrackerBoostTrackerRepository(database);
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
    public void FindByUrl_MatchesLegacyRowWhenLookupUsesCanonicalForm()
    {
        this.repository.Insert(new TrackerBoostTracker
        {
            Url = "https://tracker.tamersunion.org:443/announce",
            Host = "tracker.tamersunion.org",
            Port = 443,
            Protocol = TrackerProtocol.Https,
            Status = TrackerHealthStatus.Untested,
            Source = TrackerSourceType.PublicList,
            SourceName = "test",
            Enabled = true,
        });

        var found = this.repository.FindByUrl("https://tracker.tamersunion.org/announce");

        found.Should().NotBeNull();
        found!.Url.Should().Be("https://tracker.tamersunion.org:443/announce");
    }

    [Test]
    public void FindByUrl_MatchesTrailingSlashVariant()
    {
        this.repository.Insert(new TrackerBoostTracker
        {
            Url = "http://tracker.example/announce",
            Host = "tracker.example",
            Port = 80,
            Protocol = TrackerProtocol.Http,
            Status = TrackerHealthStatus.Untested,
            Source = TrackerSourceType.PublicList,
            SourceName = "test",
            Enabled = true,
        });

        var found = this.repository.FindByUrl("http://tracker.example/announce/");

        found.Should().NotBeNull();
        found!.Host.Should().Be("tracker.example");
    }
}

// Copyright (c) FeedItOut. All rights reserved.

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
using NzbDrone.Core.Trackers.Metrics;

namespace Leecharr.Core.Test.Trackers;

[TestFixture]
public class TrackerMetricSnapshotRepositoryTest
{
    private string dbPath = null!;
    private TrackerMetricSnapshotRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-snapshot-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new TrackerMetricSnapshotRepository(database);
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
    public void GetHistory_WhenMoreSnapshotsThanLimit_ReturnsNewestRowsInChronologicalOrder()
    {
        const int metricId = 42;
        var since = DateTime.UtcNow.AddHours(-2);
        var baseTime = since.AddMinutes(5);
        var snapshots = Enumerable.Range(0, 6)
            .Select(i => new TrackerMetricSnapshot
            {
                TrackerMetricId = metricId,
                TrackerUrl = "udp://history-limit.tracker.org:1337/announce",
                Timestamp = baseTime.AddMinutes(i * 10),
                Operation = "Announce",
                IsSuccess = true,
                ResponseTimeMs = 10 + i,
            })
            .ToArray();

        this.repository.InsertMany(snapshots);

        var history = this.repository.GetHistory(metricId, since, limit: 3);

        history.Should().HaveCount(3);
        history.Select(s => s.Timestamp).Should().BeInAscendingOrder();
        history.Select(s => s.ResponseTimeMs).Should().Equal(13, 14, 15);
    }

    [Test]
    public void GetHourlyAggregatedMetrics_WhenPeerSumExceedsInt32_Max_DoesNotOverflow()
    {
        var bucketStart = DateTime.UtcNow.Date.AddHours(12);
        var perSnapshot = (int.MaxValue / 2) + 1;
        var expectedTotal = (long)perSnapshot * 2;

        this.repository.InsertMany(new[]
        {
            new TrackerMetricSnapshot
            {
                TrackerMetricId = 1,
                TrackerUrl = "udp://overflow.tracker.org:1337/announce",
                Timestamp = bucketStart.AddMinutes(5),
                PeersDiscovered = perSnapshot,
                Operation = "Announce",
                IsSuccess = true,
            },
            new TrackerMetricSnapshot
            {
                TrackerMetricId = 1,
                TrackerUrl = "udp://overflow.tracker.org:1337/announce",
                Timestamp = bucketStart.AddMinutes(30),
                PeersDiscovered = perSnapshot,
                Operation = "Announce",
                IsSuccess = true,
            },
        });

        var aggregated = this.repository.GetHourlyAggregatedMetrics(bucketStart.AddHours(-1));

        aggregated.Should().NotBeNull();
        aggregated.Should().ContainSingle();
        aggregated[0].PeersDiscovered.Should().Be(expectedTotal);
    }
}

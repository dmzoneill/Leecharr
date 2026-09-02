using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;

namespace Leecharr.Core.Test.Bandwidth;

[TestFixture]
public class SpeedScheduleRepositoryTest
{
    private string _dbPath = null!;
    private SpeedScheduleRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-speed-repo-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};";

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
        _repository = new SpeedScheduleRepository(database);
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void GetEnabled_ReturnsEnabledSchedulesOrderedByPriority()
    {
        var schedule1 = new SpeedSchedule
        {
            Name = "Night Throttling",
            Days = 127,
            StartTime = "01:00:00",
            EndTime = "06:00:00",
            MaxDownloadSpeed = 1000,
            MaxUploadSpeed = 500,
            IsEnabled = true,
            Priority = 2
        };
        var schedule2 = new SpeedSchedule
        {
            Name = "Peak Hours",
            Days = 127,
            StartTime = "18:00:00",
            EndTime = "23:00:00",
            MaxDownloadSpeed = 500,
            MaxUploadSpeed = 250,
            IsEnabled = true,
            Priority = 1
        };
        var disabledSchedule = new SpeedSchedule
        {
            Name = "Weekend Boost",
            Days = 96,
            StartTime = "00:00:00",
            EndTime = "23:59:59",
            MaxDownloadSpeed = 10000,
            MaxUploadSpeed = 5000,
            IsEnabled = false,
            Priority = 0
        };

        _repository.Insert(schedule1);
        _repository.Insert(schedule2);
        _repository.Insert(disabledSchedule);

        var result = _repository.GetEnabled().ToList();

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Peak Hours");
        result[0].Priority.Should().Be(1);
        result[1].Name.Should().Be("Night Throttling");
        result[1].Priority.Should().Be(2);
    }

    [Test]
    public void GetEnabled_WhenNoneEnabled_ReturnsEmpty()
    {
        var disabledSchedule = new SpeedSchedule
        {
            Name = "Disabled",
            IsEnabled = false,
            Priority = 1
        };
        _repository.Insert(disabledSchedule);

        var result = _repository.GetEnabled().ToList();

        result.Should().BeEmpty();
    }
}

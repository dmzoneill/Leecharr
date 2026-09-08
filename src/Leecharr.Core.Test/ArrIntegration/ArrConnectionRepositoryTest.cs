// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrConnectionRepositoryTest
{
    private string dbPath = null!;
    private ArrConnectionRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-arrconn-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new ArrConnectionRepository(database);
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
    public void GetByAffinity_WithCategoryMatch_ReturnsHighestScoringConnection()
    {
        this.repository.Insert(new ArrConnectionDefinition
        {
            Name = "Sonarr TV",
            ArrType = "Sonarr",
            Implementation = "Sonarr",
            Url = "http://sonarr-tv:8989",
            ApiKey = "key1",
            Enable = true,
            Priority = 2,
        });

        this.repository.Insert(new ArrConnectionDefinition
        {
            Name = "Sonarr Anime",
            ArrType = "Sonarr",
            Implementation = "Sonarr",
            Url = "http://sonarr-anime:8989",
            ApiKey = "key2",
            Enable = true,
            Priority = 1,
        });

        var matchAnime = this.repository.GetByAffinity("Sonarr", category: "anime");
        matchAnime.Should().NotBeNull();
        matchAnime.Name.Should().Be("Sonarr Anime");

        var matchTv = this.repository.GetByAffinity("Sonarr", category: "tv");
        matchTv.Should().NotBeNull();
        matchTv.Name.Should().Be("Sonarr TV");
    }

    [Test]
    public void GetByAffinity_WithTagMatch_ReturnsMatchingConnection()
    {
        this.repository.Insert(new ArrConnectionDefinition
        {
            Name = "Radarr Standard",
            ArrType = "Radarr",
            Implementation = "Radarr",
            Url = "http://radarr:7878",
            ApiKey = "key1",
            Enable = true,
            Priority = 2,
        });

        this.repository.Insert(new ArrConnectionDefinition
        {
            Name = "Radarr 4K",
            ArrType = "Radarr",
            Implementation = "Radarr",
            Url = "http://radarr-4k:7878",
            ApiKey = "key2",
            Enable = true,
            Priority = 1,
        });

        var match4k = this.repository.GetByAffinity("Radarr", tag: "4k");
        match4k.Should().NotBeNull();
        match4k.Name.Should().Be("Radarr 4K");
    }

    [Test]
    public void GetByAffinity_WhenOnlyTypeGiven_ReturnsTopPriority()
    {
        this.repository.Insert(new ArrConnectionDefinition
        {
            Name = "Primary Sonarr",
            ArrType = "Sonarr",
            Implementation = "Sonarr",
            Url = "http://sonarr1:8989",
            ApiKey = "key1",
            Enable = true,
            Priority = 1,
        });

        this.repository.Insert(new ArrConnectionDefinition
        {
            Name = "Secondary Sonarr",
            ArrType = "Sonarr",
            Implementation = "Sonarr",
            Url = "http://sonarr2:8989",
            ApiKey = "key2",
            Enable = true,
            Priority = 2,
        });

        var match = this.repository.GetByAffinity("Sonarr");
        match.Should().NotBeNull();
        match.Name.Should().Be("Primary Sonarr");
    }

    [Test]
    public void GetByAffinity_WhenNoConnections_ReturnsNull()
    {
        var match = this.repository.GetByAffinity("Sonarr", "tv", "hd");
        match.Should().BeNull();
    }
}

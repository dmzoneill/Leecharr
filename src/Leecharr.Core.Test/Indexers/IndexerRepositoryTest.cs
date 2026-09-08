// Copyright (c) PlaceholderCompany. All rights reserved.

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
using NzbDrone.Core.Indexers;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class IndexerRepositoryTest
{
    private string dbPath = null!;
    private IndexerRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-indexer-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new IndexerRepository(database);
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
    public void GetEnabled_ReturnsEnabledIndexersOrderedByPriority()
    {
        var indexer1 = new IndexerDefinition { Name = "Indexer 1", Url = "https://indexer1.com", Enable = true, Priority = 10, ConfigContract = "TorznabSettings", Settings = "{}" };
        var indexer2 = new IndexerDefinition { Name = "Indexer 2", Url = "https://indexer2.com", Enable = true, Priority = 5, ConfigContract = "TorznabSettings", Settings = "{}" };
        var indexerDisabled = new IndexerDefinition { Name = "Disabled Indexer", Url = "https://disabled.com", Enable = false, Priority = 1, ConfigContract = "TorznabSettings", Settings = "{}" };

        this.repository.Insert(indexer1);
        this.repository.Insert(indexer2);
        this.repository.Insert(indexerDisabled);

        var result = this.repository.GetEnabled().ToList();

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Indexer 2");
        result[1].Name.Should().Be("Indexer 1");
    }

    [Test]
    public void GetSearchEnabled_ReturnsOnlySearchEnabledAndEnabledIndexers()
    {
        var indexerSearch = new IndexerDefinition { Name = "Search Indexer", Url = "https://search.com", Enable = true, EnableSearch = true, EnableRss = false, Priority = 1, ConfigContract = "TorznabSettings", Settings = "{}" };
        var indexerRssOnly = new IndexerDefinition { Name = "RSS Only Indexer", Url = "https://rss.com", Enable = true, EnableSearch = false, EnableRss = true, Priority = 2, ConfigContract = "TorznabSettings", Settings = "{}" };
        var indexerDisabled = new IndexerDefinition { Name = "Disabled Indexer", Url = "https://disabled.com", Enable = false, EnableSearch = true, EnableRss = true, Priority = 3, ConfigContract = "TorznabSettings", Settings = "{}" };

        this.repository.Insert(indexerSearch);
        this.repository.Insert(indexerRssOnly);
        this.repository.Insert(indexerDisabled);

        var result = this.repository.GetSearchEnabled().ToList();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Search Indexer");
    }

    [Test]
    public void GetRssEnabled_ReturnsOnlyRssEnabledAndEnabledIndexers()
    {
        var indexerSearchOnly = new IndexerDefinition { Name = "Search Only", Url = "https://search.com", Enable = true, EnableSearch = true, EnableRss = false, Priority = 1, ConfigContract = "TorznabSettings", Settings = "{}" };
        var indexerRss = new IndexerDefinition { Name = "RSS Indexer", Url = "https://rss.com", Enable = true, EnableSearch = false, EnableRss = true, Priority = 2, ConfigContract = "TorznabSettings", Settings = "{}" };

        this.repository.Insert(indexerSearchOnly);
        this.repository.Insert(indexerRss);

        var result = this.repository.GetRssEnabled().ToList();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("RSS Indexer");
    }
}

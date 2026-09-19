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
using NzbDrone.Core.Tags;

namespace Leecharr.Core.Test.Tags;

[TestFixture]
public class TagRepositoryTest
{
    private string dbPath = null!;
    private TagRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-tag-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new TagRepository(database);
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
    public void GetByLabel_WhenLabelMatchesCaseInsensitively_ReturnsTag()
    {
        var tag = new Tag { Label = "Movies" };
        this.repository.Insert(tag);

        var resultLower = this.repository.GetByLabel("movies");
        var resultUpper = this.repository.GetByLabel("MOVIES");
        var resultExact = this.repository.GetByLabel("Movies");

        resultLower.Should().NotBeNull();
        resultLower!.Label.Should().Be("Movies");

        resultUpper.Should().NotBeNull();
        resultUpper!.Label.Should().Be("Movies");

        resultExact.Should().NotBeNull();
        resultExact!.Label.Should().Be("Movies");
    }

    [Test]
    public void GetByLabel_WhenLabelDoesNotExist_ReturnsNull()
    {
        var result = this.repository.GetByLabel("nonexistent");
        result.Should().BeNull();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetByLabel_WhenLabelIsNullOrWhitespace_ReturnsNull(string label)
    {
        var result = this.repository.GetByLabel(label);
        result.Should().BeNull();
    }
}

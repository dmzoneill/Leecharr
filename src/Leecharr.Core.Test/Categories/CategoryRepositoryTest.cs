using System;
using System.IO;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;

namespace Leecharr.Core.Test.Categories;

[TestFixture]
public class CategoryRepositoryTest
{
    private string _dbPath = null!;
    private CategoryRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-cat-repo-test-{Guid.NewGuid():N}.db");
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
        _repository = new CategoryRepository(database);
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
    public void GetByName_ReturnsMatchingCategory()
    {
        var category = new Category
        {
            Name = "movies",
            SavePath = "/downloads/movies",
            IsDefault = false,
            TargetRatio = 1.5
        };
        _repository.Insert(category);

        var result = _repository.GetByName("movies");

        result.Should().NotBeNull();
        result.Name.Should().Be("movies");
        result.SavePath.Should().Be("/downloads/movies");
        result.TargetRatio.Should().Be(1.5);
    }

    [Test]
    public void GetByName_WhenNotFound_ReturnsNull()
    {
        var result = _repository.GetByName("nonexistent");
        result.Should().BeNull();
    }

    [Test]
    public void GetDefault_ReturnsDefaultCategory()
    {
        var regularCat = new Category
        {
            Name = "tv",
            SavePath = "/downloads/tv",
            IsDefault = false
        };
        var defaultCat = new Category
        {
            Name = "general",
            SavePath = "/downloads/general",
            IsDefault = true
        };

        _repository.Insert(regularCat);
        _repository.Insert(defaultCat);

        var result = _repository.GetDefault();

        result.Should().NotBeNull();
        result.Name.Should().Be("general");
        result.IsDefault.Should().BeTrue();
    }

    [Test]
    public void GetDefault_WhenNoDefaultExists_ReturnsNull()
    {
        var regularCat = new Category
        {
            Name = "music",
            SavePath = "/downloads/music",
            IsDefault = false
        };
        _repository.Insert(regularCat);

        var result = _repository.GetDefault();

        result.Should().BeNull();
    }
}

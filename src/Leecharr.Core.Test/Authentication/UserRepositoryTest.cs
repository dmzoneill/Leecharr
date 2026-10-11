// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class UserRepositoryTest
{
    private string dbPath = null!;
    private UserRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-user-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new UserRepository(database);
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
    public void FindByEmail_IsCaseInsensitive()
    {
        var inserted = this.repository.Insert(new User
        {
            Identifier = Guid.NewGuid(),
            Username = "alice",
            Email = "alice@example.com",
            Roles = "[\"User\"]",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var byEmail = this.repository.FindByEmail("ALICE@example.com");

        byEmail.Should().NotBeNull();
        byEmail!.Id.Should().Be(inserted.Id);
    }

    [Test]
    public void FindByEmailForProvider_ReturnsUserForMatchingEmail()
    {
        var inserted = this.repository.Insert(new User
        {
            Identifier = Guid.NewGuid(),
            Username = "bob_kc",
            Email = "bob@example.com",
            ExternalProviderId = "keycloak",
            Roles = "[\"User\"]",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var result = this.repository.FindByEmailForProvider("bob@example.com", "keycloak");

        result.Should().NotBeNull();
        result!.Id.Should().Be(inserted.Id);
    }

    [Test]
    public void Insert_RejectsDuplicateEmail_AfterMigration()
    {
        this.repository.Insert(new User
        {
            Identifier = Guid.NewGuid(),
            Username = "carol",
            Email = "carol@example.com",
            Roles = "[\"User\"]",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var act = () => this.repository.Insert(new User
        {
            Identifier = Guid.NewGuid(),
            Username = "carol2",
            Email = "carol@example.com",
            Roles = "[\"User\"]",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        act.Should().Throw<Exception>();
    }
}

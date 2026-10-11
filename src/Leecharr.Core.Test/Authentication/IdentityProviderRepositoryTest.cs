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
public class IdentityProviderRepositoryTest
{
    private string dbPath = null!;
    private IdentityProviderRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-idp-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new IdentityProviderRepository(database);
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
    public void FindByProviderId_IsCaseInsensitive()
    {
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "Okta",
            Name = "Okta SSO",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        this.repository.Insert(provider);

        var result = this.repository.FindByProviderId("okta");

        result.Should().NotBeNull();
        result!.ProviderId.Should().Be("Okta");
    }

    [Test]
    public void Insert_RejectsCaseVariantProviderId_AfterMigration()
    {
        this.repository.Insert(new IdentityProviderDefinition
        {
            ProviderId = "Okta",
            Name = "Okta SSO",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var act = () => this.repository.Insert(new IdentityProviderDefinition
        {
            ProviderId = "okta",
            Name = "Okta lower",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        act.Should().Throw<Exception>();
    }
}

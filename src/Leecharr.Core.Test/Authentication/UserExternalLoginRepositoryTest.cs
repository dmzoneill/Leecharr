// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Linq;
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
public class UserExternalLoginRepositoryTest
{
    private string dbPath = null!;
    private Database database = null!;
    private UserRepository userRepo = null!;
    private UserExternalLoginRepository externalLoginRepo = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-ext-login-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={this.dbPath};Foreign Keys=True;";

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
        this.database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);
        this.userRepo = new UserRepository(this.database);
        this.externalLoginRepo = new UserExternalLoginRepository(this.database);
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
    public void InsertAndFindByProvider_ReturnsExternalLogin()
    {
        var user = this.userRepo.Insert(new User
        {
            Username = "oauthuser",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var login = new UserExternalLogin
        {
            UserId = user.Id,
            LoginProvider = "Google",
            ProviderKey = "google-sub-12345",
            ProviderDisplayName = "Google Account",
            LinkedAt = DateTime.UtcNow,
        };

        var inserted = this.externalLoginRepo.Insert(login);
        inserted.Id.Should().BeGreaterThan(0);

        var fetched = this.externalLoginRepo.FindByProvider("Google", "google-sub-12345");
        fetched.Should().NotBeNull();
        fetched!.UserId.Should().Be(user.Id);
        fetched.LoginProvider.Should().Be("Google");
        fetched.ProviderKey.Should().Be("google-sub-12345");
    }

    [Test]
    public void FindByUserId_ReturnsAllUserExternalLogins()
    {
        var user = this.userRepo.Insert(new User
        {
            Username = "multiuser",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        this.externalLoginRepo.Insert(new UserExternalLogin
        {
            UserId = user.Id,
            LoginProvider = "Google",
            ProviderKey = "google-1",
            LinkedAt = DateTime.UtcNow.AddMinutes(-5),
        });

        this.externalLoginRepo.Insert(new UserExternalLogin
        {
            UserId = user.Id,
            LoginProvider = "GitHub",
            ProviderKey = "github-1",
            LinkedAt = DateTime.UtcNow,
        });

        var logins = this.externalLoginRepo.FindByUserId(user.Id).ToList();
        logins.Should().HaveCount(2);
    }

    [Test]
    public void DeleteByUserId_DeletesAllExternalLoginsForUser()
    {
        var user = this.userRepo.Insert(new User
        {
            Username = "deleteuser",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        this.externalLoginRepo.Insert(new UserExternalLogin
        {
            UserId = user.Id,
            LoginProvider = "Google",
            ProviderKey = "google-del-1",
            LinkedAt = DateTime.UtcNow,
        });

        this.externalLoginRepo.DeleteByUserId(user.Id);

        var logins = this.externalLoginRepo.FindByUserId(user.Id).ToList();
        logins.Should().BeEmpty();
    }
}

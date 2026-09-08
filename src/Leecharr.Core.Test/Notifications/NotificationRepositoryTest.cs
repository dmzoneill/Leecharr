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
using NzbDrone.Core.Notifications;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class NotificationRepositoryTest
{
    private string dbPath = null!;
    private NotificationRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-notif-repo-test-{Guid.NewGuid():N}.db");
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
        this.repository = new NotificationRepository(database);
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
    public void GetEnabled_ReturnsOnlyEnabledNotifications()
    {
        var enabledNotif = new NotificationDefinition
        {
            Name = "Discord Webhook",
            Implementation = "Webhook",
            Enable = true,
            ConfigContract = "WebhookSettings",
            Settings = "{}",
        };

        var disabledNotif = new NotificationDefinition
        {
            Name = "Telegram Bot",
            Implementation = "Telegram",
            Enable = false,
            ConfigContract = "TelegramSettings",
            Settings = "{}",
        };

        this.repository.Insert(enabledNotif);
        this.repository.Insert(disabledNotif);

        var enabled = this.repository.GetEnabled().ToList();

        enabled.Should().HaveCount(1);
        enabled[0].Name.Should().Be("Discord Webhook");
        enabled[0].Enable.Should().BeTrue();
    }
}

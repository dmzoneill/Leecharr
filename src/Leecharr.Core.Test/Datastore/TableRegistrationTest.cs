// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Network;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Datastore.Tests;

[TestFixture]
public class TableRegistrationTest
{
    private string tempDbPath = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDbPath = Path.Combine(Path.GetTempPath(), $"leecharr-table-reg-test-{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(this.tempDbPath))
            {
                File.Delete(this.tempDbPath);
            }
        }
        catch
        {
            // Ignore during cleanup
        }
    }

    [Test]
    public void RegisterTables_RegistersAllRequiredEntityTableMappings()
    {
        TableRegistration.RegisterTables();

        TableMapping.GetTableName(typeof(CommandModel)).Should().Be("Commands");
        TableMapping.GetTableName(typeof(ConfigModel)).Should().Be("Config");
        TableMapping.GetTableName(typeof(ScheduledTask)).Should().Be("ScheduledTasks");
        TableMapping.GetTableName(typeof(Tag)).Should().Be("Tags");
        TableMapping.GetTableName(typeof(Torrent)).Should().Be("Torrents");
        TableMapping.GetTableName(typeof(TorrentFile)).Should().Be("TorrentFiles");
        TableMapping.GetTableName(typeof(Category)).Should().Be("Categories");
        TableMapping.GetTableName(typeof(TorrentMediaMetadata)).Should().Be("TorrentMediaMetadata");
        TableMapping.GetTableName(typeof(TrackerEntry)).Should().Be("TrackerEntries");
        TableMapping.GetTableName(typeof(ArrConnectionDefinition)).Should().Be("ArrConnectionDefinitions");
        TableMapping.GetTableName(typeof(SpeedSchedule)).Should().Be("SpeedSchedules");
        TableMapping.GetTableName(typeof(DownloadHistory)).Should().Be("DownloadHistory");
        TableMapping.GetTableName(typeof(NetworkSettings)).Should().Be("NetworkSettings");
        TableMapping.GetTableName(typeof(NotificationDefinition)).Should().Be("NotificationDefinitions");
        TableMapping.GetTableName(typeof(IndexerDefinition)).Should().Be("IndexerDefinitions");
        TableMapping.GetTableName(typeof(RssRule)).Should().Be("RssRules");
        TableMapping.GetTableName(typeof(DownloadClientDefinition)).Should().Be("DownloadClientDefinitions");
        TableMapping.GetTableName(typeof(User)).Should().Be("Users");
        TableMapping.GetTableName(typeof(IdentityProviderDefinition)).Should().Be("IdentityProviders");
        TableMapping.GetTableName(typeof(UserSession)).Should().Be("UserSessions");
        TableMapping.GetTableName(typeof(UserExternalLogin)).Should().Be("UserExternalLogins");
        TableMapping.GetTableName(typeof(TrackerBoostTracker)).Should().Be("TrackerBoostTrackers");
    }

    [Test]
    public void RegisterTables_RegistersTypeHandlersForDapper()
    {
        TableRegistration.RegisterTables();

        SqlMapper.HasTypeHandler(typeof(double)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(TimeOnly)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(List<int>)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(List<string>)).Should().BeTrue();
        SqlMapper.HasTypeHandler(typeof(Dictionary<string, string>)).Should().BeTrue();
    }

    [Test]
    public void RegisterTables_CanBeCalledConcurrentlyWithoutException()
    {
        var tasks = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(() => TableRegistration.RegisterTables()));
        }

        var act = () => Task.WaitAll(tasks.ToArray());
        act.Should().NotThrow();
    }

    [Test]
    public void RegisterTypeHandlers_DelegatesToRegisterTables()
    {
        TableRegistration.RegisterTypeHandlers();

        TableMapping.GetTableName(typeof(CommandModel)).Should().Be("Commands");
        TableMapping.GetTableName(typeof(IdentityProviderDefinition)).Should().Be("IdentityProviders");
    }

    [Test]
    public void DbFactory_Create_EnsuresTableRegistrationAndMigrationsExecute()
    {
        var dbFactory = new DbFactory();
        var connString = $"Data Source={this.tempDbPath};";

        var database = dbFactory.Create(DatabaseType.SQLite, connString);
        database.Should().NotBeNull();
        database.DatabaseType.Should().Be(DatabaseType.SQLite);

        // Verify that tables and type handlers are registered
        TableMapping.GetTableName(typeof(CommandModel)).Should().Be("Commands");
        TableMapping.GetTableName(typeof(ConfigModel)).Should().Be("Config");
        TableMapping.GetTableName(typeof(DownloadHistory)).Should().Be("DownloadHistory");
        TableMapping.GetTableName(typeof(TorrentMediaMetadata)).Should().Be("TorrentMediaMetadata");
        TableMapping.GetTableName(typeof(IdentityProviderDefinition)).Should().Be("IdentityProviders");

        // Verify database migrations were executed by inspecting schema
        using var conn = database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('Commands', 'Config', 'Torrents', 'IdentityProviders');";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        count.Should().Be(4);
    }
}

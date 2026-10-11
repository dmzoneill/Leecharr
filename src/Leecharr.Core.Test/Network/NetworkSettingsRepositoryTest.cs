// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Network;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class NetworkSettingsRepositoryTest
{
    private string dbPath = null!;
    private NetworkSettingsRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.dbPath = Path.Combine(Path.GetTempPath(), $"leecharr-network-settings-test-{Guid.NewGuid():N}.db");
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
        this.repository = new NetworkSettingsRepository(database);
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
    public void GetSettings_WhenEmpty_ReturnsNewInstanceWithZeroId()
    {
        var settings = this.repository.GetSettings();

        settings.Id.Should().Be(0);
    }

    [Test]
    public void GetSettings_WhenMultipleRowsExist_ReturnsHighestId()
    {
        var legacyDbPath = Path.Combine(Path.GetTempPath(), $"leecharr-network-settings-legacy-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={legacyDbPath};";

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
            runner.MigrateUp(51);
        }

        using (var seedConnection = new SqliteConnection(connectionString))
        {
            seedConnection.Open();
            using var seed = seedConnection.CreateCommand();
            seed.CommandText = @"
                INSERT INTO NetworkSettings (BindInterface, EnableVpnKillSwitch, EnableUpnp, EnableNatPmp, ListenPort, RandomizePortOnLaunch, EnableProxy, ProxyType, ProxyPort, ProxyPeers, ProxyTrackers, ProxyIndexers, AnonymousMode, ClientEmulationPreset)
                VALUES ('eth0', 0, 1, 1, 6881, 0, 0, 'SOCKS5', 1080, 1, 1, 1, 0, 'Leecharr');
                INSERT INTO NetworkSettings (BindInterface, EnableVpnKillSwitch, EnableUpnp, EnableNatPmp, ListenPort, RandomizePortOnLaunch, EnableProxy, ProxyType, ProxyPort, ProxyPeers, ProxyTrackers, ProxyIndexers, AnonymousMode, ClientEmulationPreset)
                VALUES ('tun0', 1, 1, 1, 51413, 0, 0, 'SOCKS5', 1080, 1, 1, 1, 0, 'Leecharr');";
            seed.ExecuteNonQuery();
        }

        var database = new Database(() => new SqliteConnection(connectionString), DatabaseType.SQLite);
        var repository = new NetworkSettingsRepository(database);
        var settings = repository.GetSettings();

        settings.BindInterface.Should().Be("tun0");
        settings.ListenPort.Should().Be(51413);

        SqliteConnection.ClearAllPools();
        File.Delete(legacyDbPath);
    }

    [Test]
    public void Insert_SecondRow_ThrowsDueToSingletonIndex()
    {
        this.repository.Insert(new NetworkSettings { BindInterface = "eth0" });

        var act = () => this.repository.Insert(new NetworkSettings { BindInterface = "tun0" });

        act.Should().Throw<SqliteException>();
    }
}

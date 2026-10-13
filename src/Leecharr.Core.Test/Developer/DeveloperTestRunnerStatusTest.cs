// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Developer;
using NzbDrone.Core.Developer.Testing;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Network.Vpn;

namespace Leecharr.Core.Test.Developer;

[TestFixture]
public class DeveloperTestRunnerStatusTest
{
    [TestCase("db-integrity")]
    [TestCase("db-foreign-keys")]
    [TestCase("db-wal-checkpoint")]
    [TestCase("db-page-size")]
    public async Task RunTestAsync_WhenDatabaseIsMissing_Fails(string testId)
    {
        var runner = new DeveloperTestRunner();

        var result = await runner.RunTestAsync(testId);

        result.Status.Should().Be("Failed");
        result.Output.Should().Contain("not registered");
        result.ErrorMessage.Should().Contain("not registered");
    }

    [Test]
    public async Task RunTestAsync_WhenQuickCheckIsNotOk_Fails()
    {
        var (database, command) = CreateSqliteDatabase();
        command.ExecuteScalar().Returns("*** in database main ***");
        var runner = new DeveloperTestRunner(mainDatabase: database);

        var result = await runner.RunTestAsync("db-integrity");

        result.Status.Should().Be("Failed");
        result.Output.Should().Contain("Quick Check failed");
        result.Output.Should().Contain("*** in database main ***");
        result.Output.Should().NotContain("Quick Check passed");
    }

    [Test]
    public async Task RunTestAsync_WhenQuickCheckIsOk_Passes()
    {
        var (database, command) = CreateSqliteDatabase();
        command.ExecuteScalar().Returns("ok");
        var runner = new DeveloperTestRunner(mainDatabase: database);

        var result = await runner.RunTestAsync("db-integrity");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("Quick Check passed: 'ok'");
    }

    [Test]
    public async Task RunTestsAsync_WhenQuickCheckFails_CountsFailure()
    {
        var (database, command) = CreateSqliteDatabase();
        command.ExecuteScalar().Returns("row 1 missing from index");
        var runner = new DeveloperTestRunner(mainDatabase: database);

        var response = await runner.RunTestsAsync(new TestExecutionRequest
        {
            TestIds = new List<string> { "db-integrity" },
        });

        response.Passed.Should().Be(0);
        response.Failed.Should().Be(1);
        response.Results.Should().ContainSingle();
        response.Results[0].Status.Should().Be("Failed");
    }

    [Test]
    public async Task RunTestAsync_WhenForeignKeysAreViolated_Fails()
    {
        var (database, command) = CreateSqliteDatabase();
        var reader = Substitute.For<IDataReader>();
        reader.Read().Returns(true, true, false);
        command.ExecuteReader().Returns(reader);
        var runner = new DeveloperTestRunner(mainDatabase: database);

        var result = await runner.RunTestAsync("db-foreign-keys");

        result.Status.Should().Be("Failed");
        result.Output.Should().Contain("Found 2 constraint violations");
    }

    [Test]
    public async Task RunTestAsync_WhenForeignKeysAreClean_Passes()
    {
        var (database, command) = CreateSqliteDatabase();
        var reader = Substitute.For<IDataReader>();
        reader.Read().Returns(false);
        command.ExecuteReader().Returns(reader);
        var runner = new DeveloperTestRunner(mainDatabase: database);

        var result = await runner.RunTestAsync("db-foreign-keys");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("Found 0 constraint violations");
    }

    [Test]
    public async Task RunTestAsync_WhenWalCheckpointIsBusy_Fails()
    {
        var (database, command) = CreateSqliteDatabase();
        var reader = Substitute.For<IDataReader>();
        reader.Read().Returns(true);
        reader.GetInt32(0).Returns(1);
        reader.GetInt32(1).Returns(4);
        reader.GetInt32(2).Returns(0);
        command.ExecuteReader().Returns(reader);
        var runner = new DeveloperTestRunner(mainDatabase: database);

        var result = await runner.RunTestAsync("db-wal-checkpoint");

        result.Status.Should().Be("Failed");
        result.Output.Should().Contain("busy=1");
    }

    [Test]
    public async Task RunTestAsync_WhenWalCheckpointIsIdle_Passes()
    {
        var (database, command) = CreateSqliteDatabase();
        var reader = Substitute.For<IDataReader>();
        reader.Read().Returns(true);
        reader.GetInt32(0).Returns(0);
        reader.GetInt32(1).Returns(2);
        reader.GetInt32(2).Returns(2);
        command.ExecuteReader().Returns(reader);
        var runner = new DeveloperTestRunner(mainDatabase: database);

        var result = await runner.RunTestAsync("db-wal-checkpoint");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("busy=0");
    }

    [Test]
    public async Task RunTestAsync_WhenPagePragmasAreRead_ReportsAllThree()
    {
        var (database, command) = CreateSqliteDatabase();
        command.ExecuteScalar().Returns(4096, 12, 3);
        var runner = new DeveloperTestRunner(mainDatabase: database);

        var result = await runner.RunTestAsync("db-page-size");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("PageSize=4096");
        result.Output.Should().Contain("PageCount=12");
        result.Output.Should().Contain("Freelist=3");
    }

    [Test]
    public async Task RunTestAsync_WhenPageSizeIsInvalid_Fails()
    {
        var (database, command) = CreateSqliteDatabase();
        command.ExecuteScalar().Returns(0, 1, 0);
        var runner = new DeveloperTestRunner(mainDatabase: database);

        var result = await runner.RunTestAsync("db-page-size");

        result.Status.Should().Be("Failed");
        result.Output.Should().Contain("PageSize=0");
    }

    [Test]
    public async Task RunTestAsync_WhenTaskManagerIsMissing_DoesNotReportTaskActive()
    {
        var runner = new DeveloperTestRunner();

        var result = await runner.RunTestAsync("scheduler-watchfolder");

        result.Status.Should().Be("Failed");
        result.Output.Should().Contain("not registered");
        result.Output.Should().NotContain("Active=True");
    }

    [Test]
    public async Task RunTestAsync_WhenWatchFolderTaskIsMissing_Fails()
    {
        var taskManager = Substitute.For<ITaskManager>();
        taskManager.GetAll().Returns(new List<ScheduledTask>());
        var runner = new DeveloperTestRunner(taskManager: taskManager);

        var result = await runner.RunTestAsync("scheduler-watchfolder");

        result.Status.Should().Be("Failed");
        result.Output.Should().Contain("Active=False");
    }

    [Test]
    public async Task RunTestAsync_WhenWatchFolderTaskExists_Passes()
    {
        var taskManager = Substitute.For<ITaskManager>();
        taskManager.GetAll().Returns(new List<ScheduledTask>
        {
            new() { TypeName = "NzbDrone.Core.Jobs.WatchFolderService" },
        });
        var runner = new DeveloperTestRunner(taskManager: taskManager);

        var result = await runner.RunTestAsync("scheduler-watchfolder");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("Active=True");
    }

    [Test]
    public async Task RunTestAsync_WhenEngineChecksDoNotInspect_Fails()
    {
        var runner = new DeveloperTestRunner();

        var encryption = await runner.RunTestAsync("engine-encryption");
        var ports = await runner.RunTestAsync("engine-ports");
        var capabilities = await runner.RunTestAsync("engine-capabilities");

        encryption.Status.Should().Be("Failed");
        encryption.Output.Should().NotContain("PreferEncryption enabled");
        ports.Status.Should().Be("Failed");
        ports.Output.Should().NotContain("6881-6889");
        capabilities.Status.Should().Be("Failed");
        capabilities.Output.Should().NotContain("SequentialDownload=True");
    }

    [Test]
    public async Task RunTestAsync_WhenEngineSettingsArePresent_ReportsObservedValues()
    {
        var downloadEngine = Substitute.For<IDownloadEngine, ITorrentEngine>();
        var torrentEngine = (ITorrentEngine)downloadEngine;
        torrentEngine.Capabilities.Returns(new TorrentEngineCapabilities
        {
            SupportsEncryptionToggle = true,
            SupportsSequentialDownload = false,
            SupportsFastResume = true,
            SupportsDynamicRateLimits = false,
        });

        var configService = Substitute.For<IConfigService>();
        configService.EncryptionMode.Returns("preferEncrypted");
        configService.ListeningPort.Returns(51413);

        var runner = new DeveloperTestRunner(downloadEngine: downloadEngine, configService: configService);

        var encryption = await runner.RunTestAsync("engine-encryption");
        var ports = await runner.RunTestAsync("engine-ports");
        var capabilities = await runner.RunTestAsync("engine-capabilities");

        encryption.Status.Should().Be("Passed");
        encryption.Output.Should().Contain("Mode=preferEncrypted");
        encryption.Output.Should().Contain("SupportsEncryptionToggle=True");
        ports.Status.Should().Be("Passed");
        ports.Output.Should().Contain("51413");
        capabilities.Status.Should().Be("Passed");
        capabilities.Output.Should().Contain("SequentialDownload=False");
        capabilities.Output.Should().Contain("DynamicLimits=False");
    }

    [Test]
    public async Task RunTestAsync_WhenVpnServiceIsMissing_Fails()
    {
        var runner = new DeveloperTestRunner();

        var result = await runner.RunTestAsync("network-vpn-binding");

        result.Status.Should().Be("Failed");
        result.Output.Should().NotContain("Direct adapter policy active");
    }

    [Test]
    public async Task RunTestAsync_WhenKillSwitchIsEnabledAndInterfaceIsDown_Fails()
    {
        var vpn = Substitute.For<IVpnKillSwitchService>();
        vpn.IsKillSwitchEnabled.Returns(true);
        vpn.VpnInterfaceName.Returns("wg0");
        vpn.IsVpnInterfaceUp.Returns(false);
        vpn.IsFailClosedActive.Returns(true);
        var runner = new DeveloperTestRunner(vpnKillSwitch: vpn);

        var result = await runner.RunTestAsync("network-vpn-binding");

        result.Status.Should().Be("Failed");
        result.Output.Should().Contain("wg0");
        result.Output.Should().Contain("down");
    }

    [Test]
    public async Task RunTestAsync_WhenKillSwitchIsDisabled_ReportsDirectPolicyFromService()
    {
        var vpn = Substitute.For<IVpnKillSwitchService>();
        vpn.IsKillSwitchEnabled.Returns(false);
        vpn.VpnInterfaceName.Returns(string.Empty);
        vpn.IsVpnInterfaceUp.Returns(false);
        vpn.IsFailClosedActive.Returns(false);
        var runner = new DeveloperTestRunner(vpnKillSwitch: vpn);

        var result = await runner.RunTestAsync("network-vpn-binding");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("KillSwitch=False");
        result.Output.Should().Contain("Direct adapter policy");
    }

    [Test]
    public async Task RunTestAsync_WhenEventStoreIsMissing_Fails()
    {
        var runner = new DeveloperTestRunner();

        var wiretap = await runner.RunTestAsync("event-wiretap");
        var handlers = await runner.RunTestAsync("event-handlers");

        wiretap.Status.Should().Be("Failed");
        wiretap.Output.Should().NotContain("ring buffer operational");
        handlers.Status.Should().Be("Passed");
        handlers.Output.Should().Contain("IHandle");
    }

    [Test]
    public async Task RunTestAsync_WhenEventStoreIsRegistered_ReportsRecordedCount()
    {
        var store = Substitute.For<IDeveloperEventStore>();
        store.GetRecordedCount().Returns(4);
        var runner = new DeveloperTestRunner(developerEventStore: store);

        var result = await runner.RunTestAsync("event-wiretap");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("RecordedEvents=4");
    }

    [Test]
    public async Task RunTestAsync_SignalRHub_DoesNotUseCannedSuccessText()
    {
        var runner = new DeveloperTestRunner();

        var result = await runner.RunTestAsync("signalr-hub");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("MessageHub loaded");
        result.Output.Should().Contain("Connected=");
        result.Output.Should().NotContain("pipeline initialized");
    }

    [Test]
    public async Task RunTestAsync_UnrecognizedTest_StaysSkipped()
    {
        var runner = new DeveloperTestRunner();

        var result = await runner.RunTestAsync("not-a-real-test");

        result.Status.Should().Be("Skipped");
    }

    private static (IMainDatabase Database, IDbCommand Command) CreateSqliteDatabase()
    {
        var database = Substitute.For<IMainDatabase>();
        var connection = Substitute.For<IDbConnection>();
        var command = Substitute.For<IDbCommand>();
        database.DatabaseType.Returns(DatabaseType.SQLite);
        database.Version.Returns(new Version(3, 45));
        database.OpenConnection().Returns(connection);
        connection.CreateCommand().Returns(command);
        return (database, command);
    }
}

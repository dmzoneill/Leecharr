// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Backup;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Messaging;

[TestFixture]
public class CoreSystemCommandHandlerTest
{
    [Test]
    public async Task VacuumDatabaseCommand_RunsVacuumOnSqliteDatabase()
    {
        var database = Substitute.For<IMainDatabase>();
        database.DatabaseType.Returns(DatabaseType.SQLite);
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        await connection.ExecuteAsync("CREATE TABLE vacuum_test (id INTEGER PRIMARY KEY);");
        database.OpenConnection().Returns(connection);

        var handler = new CoreSystemCommandHandler(mainDatabase: database);
        Func<Task> act = async () => await handler.ExecuteAsync(new VacuumDatabaseCommand { AnalyzeAfterVacuum = false }, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task PurgeCompletedTorrentsCommand_DeletesExpiredCompletedTorrents()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { Id = 1, Status = TorrentStatus.Completed, DateCompleted = System.DateTime.UtcNow.AddDays(-40) },
            new() { Id = 2, Status = TorrentStatus.Downloading },
        });

        var handler = new CoreSystemCommandHandler(torrentService: torrentService);
        await handler.ExecuteAsync(new PurgeCompletedTorrentsCommand { RetentionDays = 30, RemoveData = true }, CancellationToken.None);

        await torrentService.Received(1).DeleteAsync(1, true);
        await torrentService.DidNotReceive().DeleteAsync(2, Arg.Any<bool>());
    }

    [Test]
    public async Task ExportDiagnosticsBundleCommand_CreatesBackup()
    {
        var backupService = Substitute.For<IBackupService>();
        backupService.CreateBackup("Diagnostics").Returns(new BackupResult { Path = "/tmp/diag.zip", Size = 42 });

        var handler = new CoreSystemCommandHandler(backupService: backupService);
        await handler.ExecuteAsync(new ExportDiagnosticsBundleCommand(), CancellationToken.None);

        backupService.Received(1).CreateBackup("Diagnostics");
    }

    [Test]
    public async Task TestNotificationEndpointsCommand_UsesEndpointTester()
    {
        var tester = Substitute.For<INotificationEndpointTester>();
        tester.TestEndpointsAsync(5, true, CancellationToken.None).Returns(2);

        var handler = new CoreSystemCommandHandler(notificationEndpointTester: tester);
        await handler.ExecuteAsync(new TestNotificationEndpointsCommand { NotificationId = 5, SendTestPayload = true }, CancellationToken.None);

        await tester.Received(1).TestEndpointsAsync(5, true, CancellationToken.None);
    }
}

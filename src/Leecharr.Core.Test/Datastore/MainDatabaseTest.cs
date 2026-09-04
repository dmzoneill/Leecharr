// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.Datastore;

[TestFixture]
public class MainDatabaseTest
{
    private string tempFolder = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempFolder = Path.Combine(Path.GetTempPath(), "MainDbRestoreTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(this.tempFolder))
            {
                Directory.Delete(this.tempFolder, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Test]
    public void ApplyPendingRestore_WhenRestoreFileAndStaleWalShmExist_PurgesWalShmAndAppliesRestore()
    {
        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(this.tempFolder);

        var connStringFactory = Substitute.For<IConnectionStringFactory>();
        connStringFactory.DatabaseType.Returns(DatabaseType.SQLite);
        connStringFactory.MainDbConnectionString.Returns($"Data Source={Path.Combine(this.tempFolder, "leecharr.db")}");

        var dbFactory = Substitute.For<IDbFactory>();
        var fakeDb = Substitute.For<IDatabase>();
        dbFactory.Create(DatabaseType.SQLite, Arg.Any<string>()).Returns(fakeDb);

        var dbPath = Path.Combine(this.tempFolder, "leecharr.db");
        var dbRestorePath = dbPath + ".restore";
        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";

        File.WriteAllText(dbPath, "old-db");
        File.WriteAllText(dbRestorePath, "restored-db-content");
        File.WriteAllText(walPath, "stale-wal");
        File.WriteAllText(shmPath, "stale-shm");

        var mainDb = new MainDatabase(dbFactory, connStringFactory, appFolderInfo);

        mainDb.Should().NotBeNull();
        File.Exists(dbRestorePath).Should().BeFalse();
        File.Exists(walPath).Should().BeFalse("Stale WAL file should be deleted on pending restore");
        File.Exists(shmPath).Should().BeFalse("Stale SHM file should be deleted on pending restore");
        File.ReadAllText(dbPath).Should().Be("restored-db-content");
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
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
        SqliteConnection.ClearAllPools();
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

    private static void CreateValidSqliteDatabase(string path, string markerTableName = "RestoredTable")
    {
        var connString = $"Data Source={path};";
        using (var conn = new SqliteConnection(connString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE TABLE {markerTableName} (Id INTEGER PRIMARY KEY, Val TEXT); INSERT INTO {markerTableName} VALUES (1, 'val1');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    [Test]
    public void ApplyPendingRestore_WhenValidRestoreFileAndStaleWalShmExist_PurgesWalShmAndAppliesRestore()
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

        CreateValidSqliteDatabase(dbPath, "OriginalTable");
        CreateValidSqliteDatabase(dbRestorePath, "RestoredTable");
        File.WriteAllText(walPath, "stale-wal");
        File.WriteAllText(shmPath, "stale-shm");

        var mainDb = new MainDatabase(dbFactory, connStringFactory, appFolderInfo);

        mainDb.Should().NotBeNull();
        File.Exists(dbRestorePath).Should().BeFalse("Restore file should be moved");
        File.Exists(walPath).Should().BeFalse("Stale WAL file should be deleted on successful restore");
        File.Exists(shmPath).Should().BeFalse("Stale SHM file should be deleted on successful restore");
        File.Exists(dbPath).Should().BeTrue();

        using var conn = new SqliteConnection($"Data Source={dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RestoredTable';";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        count.Should().Be(1, "The restored database tables should be present in leecharr.db");
    }

    [Test]
    public void ApplyPendingRestore_WhenRestoreFileIsCorruptOrInvalid_PreservesOriginalDatabaseAndWalAndRenamesRestoreToFailed()
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

        CreateValidSqliteDatabase(dbPath, "OriginalTable");
        File.WriteAllText(walPath, "active-wal-content");
        File.WriteAllText(shmPath, "active-shm-content");
        File.WriteAllText(dbRestorePath, "corrupt-non-sqlite-content-that-should-fail-header-validation");

        var mainDb = new MainDatabase(dbFactory, connStringFactory, appFolderInfo);

        mainDb.Should().NotBeNull();
        File.Exists(dbRestorePath).Should().BeFalse("Original .restore file should be renamed to .failed");
        File.Exists(dbRestorePath + ".failed").Should().BeTrue("Failed restore file should be preserved with .failed extension");
        File.ReadAllText(dbRestorePath + ".failed").Should().Be("corrupt-non-sqlite-content-that-should-fail-header-validation");

        File.Exists(walPath).Should().BeTrue("Active WAL file must be preserved on restore failure");
        File.ReadAllText(walPath).Should().Be("active-wal-content");
        File.Exists(shmPath).Should().BeTrue("Active SHM file must be preserved on restore failure");
        File.ReadAllText(shmPath).Should().Be("active-shm-content");

        using var conn = new SqliteConnection($"Data Source={dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='OriginalTable';";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        count.Should().Be(1, "Original database tables must remain untouched");
    }

    [Test]
    public void ApplyPendingRestore_WhenRestoreFileIsTruncated_PreservesOriginalDatabaseAndPreservesFailedFile()
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

        File.WriteAllText(dbPath, "original-db-bytes");
        File.WriteAllText(walPath, "original-wal-bytes");
        File.WriteAllBytes(dbRestorePath, new byte[] { 1, 2, 3, 4 }); // Less than 100 bytes

        var mainDb = new MainDatabase(dbFactory, connStringFactory, appFolderInfo);

        mainDb.Should().NotBeNull();
        File.Exists(dbRestorePath).Should().BeFalse();
        File.Exists(dbRestorePath + ".failed").Should().BeTrue();
        File.ReadAllText(dbPath).Should().Be("original-db-bytes");
        File.ReadAllText(walPath).Should().Be("original-wal-bytes");
    }

    [Test]
    public void ApplyPendingRestore_WhenNoRestoreFileExists_DoesNotModifyDatabaseOrWal()
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
        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";

        File.WriteAllText(dbPath, "existing-db");
        File.WriteAllText(walPath, "existing-wal");
        File.WriteAllText(shmPath, "existing-shm");

        var mainDb = new MainDatabase(dbFactory, connStringFactory, appFolderInfo);

        mainDb.Should().NotBeNull();
        File.ReadAllText(dbPath).Should().Be("existing-db");
        File.ReadAllText(walPath).Should().Be("existing-wal");
        File.ReadAllText(shmPath).Should().Be("existing-shm");
    }

    [Test]
    public void ApplyPendingRestore_WhenPostgreSQL_DoesNotAttemptRestore()
    {
        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(this.tempFolder);

        var connStringFactory = Substitute.For<IConnectionStringFactory>();
        connStringFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);
        connStringFactory.MainDbConnectionString.Returns("Host=localhost;Database=leecharr;");

        var dbFactory = Substitute.For<IDbFactory>();
        var fakeDb = Substitute.For<IDatabase>();
        dbFactory.Create(DatabaseType.PostgreSQL, Arg.Any<string>()).Returns(fakeDb);

        var dbPath = Path.Combine(this.tempFolder, "leecharr.db");
        var dbRestorePath = dbPath + ".restore";
        File.WriteAllText(dbRestorePath, "dummy-restore-file");

        var mainDb = new MainDatabase(dbFactory, connStringFactory, appFolderInfo);

        mainDb.Should().NotBeNull();
        File.Exists(dbRestorePath).Should().BeTrue("Restore file should be untouched when database is PostgreSQL");
    }

    [Test]
    public void ConnectionStringFactory_Sqlite_IncludesForeignKeysParameter()
    {
        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(this.tempFolder);

        var configFileProvider = Substitute.For<NzbDrone.Core.Configuration.IConfigFileProvider>();
        configFileProvider.PostgresHost.Returns(string.Empty);

        var factory = new ConnectionStringFactory(appFolderInfo, configFileProvider);

        factory.DatabaseType.Should().Be(DatabaseType.SQLite);
        factory.MainDbConnectionString.Should().Contain("Foreign Keys=True");
    }

    [Test]
    public void Database_OpenConnection_Sqlite_EnablesForeignKeysPragma()
    {
        var dbPath = Path.Combine(this.tempFolder, "pragma_test.db");
        var connectionString = $"Data Source={dbPath};";

        var db = new Database(() => new Microsoft.Data.Sqlite.SqliteConnection(connectionString), DatabaseType.SQLite);
        using var conn = db.OpenConnection();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys;";
        var result = Convert.ToInt32(cmd.ExecuteScalar());

        result.Should().Be(1, "Foreign keys PRAGMA must be enabled (1)");
    }
}

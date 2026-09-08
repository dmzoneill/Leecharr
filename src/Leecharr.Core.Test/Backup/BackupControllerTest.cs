// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Leecharr.Api.V1.Backup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.Backup;

[TestFixture]
public class BackupControllerTest
{
    private string testTempDir = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private BackupController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.testTempDir = Path.Combine(Path.GetTempPath(), "LeecharrBackupTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.testTempDir);

        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(this.testTempDir);

        this.controller = new BackupController(this.appFolderInfo);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(this.testTempDir))
            {
                Directory.Delete(this.testTempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private string CreateSampleBackup(string fileName, string dbContent = "sample-db-data", string configContent = "<config/>")
    {
        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);
        var zipPath = Path.Combine(backupDir, fileName);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            if (dbContent != null)
            {
                var entry = zip.CreateEntry("leecharr.db");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(dbContent);
            }

            if (configContent != null)
            {
                var entry = zip.CreateEntry("config.xml");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(configContent);
            }
        }

        return zipPath;
    }

    [Test]
    public void Download_WhenBackupNotFound_ReturnsNotFound()
    {
        var result = this.controller.Download(999);
        result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Download_WhenBackupExists_ReturnsFileStreamResult()
    {
        var fileName = "Leecharr_backup_20260904_120000.zip";
        this.CreateSampleBackup(fileName);

        var result = this.controller.Download(1);
        result.Should().BeOfType<FileStreamResult>();

        var fileResult = (FileStreamResult)result;
        fileResult.ContentType.Should().Be("application/zip");
        fileResult.FileDownloadName.Should().Be(fileName);
        fileResult.FileStream.Dispose();
    }

    [Test]
    public void Restore_WhenRequestIsNull_ReturnsBadRequest()
    {
        var result = this.controller.Restore(null!);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Restore_WhenBackupNotFound_ReturnsBadRequest()
    {
        var result = this.controller.Restore(new RestoreBackupRequest { BackupId = 999 });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Restore_WhenMatchedByBackupId_RestoresBackupFiles()
    {
        var fileName = "Leecharr_backup_20260904_120000.zip";
        this.CreateSampleBackup(fileName, dbContent: "restored-db-data", configContent: "<restored-config/>");

        var result = this.controller.Restore(new RestoreBackupRequest { BackupId = 1 });
        result.Should().BeOfType<OkObjectResult>();

        var restoredDb = Path.Combine(this.testTempDir, "leecharr.db");
        var restoredConfig = Path.Combine(this.testTempDir, "config.xml");

        File.Exists(restoredDb).Should().BeTrue();
        File.ReadAllText(restoredDb).Should().Be("restored-db-data");

        File.Exists(restoredConfig).Should().BeTrue();
        File.ReadAllText(restoredConfig).Should().Be("<restored-config/>");
    }

    [Test]
    public void Restore_WhenMatchedByFileName_RestoresBackupFiles()
    {
        var fileName = "Leecharr_backup_20260904_120000.zip";
        this.CreateSampleBackup(fileName, dbContent: "restored-by-filename", configContent: "<config-by-filename/>");

        var result = this.controller.Restore(new RestoreBackupRequest { FileName = fileName });
        result.Should().BeOfType<OkObjectResult>();

        var restoredDb = Path.Combine(this.testTempDir, "leecharr.db");
        File.ReadAllText(restoredDb).Should().Be("restored-by-filename");
    }

    [Test]
    public void Restore_WhenMatchedByPath_RestoresBackupFiles()
    {
        var fileName = "Leecharr_backup_20260904_120000.zip";
        var zipPath = this.CreateSampleBackup(fileName, dbContent: "restored-by-path", configContent: "<config-by-path/>");

        var result = this.controller.Restore(new RestoreBackupRequest { Path = zipPath });
        result.Should().BeOfType<OkObjectResult>();

        var restoredDb = Path.Combine(this.testTempDir, "leecharr.db");
        File.ReadAllText(restoredDb).Should().Be("restored-by-path");
    }

    [Test]
    public void Create_WhenSQLiteDatabaseInWalMode_CheckpointsWalAndIncludesWalInArchive()
    {
        var dbPath = Path.Combine(this.testTempDir, "leecharr.db");
        using var activeConn = new SqliteConnection($"Data Source={dbPath}");
        activeConn.Open();
        using var cmd = activeConn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL; CREATE TABLE test_table (id INTEGER PRIMARY KEY, value TEXT); INSERT INTO test_table (value) VALUES ('checkpoint-test');";
        cmd.ExecuteNonQuery();

        var walPath = Path.Combine(this.testTempDir, "leecharr.db-wal");
        File.Exists(walPath).Should().BeTrue("WAL file should exist while connection is active in WAL mode");

        var result = this.controller.Create();
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        var backup = (BackupResource)okResult.Value!;
        backup.Should().NotBeNull();
        File.Exists(backup.Path).Should().BeTrue();

        using (var zip = ZipFile.OpenRead(backup.Path))
        {
            zip.Entries.Should().Contain(e => e.FullName == "leecharr.db");
            zip.Entries.Should().Contain(e => e.FullName == "leecharr.db-wal");

            // Extract leecharr.db in isolation to verify WAL checkpoint flushed data into the main database file
            var extractDir = Path.Combine(this.testTempDir, "isolated-check");
            Directory.CreateDirectory(extractDir);
            var extractedDb = Path.Combine(extractDir, "leecharr.db");
            zip.GetEntry("leecharr.db")!.ExtractToFile(extractedDb);

            using var isolatedConn = new SqliteConnection($"Data Source={extractedDb}");
            isolatedConn.Open();
            using var readCmd = isolatedConn.CreateCommand();
            readCmd.CommandText = "SELECT value FROM test_table WHERE id = 1;";
            var readValue = readCmd.ExecuteScalar()?.ToString();
            readValue.Should().Be("checkpoint-test");
            SqliteConnection.ClearAllPools();
        }

        activeConn.Close();
        SqliteConnection.ClearAllPools();
    }

    [Test]
    public void Create_WhenNoDatabaseExists_CreatesBackupWithConfig()
    {
        var configPath = Path.Combine(this.testTempDir, "config.xml");
        File.WriteAllText(configPath, "<config><port>8989</port></config>");

        var result = this.controller.Create();
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        var backup = (BackupResource)okResult.Value!;
        backup.Should().NotBeNull();

        using var zip = ZipFile.OpenRead(backup.Path);
        zip.Entries.Should().Contain(e => e.FullName == "config.xml");
        zip.Entries.Should().NotContain(e => e.FullName == "leecharr.db");
    }

    [Test]
    public void Restore_WhenStaleWalAndShmExist_DeletesStaleWalAndShmFiles()
    {
        var walPath = Path.Combine(this.testTempDir, "leecharr.db-wal");
        var shmPath = Path.Combine(this.testTempDir, "leecharr.db-shm");
        File.WriteAllText(walPath, "stale-wal-data");
        File.WriteAllText(shmPath, "stale-shm-data");

        var fileName = "Leecharr_backup_20260904_120000.zip";
        this.CreateSampleBackup(fileName, dbContent: "fresh-db-data", configContent: "<config/>");

        var result = this.controller.Restore(new RestoreBackupRequest { BackupId = 1 });
        result.Should().BeOfType<OkObjectResult>();

        File.Exists(walPath).Should().BeFalse("Stale WAL file must be deleted before extracting restored DB");
        File.Exists(shmPath).Should().BeFalse("Stale SHM file must be deleted before extracting restored DB");

        var restoredDb = Path.Combine(this.testTempDir, "leecharr.db");
        File.ReadAllText(restoredDb).Should().Be("fresh-db-data");
    }

    [Test]
    public void Restore_WhenStaleWalFromPriorDatabasePresent_RestoresCleanlyWithoutDiskImageMalformed()
    {
        // 1. Create original database state and archive it
        var dbPath = Path.Combine(this.testTempDir, "leecharr.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE records (id INTEGER PRIMARY KEY, note TEXT); INSERT INTO records (note) VALUES ('original-note');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var createResult = this.controller.Create();
        createResult.Result.Should().BeOfType<OkObjectResult>();

        // 2. Simulate subsequent database activity leaving stale WAL and SHM files with invalid salt
        var walPath = Path.Combine(this.testTempDir, "leecharr.db-wal");
        var shmPath = Path.Combine(this.testTempDir, "leecharr.db-shm");
        File.WriteAllBytes(walPath, new byte[] { 0x37, 0x7f, 0x06, 0x82, 0x01, 0x02, 0x03, 0x04 });
        File.WriteAllBytes(shmPath, new byte[] { 0x01, 0x02, 0x03, 0x04 });
        File.Exists(walPath).Should().BeTrue();
        File.Exists(shmPath).Should().BeTrue();

        // 3. Trigger restore of the initial backup
        var restoreResult = this.controller.Restore(new RestoreBackupRequest { BackupId = 1 });
        restoreResult.Should().BeOfType<OkObjectResult>();

        // 4. Verify stale WAL and SHM were deleted
        File.Exists(walPath).Should().BeFalse("Stale WAL should be purged on restore");
        File.Exists(shmPath).Should().BeFalse("Stale SHM should be purged on restore");

        // 5. Verify restored database opens cleanly and passes integrity check
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var check = cmd.ExecuteScalar()?.ToString();
            check.Should().Be("ok");

            cmd.CommandText = "SELECT note FROM records;";
            var note = cmd.ExecuteScalar()?.ToString();
            note.Should().Be("original-note");
        }

        SqliteConnection.ClearAllPools();
    }

    [Test]
    public void Restore_WhenBackupIncludesWal_ExtractsWalAndIntegrityCheckPasses()
    {
        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);
        var zipPath = Path.Combine(backupDir, "Leecharr_backup_with_wal.zip");

        var tempDb = Path.Combine(this.testTempDir, "temp_source.db");
        using (var conn = new SqliteConnection($"Data Source={tempDb}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode = WAL; CREATE TABLE wal_table (id INTEGER PRIMARY KEY, content TEXT); INSERT INTO wal_table (content) VALUES ('wal-data');";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        var tempWal = Path.Combine(this.testTempDir, "temp_source.db-wal");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(tempDb, "leecharr.db");
            if (File.Exists(tempWal))
            {
                zip.CreateEntryFromFile(tempWal, "leecharr.db-wal");
            }
        }

        // Place stale WAL/SHM in target directory
        File.WriteAllText(Path.Combine(this.testTempDir, "leecharr.db-wal"), "stale-wal");
        File.WriteAllText(Path.Combine(this.testTempDir, "leecharr.db-shm"), "stale-shm");

        var result = this.controller.Restore(new RestoreBackupRequest { Path = zipPath });
        result.Should().BeOfType<OkObjectResult>();

        var restoredDb = Path.Combine(this.testTempDir, "leecharr.db");
        using (var conn = new SqliteConnection($"Data Source={restoredDb}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var check = cmd.ExecuteScalar()?.ToString();
            check.Should().Be("ok");

            cmd.CommandText = "SELECT content FROM wal_table WHERE id = 1;";
            var val = cmd.ExecuteScalar()?.ToString();
            val.Should().Be("wal-data");
        }

        SqliteConnection.ClearAllPools();
    }

    private class TestableBackupController : BackupController
    {
        public bool SimulatePgDumpSuccess { get; set; } = true;

        public bool SimulatePsqlRestoreSuccess { get; set; } = true;

        public bool SimulatePgDumpExecutableFound { get; set; } = true;

        public bool SimulatePsqlExecutableFound { get; set; } = true;

        public TestableBackupController(
            IAppFolderInfo appFolderInfo,
            IDiskProvider diskProvider = null,
            IConnectionStringFactory connectionStringFactory = null,
            IConfigFileProvider configFileProvider = null,
            IConfigService configService = null)
            : base(appFolderInfo, diskProvider, connectionStringFactory, configFileProvider, configService)
        {
        }

        protected override string FindPgDumpExecutable() => this.SimulatePgDumpExecutableFound ? "pg_dump" : null!;

        protected override string FindPsqlExecutable() => this.SimulatePsqlExecutableFound ? "psql" : null!;

        protected override bool RunPgDump(string pgDumpExe, string host, int port, string user, string password, string dbName, string outputPath)
        {
            if (!this.SimulatePgDumpSuccess)
            {
                return false;
            }

            global::System.IO.File.WriteAllText(outputPath, "-- PostgreSQL test database dump");
            return true;
        }

        protected override bool RunPsqlRestore(string psqlExe, string host, int port, string user, string password, string dbName, string sqlScriptPath)
        {
            return this.SimulatePsqlRestoreSuccess;
        }
    }

    [Test]
    public void Create_WhenPostgreSqlConfiguredAndPgDumpSucceeds_IncludesPostgresDumpAndIgnoresStaleSqliteFiles()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.PostgresHost.Returns("localhost");
        configProvider.PostgresPort.Returns(5432);
        configProvider.PostgresMainDb.Returns("leecharr_test");
        configProvider.PostgresUser.Returns("postgres");

        var connFactory = Substitute.For<IConnectionStringFactory>();
        connFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);

        var pgController = new TestableBackupController(this.appFolderInfo, connectionStringFactory: connFactory, configFileProvider: configProvider);

        // Create stale SQLite files
        var staleDb = Path.Combine(this.testTempDir, "leecharr.db");
        var staleWal = Path.Combine(this.testTempDir, "leecharr.db-wal");
        File.WriteAllText(staleDb, "stale-sqlite-data");
        File.WriteAllText(staleWal, "stale-sqlite-wal");

        var configPath = Path.Combine(this.testTempDir, "config.xml");
        File.WriteAllText(configPath, "<config><PostgresHost>localhost</PostgresHost></config>");

        var result = pgController.Create();
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        var backup = (BackupResource)okResult.Value!;
        backup.Should().NotBeNull();
        backup.DatabaseType.Should().Be("PostgreSQL");
        backup.IncludesDatabase.Should().BeTrue();

        using var zip = ZipFile.OpenRead(backup.Path);
        zip.Entries.Should().Contain(e => e.FullName == "config.xml");
        zip.Entries.Should().Contain(e => e.FullName == "leecharr_postgres.sql", "PostgreSQL backup must include postgres dump file");
        zip.Entries.Should().NotContain(e => e.FullName == "leecharr.db", "PostgreSQL backup must not include stale SQLite database");
        zip.Entries.Should().NotContain(e => e.FullName == "leecharr.db-wal", "PostgreSQL backup must not include stale SQLite WAL");
    }

    [Test]
    public void Create_WhenPostgreSqlConfiguredAndPgDumpExecutableNotFound_FailsLoudlyWith500()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.PostgresHost.Returns("localhost");
        configProvider.PostgresPort.Returns(5432);
        configProvider.PostgresMainDb.Returns("leecharr_test");
        configProvider.PostgresUser.Returns("postgres");

        var connFactory = Substitute.For<IConnectionStringFactory>();
        connFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);

        var pgController = new TestableBackupController(this.appFolderInfo, connectionStringFactory: connFactory, configFileProvider: configProvider)
        {
            SimulatePgDumpExecutableFound = false,
        };

        var result = pgController.Create();
        result.Result.Should().BeOfType<ObjectResult>();

        var objResult = (ObjectResult)result.Result!;
        objResult.StatusCode.Should().Be(500);
    }

    [Test]
    public void Create_WhenPostgreSqlConfiguredAndPgDumpFails_FailsLoudlyWith500()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.PostgresHost.Returns("localhost");
        configProvider.PostgresPort.Returns(5432);
        configProvider.PostgresMainDb.Returns("leecharr_test");
        configProvider.PostgresUser.Returns("postgres");

        var connFactory = Substitute.For<IConnectionStringFactory>();
        connFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);

        var pgController = new TestableBackupController(this.appFolderInfo, connectionStringFactory: connFactory, configFileProvider: configProvider)
        {
            SimulatePgDumpSuccess = false,
        };

        var result = pgController.Create();
        result.Result.Should().BeOfType<ObjectResult>();

        var objResult = (ObjectResult)result.Result!;
        objResult.StatusCode.Should().Be(500);
    }

    [Test]
    public void Restore_WhenPostgreSqlConfiguredAndPsqlRestoreFails_FailsLoudlyWith500()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.PostgresHost.Returns("localhost");
        configProvider.PostgresPort.Returns(5432);
        configProvider.PostgresMainDb.Returns("leecharr_test");
        configProvider.PostgresUser.Returns("postgres");

        var connFactory = Substitute.For<IConnectionStringFactory>();
        connFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);

        var pgController = new TestableBackupController(this.appFolderInfo, connectionStringFactory: connFactory, configFileProvider: configProvider)
        {
            SimulatePsqlRestoreSuccess = false,
        };

        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);
        var zipPath = Path.Combine(backupDir, "Leecharr_backup_pg_restore.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("config.xml");
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write("<pg-config/>");
            }

            var sqlEntry = zip.CreateEntry("leecharr_postgres.sql");
            using (var writer = new StreamWriter(sqlEntry.Open()))
            {
                writer.Write("-- sql dump");
            }
        }

        var result = pgController.Restore(new RestoreBackupRequest { Path = zipPath });
        result.Should().BeOfType<ObjectResult>();

        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(500);
    }

    [Test]
    public void Restore_WhenPostgreSqlConfiguredAndPsqlRestoreSucceeds_RestoresSuccessfully()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.PostgresHost.Returns("localhost");
        configProvider.PostgresPort.Returns(5432);
        configProvider.PostgresMainDb.Returns("leecharr_test");
        configProvider.PostgresUser.Returns("postgres");

        var connFactory = Substitute.For<IConnectionStringFactory>();
        connFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);

        var pgController = new TestableBackupController(this.appFolderInfo, connectionStringFactory: connFactory, configFileProvider: configProvider)
        {
            SimulatePsqlRestoreSuccess = true,
        };

        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);
        var zipPath = Path.Combine(backupDir, "Leecharr_backup_pg_restore_ok.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("config.xml");
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write("<pg-config/>");
            }

            var sqlEntry = zip.CreateEntry("leecharr_postgres.sql");
            using (var writer = new StreamWriter(sqlEntry.Open()))
            {
                writer.Write("-- sql dump");
            }
        }

        var result = pgController.Restore(new RestoreBackupRequest { Path = zipPath });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public void Restore_WhenPostgreSqlConfigured_ExtractsConfigAndDoesNotCorruptOrCheckSqlite()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.PostgresHost.Returns("localhost");
        configProvider.PostgresPort.Returns(5432);
        configProvider.PostgresMainDb.Returns("leecharr_test");
        configProvider.PostgresUser.Returns("postgres");

        var connFactory = Substitute.For<IConnectionStringFactory>();
        connFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);

        var pgController = new BackupController(this.appFolderInfo, connectionStringFactory: connFactory, configFileProvider: configProvider);

        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);
        var zipPath = Path.Combine(backupDir, "Leecharr_backup_pg.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("config.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<pg-config/>");
        }

        var result = pgController.Restore(new RestoreBackupRequest { Path = zipPath });
        result.Should().BeOfType<OkObjectResult>();

        var restoredConfig = Path.Combine(this.testTempDir, "config.xml");
        File.Exists(restoredConfig).Should().BeTrue();
        File.ReadAllText(restoredConfig).Should().Be("<pg-config/>");
    }

    private class TestableDirectProcessBackupController : BackupController
    {
        public TestableDirectProcessBackupController(
            IAppFolderInfo appFolderInfo,
            IDiskProvider diskProvider = null,
            IConnectionStringFactory connectionStringFactory = null,
            IConfigFileProvider configFileProvider = null,
            IConfigService configService = null)
            : base(appFolderInfo, diskProvider, connectionStringFactory, configFileProvider, configService)
        {
        }

        public bool TestRunPgDump(string pgDumpExe, string host, int port, string user, string password, string dbName, string outputPath)
            => this.RunPgDump(pgDumpExe, host, port, user, password, dbName, outputPath);

        public bool TestRunPsqlRestore(string psqlExe, string host, int port, string user, string password, string dbName, string sqlScriptPath)
            => this.RunPsqlRestore(psqlExe, host, port, user, password, dbName, sqlScriptPath);
    }

    private string CreateExecutableScript(string content)
    {
        var ext = OperatingSystem.IsWindows() ? ".cmd" : ".sh";
        var scriptPath = Path.Combine(this.testTempDir, "test_proc_" + Guid.NewGuid().ToString("N") + ext);
        File.WriteAllText(scriptPath, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return scriptPath;
    }

    [Test]
    public void RunPgDump_WhenExecutableDoesNotExist_ReturnsFalse()
    {
        var controller = new TestableDirectProcessBackupController(this.appFolderInfo);
        var result = controller.TestRunPgDump("non_existent_binary_for_pg_dump", "localhost", 5432, "postgres", "pass", "db", Path.Combine(this.testTempDir, "out.sql"));
        result.Should().BeFalse();
    }

    [Test]
    public void RunPsqlRestore_WhenExecutableDoesNotExist_ReturnsFalse()
    {
        var controller = new TestableDirectProcessBackupController(this.appFolderInfo);
        var result = controller.TestRunPsqlRestore("non_existent_binary_for_psql", "localhost", 5432, "postgres", "pass", "db", Path.Combine(this.testTempDir, "in.sql"));
        result.Should().BeFalse();
    }

    [Test]
    public void RunPgDump_WhenProcessTimesOut_KillsProcessAndReturnsFalse()
    {
        var configService = Substitute.For<IConfigService>();
        configService.DatabaseBackupTimeoutSeconds.Returns(1);

        var controller = new TestableDirectProcessBackupController(this.appFolderInfo, configService: configService);
        var script = this.CreateExecutableScript(OperatingSystem.IsWindows()
            ? "@echo off\r\nping 127.0.0.1 -n 10 >nul"
            : "#!/bin/sh\nsleep 10\n");

        var outputPath = Path.Combine(this.testTempDir, "timeout_dump.sql");
        var result = controller.TestRunPgDump(script, "localhost", 5432, "postgres", null, "db", outputPath);

        result.Should().BeFalse();
    }

    [Test]
    public void RunPsqlRestore_WhenProcessTimesOut_KillsProcessAndReturnsFalse()
    {
        var configService = Substitute.For<IConfigService>();
        configService.DatabaseRestoreTimeoutSeconds.Returns(1);

        var controller = new TestableDirectProcessBackupController(this.appFolderInfo, configService: configService);
        var script = this.CreateExecutableScript(OperatingSystem.IsWindows()
            ? "@echo off\r\nping 127.0.0.1 -n 10 >nul"
            : "#!/bin/sh\nsleep 10\n");

        var sqlPath = Path.Combine(this.testTempDir, "restore.sql");
        File.WriteAllText(sqlPath, "-- sql script");
        var result = controller.TestRunPsqlRestore(script, "localhost", 5432, "postgres", null, "db", sqlPath);

        result.Should().BeFalse();
    }

    [Test]
    public void RunPgDump_WhenProcessFailsWithNonZeroExitCode_ReturnsFalse()
    {
        var controller = new TestableDirectProcessBackupController(this.appFolderInfo);
        var script = this.CreateExecutableScript(OperatingSystem.IsWindows()
            ? "@echo off\r\necho pg_dump error 1>&2\r\nexit /b 1"
            : "#!/bin/sh\necho 'pg_dump error' >&2\nexit 1\n");

        var outputPath = Path.Combine(this.testTempDir, "failed_dump.sql");
        var result = controller.TestRunPgDump(script, "localhost", 5432, "postgres", "secret", "db", outputPath);

        result.Should().BeFalse();
    }

    [Test]
    public void RunPsqlRestore_WhenProcessFailsWithNonZeroExitCode_ReturnsFalse()
    {
        var controller = new TestableDirectProcessBackupController(this.appFolderInfo);
        var script = this.CreateExecutableScript(OperatingSystem.IsWindows()
            ? "@echo off\r\necho psql restore error 1>&2\r\nexit /b 2"
            : "#!/bin/sh\necho 'psql restore error' >&2\nexit 2\n");

        var sqlPath = Path.Combine(this.testTempDir, "restore_fail.sql");
        File.WriteAllText(sqlPath, "-- sql script");
        var result = controller.TestRunPsqlRestore(script, "localhost", 5432, "postgres", "secret", "db", sqlPath);

        result.Should().BeFalse();
    }

    [Test]
    public void RunPgDump_WhenProcessSucceedsAndProducesOutput_ReturnsTrue()
    {
        var controller = new TestableDirectProcessBackupController(this.appFolderInfo);
        var outputPath = Path.Combine(this.testTempDir, "success_dump.sql");

        var script = this.CreateExecutableScript(OperatingSystem.IsWindows()
            ? $"@echo off\r\necho pg_dump success > \"{outputPath}\"\r\nexit /b 0"
            : $"#!/bin/sh\necho 'pg_dump success' > \"{outputPath}\"\nexit 0\n");

        var result = controller.TestRunPgDump(script, "localhost", 5432, "postgres", "secret", "db", outputPath);

        result.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
        File.ReadAllText(outputPath).Trim().Should().Be("pg_dump success");
    }

    [Test]
    public void RunPsqlRestore_WhenProcessSucceeds_ReturnsTrue()
    {
        var controller = new TestableDirectProcessBackupController(this.appFolderInfo);
        var sqlPath = Path.Combine(this.testTempDir, "restore_success.sql");
        File.WriteAllText(sqlPath, "-- sql script");

        var script = this.CreateExecutableScript(OperatingSystem.IsWindows()
            ? "@echo off\r\necho restore success\r\nexit /b 0"
            : "#!/bin/sh\necho 'restore success'\nexit 0\n");

        var result = controller.TestRunPsqlRestore(script, "localhost", 5432, "postgres", "secret", "db", sqlPath);

        result.Should().BeTrue();
    }

    [Test]
    public void PruneOldBackups_WhenBackupsExceedMaxCount_PrunesOldestArchives()
    {
        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);

        for (var i = 1; i <= 5; i++)
        {
            var path = this.CreateSampleBackup($"Leecharr_backup_count_{i}.zip");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10 + i));
        }

        var deleted = this.controller.PruneOldBackups(backupDir, maxBackupsToKeep: 3, maxAgeDays: 365);
        deleted.Should().Be(2);

        var remaining = Directory.GetFiles(backupDir, "*.zip");
        remaining.Length.Should().Be(3);
        remaining.Select(Path.GetFileName).Should().Contain("Leecharr_backup_count_5.zip");
        remaining.Select(Path.GetFileName).Should().Contain("Leecharr_backup_count_4.zip");
        remaining.Select(Path.GetFileName).Should().Contain("Leecharr_backup_count_3.zip");
        remaining.Select(Path.GetFileName).Should().NotContain("Leecharr_backup_count_1.zip");
        remaining.Select(Path.GetFileName).Should().NotContain("Leecharr_backup_count_2.zip");
    }

    [Test]
    public void PruneOldBackups_WhenBackupsOlderThanMaxAge_PrunesAgedArchives()
    {
        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);

        var pathToday = this.CreateSampleBackup("Leecharr_backup_today.zip");
        File.SetLastWriteTimeUtc(pathToday, DateTime.UtcNow);

        var path10Days = this.CreateSampleBackup("Leecharr_backup_10days.zip");
        File.SetLastWriteTimeUtc(path10Days, DateTime.UtcNow.AddDays(-10));

        var path35Days = this.CreateSampleBackup("Leecharr_backup_35days.zip");
        File.SetLastWriteTimeUtc(path35Days, DateTime.UtcNow.AddDays(-35));

        var path60Days = this.CreateSampleBackup("Leecharr_backup_60days.zip");
        File.SetLastWriteTimeUtc(path60Days, DateTime.UtcNow.AddDays(-60));

        var deleted = this.controller.PruneOldBackups(backupDir, maxBackupsToKeep: 10, maxAgeDays: 28);
        deleted.Should().Be(2);

        var remaining = Directory.GetFiles(backupDir, "*.zip");
        remaining.Length.Should().Be(2);
        remaining.Select(Path.GetFileName).Should().Contain("Leecharr_backup_today.zip");
        remaining.Select(Path.GetFileName).Should().Contain("Leecharr_backup_10days.zip");
        remaining.Select(Path.GetFileName).Should().NotContain("Leecharr_backup_35days.zip");
        remaining.Select(Path.GetFileName).Should().NotContain("Leecharr_backup_60days.zip");
    }

    [Test]
    public void PruneOldBackups_WhenAllBackupsOlderThanMaxAge_PreservesNewestBackup()
    {
        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);

        var path30Days = this.CreateSampleBackup("Leecharr_backup_30days.zip");
        File.SetLastWriteTimeUtc(path30Days, DateTime.UtcNow.AddDays(-30));

        var path40Days = this.CreateSampleBackup("Leecharr_backup_40days.zip");
        File.SetLastWriteTimeUtc(path40Days, DateTime.UtcNow.AddDays(-40));

        var path50Days = this.CreateSampleBackup("Leecharr_backup_50days.zip");
        File.SetLastWriteTimeUtc(path50Days, DateTime.UtcNow.AddDays(-50));

        var deleted = this.controller.PruneOldBackups(backupDir, maxBackupsToKeep: 10, maxAgeDays: 28);
        deleted.Should().Be(2);

        var remaining = Directory.GetFiles(backupDir, "*.zip");
        remaining.Length.Should().Be(1);
        remaining.Select(Path.GetFileName).Should().Contain("Leecharr_backup_30days.zip");
    }

    [Test]
    public void PruneOldBackups_WhenDirectoryHasSubdirectories_PrunesBothManualAndScheduledDirectories()
    {
        var manualDir = Path.Combine(this.testTempDir, "Backups", "manual");
        var scheduledDir = Path.Combine(this.testTempDir, "Backups", "scheduled");
        Directory.CreateDirectory(manualDir);
        Directory.CreateDirectory(scheduledDir);

        for (var i = 1; i <= 4; i++)
        {
            var mPath = Path.Combine(manualDir, $"Leecharr_manual_{i}.zip");
            using (var zip = ZipFile.Open(mPath, ZipArchiveMode.Create))
            {
                zip.CreateEntry("config.xml");
            }

            File.SetLastWriteTimeUtc(mPath, DateTime.UtcNow.AddDays(-10 + i));

            var sPath = Path.Combine(scheduledDir, $"Leecharr_sched_{i}.zip");
            using (var zip = ZipFile.Open(sPath, ZipArchiveMode.Create))
            {
                zip.CreateEntry("config.xml");
            }

            File.SetLastWriteTimeUtc(sPath, DateTime.UtcNow.AddDays(-10 + i));
        }

        var backupsRoot = Path.Combine(this.testTempDir, "Backups");
        var deleted = this.controller.PruneOldBackups(backupsRoot, maxBackupsToKeep: 2, maxAgeDays: 365);
        deleted.Should().Be(4);

        Directory.GetFiles(manualDir, "*.zip").Length.Should().Be(2);
        Directory.GetFiles(scheduledDir, "*.zip").Length.Should().Be(2);
    }

    [Test]
    public void Create_WhenExistingBackupsExceedRetention_AutomaticallyPrunesOlderBackups()
    {
        var configService = Substitute.For<IConfigService>();
        configService.BackupRetentionMaxCount.Returns(3);
        configService.BackupRetentionDays.Returns(365);

        var configProvider = Substitute.For<IConfigFileProvider>();
        var testController = new BackupController(
            this.appFolderInfo,
            configService: configService,
            configFileProvider: configProvider);

        var manualDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(manualDir);

        for (var i = 1; i <= 4; i++)
        {
            var p = Path.Combine(manualDir, $"Leecharr_backup_old_{i}.zip");
            using (var zip = ZipFile.Open(p, ZipArchiveMode.Create))
            {
                zip.CreateEntry("config.xml");
            }

            File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-10 + i));
        }

        var configPath = Path.Combine(this.testTempDir, "config.xml");
        File.WriteAllText(configPath, "<config/>");

        var createResult = testController.Create();
        createResult.Result.Should().BeOfType<OkObjectResult>();

        var remaining = Directory.GetFiles(manualDir, "*.zip");
        remaining.Length.Should().Be(3);
    }

    [Test]
    public void PruneOldBackups_WhenDirectoryDoesNotExistOrIsEmpty_ReturnsZeroWithoutError()
    {
        var nonExistent = Path.Combine(this.testTempDir, "non_existent_backups_dir");
        var empty = Path.Combine(this.testTempDir, "empty_backups_dir");
        Directory.CreateDirectory(empty);

        this.controller.PruneOldBackups(nonExistent).Should().Be(0);
        this.controller.PruneOldBackups(empty).Should().Be(0);
        this.controller.PruneOldBackups(null!).Should().Be(0);
        this.controller.PruneOldBackups(string.Empty).Should().Be(0);
    }
}

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
using NzbDrone.Common.EnvironmentInfo;

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
}

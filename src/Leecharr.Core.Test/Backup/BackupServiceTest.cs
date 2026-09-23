// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

namespace Leecharr.Core.Test.Backup;

[TestFixture]
public class BackupServiceTest
{
    private string testTempDir = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private IDiskProvider diskProvider = null!;
    private IConnectionStringFactory connectionStringFactory = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IConfigService configService = null!;
    private BackupService backupService = null!;

    [SetUp]
    public void SetUp()
    {
        this.testTempDir = Path.Combine(Path.GetTempPath(), "Leecharr_BackupServiceTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.testTempDir);

        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(this.testTempDir);

        this.diskProvider = Substitute.For<IDiskProvider>();
        this.connectionStringFactory = Substitute.For<IConnectionStringFactory>();
        this.connectionStringFactory.DatabaseType.Returns(DatabaseType.SQLite);

        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configFileProvider.PostgresHost.Returns((string)null!);

        this.configService = Substitute.For<IConfigService>();
        this.configService.BackupRetentionMaxCount.Returns(14);
        this.configService.BackupRetentionDays.Returns(28);

        this.backupService = new BackupService(
            this.appFolderInfo,
            this.diskProvider,
            this.connectionStringFactory,
            this.configFileProvider,
            this.configService);
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
            // Ignore cleanup failures
        }
    }

    private void CreateRealSqliteDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE TestTable (Id INTEGER PRIMARY KEY, Val TEXT); INSERT INTO TestTable VALUES (1, 'Hello');";
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    [Test]
    public void CreateBackup_Manual_CreatesValidZipWithSqliteAndConfig()
    {
        var dbPath = Path.Combine(this.testTempDir, "leecharr.db");
        this.CreateRealSqliteDatabase(dbPath);

        var configPath = Path.Combine(this.testTempDir, "config.xml");
        File.WriteAllText(configPath, "<Config><ApiKey>test-key</ApiKey></Config>");

        var result = this.backupService.CreateBackup("Manual");

        result.Should().NotBeNull();
        result.Type.Should().Be("Manual");
        result.DatabaseType.Should().Be("SQLite");
        result.IncludesDatabase.Should().BeTrue();
        result.Path.Should().NotBeNullOrWhiteSpace();
        result.Path.Should().Contain("manual");
        File.Exists(result.Path).Should().BeTrue();

        using var zip = ZipFile.OpenRead(result.Path);
        zip.Entries.Should().Contain(e => e.Name == "leecharr.db");
        zip.Entries.Should().Contain(e => e.Name == "config.xml");
    }

    [Test]
    public void CreateBackup_Scheduled_PlacesZipInScheduledDirectory()
    {
        var configPath = Path.Combine(this.testTempDir, "config.xml");
        File.WriteAllText(configPath, "<Config/>");

        var result = this.backupService.CreateBackup("Scheduled");

        result.Should().NotBeNull();
        result.Type.Should().Be("Scheduled");
        result.Path.Should().Contain("scheduled");
        File.Exists(result.Path).Should().BeTrue();

        using var zip = ZipFile.OpenRead(result.Path);
        zip.Entries.Should().Contain(e => e.Name == "config.xml");
    }

    [Test]
    public void CreateBackup_WhenDbNotValidSqlite_FallsBackToDirectFileCopyIncludingWal()
    {
        var dbPath = Path.Combine(this.testTempDir, "leecharr.db");
        File.WriteAllText(dbPath, "not-a-valid-sqlite-header-plain-text");

        var walPath = Path.Combine(this.testTempDir, "leecharr.db-wal");
        File.WriteAllText(walPath, "wal-contents");

        var result = this.backupService.CreateBackup("Manual");

        result.Should().NotBeNull();
        result.IncludesDatabase.Should().BeTrue();

        using var zip = ZipFile.OpenRead(result.Path);
        zip.Entries.Should().Contain(e => e.Name == "leecharr.db");
        zip.Entries.Should().Contain(e => e.Name == "leecharr.db-wal");
    }

    [Test]
    public void CreateBackup_WhenDatabaseDoesNotExist_CreatesZipWithoutDatabase()
    {
        var configPath = Path.Combine(this.testTempDir, "config.xml");
        File.WriteAllText(configPath, "<Config/>");

        var result = this.backupService.CreateBackup("Manual");

        result.Should().NotBeNull();
        result.IncludesDatabase.Should().BeFalse();

        using var zip = ZipFile.OpenRead(result.Path);
        zip.Entries.Should().NotContain(e => e.Name == "leecharr.db");
        zip.Entries.Should().Contain(e => e.Name == "config.xml");
    }

    [Test]
    public void CreateBackup_WhenPostgresConfiguredAndHostMissing_ThrowsInvalidOperationException()
    {
        this.connectionStringFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);
        this.configFileProvider.PostgresHost.Returns(string.Empty);
        this.configFileProvider.PostgresMainDb.Returns(string.Empty);

        var act = () => this.backupService.CreateBackup("Manual");

        act.Should().Throw<Exception>();
    }

    [Test]
    public void Execute_CallsCreateBackupWithScheduledType()
    {
        var configPath = Path.Combine(this.testTempDir, "config.xml");
        File.WriteAllText(configPath, "<Config/>");

        var command = new BackupCommand { Type = "Scheduled" };
        this.backupService.Execute(command);

        var scheduledDir = Path.Combine(this.testTempDir, "Backups", "scheduled");
        Directory.Exists(scheduledDir).Should().BeTrue();
        Directory.GetFiles(scheduledDir, "*.zip").Should().HaveCount(1);
    }

    [Test]
    public async Task ExecuteAsync_ExecutesBackupCommandAsynchronously()
    {
        var configPath = Path.Combine(this.testTempDir, "config.xml");
        File.WriteAllText(configPath, "<Config/>");

        var command = new BackupCommand { Type = "Manual" };
        await this.backupService.ExecuteAsync(command, CancellationToken.None);

        var manualDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.Exists(manualDir).Should().BeTrue();
        Directory.GetFiles(manualDir, "*.zip").Should().HaveCount(1);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("/path/does/not/exist/999")]
    public void PruneOldBackups_WhenDirectoryMissingOrEmpty_ReturnsZero(string path)
    {
        var pruned = this.backupService.PruneOldBackups(path);

        pruned.Should().Be(0);
    }

    [Test]
    public void PruneOldBackups_NeverDeletesTheSingleNewestBackup()
    {
        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);

        var oldZipPath = Path.Combine(backupDir, "Leecharr_backup_20200101_000000.zip");
        File.WriteAllText(oldZipPath, "dummy-zip");
        File.SetLastWriteTimeUtc(oldZipPath, DateTime.UtcNow.AddDays(-100));

        var pruned = this.backupService.PruneOldBackups(backupDir, maxBackupsToKeep: 1, maxAgeDays: 5);

        pruned.Should().Be(0);
        File.Exists(oldZipPath).Should().BeTrue("single newest backup must never be deleted");
    }

    [Test]
    public void PruneOldBackups_DeletesBackupsExceedingMaxBackupsToKeep()
    {
        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);

        for (var i = 1; i <= 5; i++)
        {
            var zipPath = Path.Combine(backupDir, $"Leecharr_backup_2026010{i}_000000.zip");
            File.WriteAllText(zipPath, "dummy-zip");
            File.SetLastWriteTimeUtc(zipPath, DateTime.UtcNow.AddMinutes(i));
        }

        var pruned = this.backupService.PruneOldBackups(backupDir, maxBackupsToKeep: 2, maxAgeDays: 365);

        pruned.Should().Be(3);
        Directory.GetFiles(backupDir, "*.zip").Should().HaveCount(2);
    }

    [Test]
    public void PruneOldBackups_DeletesBackupsOlderThanMaxAgeDays()
    {
        var backupDir = Path.Combine(this.testTempDir, "Backups", "manual");
        Directory.CreateDirectory(backupDir);

        var newest = Path.Combine(backupDir, "Leecharr_backup_newest.zip");
        File.WriteAllText(newest, "new");
        File.SetLastWriteTimeUtc(newest, DateTime.UtcNow.AddDays(-1));

        var older = Path.Combine(backupDir, "Leecharr_backup_older.zip");
        File.WriteAllText(older, "old");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-40));

        var pruned = this.backupService.PruneOldBackups(backupDir, maxBackupsToKeep: 10, maxAgeDays: 30);

        pruned.Should().Be(1);
        File.Exists(older).Should().BeFalse();
        File.Exists(newest).Should().BeTrue();
    }

    [Test]
    public void PruneOldBackups_RecursivelyPrunesSubdirectories()
    {
        var baseDir = Path.Combine(this.testTempDir, "Backups");
        var manualDir = Path.Combine(baseDir, "manual");
        var scheduledDir = Path.Combine(baseDir, "scheduled");
        Directory.CreateDirectory(manualDir);
        Directory.CreateDirectory(scheduledDir);

        for (var i = 1; i <= 4; i++)
        {
            var mFile = Path.Combine(manualDir, $"manual_{i}.zip");
            File.WriteAllText(mFile, "m");
            File.SetLastWriteTimeUtc(mFile, DateTime.UtcNow.AddMinutes(i));

            var sFile = Path.Combine(scheduledDir, $"scheduled_{i}.zip");
            File.WriteAllText(sFile, "s");
            File.SetLastWriteTimeUtc(sFile, DateTime.UtcNow.AddMinutes(i));
        }

        var pruned = this.backupService.PruneOldBackups(baseDir, maxBackupsToKeep: 2, maxAgeDays: 365);

        pruned.Should().Be(4); // 2 pruned from manual, 2 pruned from scheduled
        Directory.GetFiles(manualDir, "*.zip").Should().HaveCount(2);
        Directory.GetFiles(scheduledDir, "*.zip").Should().HaveCount(2);
    }
}

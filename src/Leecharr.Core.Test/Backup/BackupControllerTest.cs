// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Leecharr.Api.V1.Backup;
using Microsoft.AspNetCore.Mvc;
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
}

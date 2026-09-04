// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Leecharr.Api.V1.System;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace Leecharr.Core.Test.SystemServices;

[TestFixture]
public class LogFileControllerTest
{
    private string testTempDir = null!;
    private IAppFolderInfo appFolderInfo = null!;
    private LogFileController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.testTempDir = Path.Combine(Path.GetTempPath(), "LeecharrLogFileTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.testTempDir);

        this.appFolderInfo = Substitute.For<IAppFolderInfo>();
        this.appFolderInfo.AppDataFolder.Returns(this.testTempDir);

        this.controller = new LogFileController(this.appFolderInfo);
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

    [Test]
    public void GetLogFiles_WhenNoLogsDirectory_ReturnsEmptyList()
    {
        var result = this.controller.GetLogFiles();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var files = okResult.Value as List<LogFileResource>;
        files.Should().NotBeNull();
        files.Should().BeEmpty();
    }

    [Test]
    public void GetLogFiles_WhenFilesExist_ReturnsSortedList()
    {
        var logDir = Path.Combine(this.testTempDir, "logs");
        Directory.CreateDirectory(logDir);

        var olderFile = Path.Combine(logDir, "leecharr.0.txt");
        var newerFile = Path.Combine(logDir, "leecharr.txt");

        File.WriteAllText(olderFile, "old log content");
        File.SetLastWriteTimeUtc(olderFile, DateTime.UtcNow.AddHours(-2));

        File.WriteAllText(newerFile, "new log content");
        File.SetLastWriteTimeUtc(newerFile, DateTime.UtcNow);

        var result = this.controller.GetLogFiles();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var files = okResult.Value as List<LogFileResource>;
        files.Should().NotBeNull();
        files!.Count.Should().Be(2);
        files[0].Filename.Should().Be("leecharr.txt");
        files[1].Filename.Should().Be("leecharr.0.txt");
    }

    [Test]
    public void GetLogFile_WhenFileExists_ReturnsFileStreamResult()
    {
        var logDir = Path.Combine(this.testTempDir, "logs");
        Directory.CreateDirectory(logDir);
        var filePath = Path.Combine(logDir, "leecharr.txt");
        File.WriteAllText(filePath, "sample log content");

        var result = this.controller.GetLogFile("leecharr.txt");

        result.Should().BeOfType<FileStreamResult>();
        var fileResult = (FileStreamResult)result;
        fileResult.ContentType.Should().Be("text/plain");
        fileResult.FileDownloadName.Should().Be("leecharr.txt");

        using var reader = new StreamReader(fileResult.FileStream);
        var content = reader.ReadToEnd();
        content.Should().Be("sample log content");
    }

    [Test]
    public void GetLogFile_WhenFileDoesNotExist_ReturnsNotFound()
    {
        var logDir = Path.Combine(this.testTempDir, "logs");
        Directory.CreateDirectory(logDir);

        var result = this.controller.GetLogFile("missing.txt");

        result.Should().BeOfType<NotFoundResult>();
    }

    [TestCase("../secret.txt")]
    [TestCase("sub/nested.txt")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/etc/passwd")]
    public void GetLogFile_WithInvalidFilename_ReturnsBadRequest(string invalidFilename)
    {
        var result = this.controller.GetLogFile(invalidFilename);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void GetLogFile_HasBothRouteTemplates_IncludingBackwardCompatibleAlias()
    {
        var method = typeof(LogFileController).GetMethod(nameof(LogFileController.GetLogFile));
        method.Should().NotBeNull();

        var httpGetAttrs = method!.GetCustomAttributes<HttpGetAttribute>().ToList();
        httpGetAttrs.Should().HaveCount(2);

        var templates = httpGetAttrs.Select(a => a.Template).ToList();
        templates.Should().Contain("{filename}");
        templates.Should().Contain("/api/v1/log/file/{filename}");
    }

    [Test]
    public void ClearLogs_WhenLogsExist_DeletesAllLogFiles()
    {
        var logDir = Path.Combine(this.testTempDir, "logs");
        Directory.CreateDirectory(logDir);

        File.WriteAllText(Path.Combine(logDir, "log1.txt"), "log1");
        File.WriteAllText(Path.Combine(logDir, "log2.txt"), "log2");

        var result = this.controller.ClearLogs();

        result.Should().BeOfType<OkResult>();
        Directory.GetFiles(logDir).Should().BeEmpty();
    }
}

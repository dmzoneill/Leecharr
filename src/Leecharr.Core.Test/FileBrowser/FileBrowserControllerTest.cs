// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.FileBrowser;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.FileBrowser;

namespace Leecharr.Core.Test.FileBrowser;

[TestFixture]
public class FileBrowserControllerTest
{
    private IFileBrowserService fileBrowserService = null!;
    private FileBrowserController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.fileBrowserService = Substitute.For<IFileBrowserService>();
        this.controller = new FileBrowserController(this.fileBrowserService);
    }

    [Test]
    public void CreateDirectory_WhenPathIsRootOrSystemDirectory_ReturnsBadRequest()
    {
        this.fileBrowserService.ResolvePath("/etc/newdir").Returns("/etc/newdir");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc/newdir").Returns(true);

        var result = this.controller.CreateDirectory(new FileBrowserPathRequest { Path = "/etc/newdir" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Rename_WhenTargetIsRootOrSystemDirectory_ReturnsBadRequest()
    {
        this.fileBrowserService.ResolvePath("/etc/passwd").Returns("/etc/passwd");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc/passwd").Returns(true);

        var result = this.controller.Rename(new FileBrowserRenameRequest { Path = "/etc/passwd", NewName = "passwd.bak" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Delete_WhenPathIsRootOrSystemDirectory_ReturnsBadRequest()
    {
        this.fileBrowserService.ResolvePath("/etc/shadow").Returns("/etc/shadow");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc/shadow").Returns(true);

        var result = this.controller.Delete("/etc/shadow");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void BatchDelete_WhenPathIsRootOrSystemDirectory_RecordsFailure()
    {
        this.fileBrowserService.ResolvePath("/etc/shadow").Returns("/etc/shadow");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc/shadow").Returns(true);

        var result = this.controller.BatchDelete(new FileBrowserBatchDeleteRequest { Paths = new List<string> { "/etc/shadow" } });

        result.Should().BeOfType<OkObjectResult>();
        this.fileBrowserService.DidNotReceive().Delete(Arg.Any<string>());
    }

    [Test]
    public void Download_WhenPathIsRootOrSystemDirectory_ReturnsBadRequest()
    {
        this.fileBrowserService.ResolvePath("/etc/shadow").Returns("/etc/shadow");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc/shadow").Returns(true);

        var result = this.controller.Download("/etc/shadow");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Stream_WhenPathIsRootOrSystemDirectory_ReturnsBadRequest()
    {
        this.fileBrowserService.ResolvePath("/etc/shadow").Returns("/etc/shadow");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc/shadow").Returns(true);

        var result = this.controller.Stream("/etc/shadow");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void GetPlaylistM3u_WhenPathIsRootOrSystemDirectory_ReturnsBadRequest()
    {
        this.fileBrowserService.ResolvePath("/etc/shadow").Returns("/etc/shadow");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc/shadow").Returns(true);

        var result = this.controller.GetPlaylistM3u("/etc/shadow");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void GetPreview_WhenPathIsRootOrSystemDirectory_ReturnsBadRequest()
    {
        this.fileBrowserService.ResolvePath("/etc/passwd").Returns("/etc/passwd");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc/passwd").Returns(true);

        var result = this.controller.GetPreview("/etc/passwd");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Paste_WhenDestinationIsRootOrSystemDirectory_ReturnsBadRequest()
    {
        this.fileBrowserService.ResolvePath("/etc").Returns("/etc");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc").Returns(true);

        var result = this.controller.Paste(new FileBrowserTransferRequest
        {
            Sources = new List<string> { "/downloads/file.txt" },
            Destination = "/etc",
            Operation = "copy",
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task Upload_WhenTargetDirectoryIsRootOrSystemDirectory_ReturnsBadRequest()
    {
        this.fileBrowserService.ResolvePath("/etc").Returns("/etc");
        this.fileBrowserService.IsRootOrSystemDirectory("/etc").Returns(true);

        var result = await this.controller.Upload("/etc");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task Upload_WhenTargetFileAlreadyExists_RecordsFailureWithoutOverwriting()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var existingFile = Path.Combine(tempDir, "notes.txt");
        await File.WriteAllTextAsync(existingFile, "keep me");

        try
        {
            this.fileBrowserService.ResolvePath(tempDir).Returns(tempDir);
            this.fileBrowserService.IsRootOrSystemDirectory(tempDir).Returns(false);
            this.fileBrowserService.IsRootPath(tempDir).Returns(false);
            this.fileBrowserService.IsRootOrSystemDirectory(existingFile).Returns(false);

            var uploadBytes = Encoding.UTF8.GetBytes("new content");
            var formFile = new FormFile(new MemoryStream(uploadBytes), 0, uploadBytes.Length, "files", "notes.txt")
            {
                Headers = new HeaderDictionary(),
                ContentType = "text/plain",
            };

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW";
            httpContext.Request.Form = new FormCollection(
                new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
                new FormFileCollection { formFile });

            this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var result = await this.controller.Upload(tempDir);

            result.Should().BeOfType<OkObjectResult>();
            (await File.ReadAllTextAsync(existingFile)).Should().Be("keep me");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void Download_WhenPathIsDirectory_ReturnsBadRequest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            this.fileBrowserService.ResolvePath(tempDir).Returns(tempDir);
            this.fileBrowserService.IsRootOrSystemDirectory(tempDir).Returns(false);
            this.fileBrowserService.IsRootPath(tempDir).Returns(false);

            var result = this.controller.Download(tempDir);

            result.Should().BeOfType<BadRequestObjectResult>();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir);
            }
        }
    }

    [Test]
    public void GetPlaylistM3u_WhenPathIsDirectory_ReturnsBadRequest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            this.fileBrowserService.ResolvePath(tempDir).Returns(tempDir);
            this.fileBrowserService.IsRootOrSystemDirectory(tempDir).Returns(false);
            this.fileBrowserService.IsRootPath(tempDir).Returns(false);

            var result = this.controller.GetPlaylistM3u(tempDir);

            result.Should().BeOfType<BadRequestObjectResult>();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir);
            }
        }
    }

    [Test]
    public void GetPlaylistM3u_WhenApiKeyQueryProvided_PreservesApiKeyInStreamUrl()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            this.fileBrowserService.ResolvePath(tempFile).Returns(tempFile);
            this.fileBrowserService.IsRootOrSystemDirectory(tempFile).Returns(false);
            this.fileBrowserService.IsRootPath(tempFile).Returns(false);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("leecharr.local:7889");
            httpContext.Request.QueryString = new QueryString("?apikey=secret_api_key_123");

            this.controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
            };

            var result = this.controller.GetPlaylistM3u(tempFile);

            result.Should().BeOfType<FileContentResult>();
            var fileResult = (FileContentResult)result;
            var content = Encoding.UTF8.GetString(fileResult.FileContents);
            content.Should().Contain("&apikey=secret_api_key_123");
            content.Should().Contain("https://leecharr.local:7889/api/v1/files/stream?path=");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void GetPlaylistM3u_WhenApiKeyHeaderProvided_PreservesApiKeyInStreamUrl()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            this.fileBrowserService.ResolvePath(tempFile).Returns(tempFile);
            this.fileBrowserService.IsRootOrSystemDirectory(tempFile).Returns(false);
            this.fileBrowserService.IsRootPath(tempFile).Returns(false);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "http";
            httpContext.Request.Host = new HostString("localhost:7889");
            httpContext.Request.Headers["X-Api-Key"] = "header_api_key_xyz";

            this.controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
            };

            var result = this.controller.GetPlaylistM3u(tempFile);

            result.Should().BeOfType<FileContentResult>();
            var fileResult = (FileContentResult)result;
            var content = Encoding.UTF8.GetString(fileResult.FileContents);
            content.Should().Contain("&apikey=header_api_key_xyz");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void ReadEndpoints_WhenSymlinkTargetsProtectedPath_ReturnBadRequest()
    {
        string protectedDir = null;
        string linkDir = null;
        try
        {
            protectedDir = CreateTempDirectory();
            linkDir = CreateTempDirectory();
            var secret = Path.Combine(protectedDir, "notes.txt");
            File.WriteAllText(secret, "do-not-leak");

            var fileLink = Path.Combine(linkDir, "notes.txt");
            File.CreateSymbolicLink(fileLink, secret);

            var directoryLink = Path.Combine(linkDir, "linked");
            Directory.CreateSymbolicLink(directoryLink, protectedDir);
            var throughLink = Path.Combine(directoryLink, "notes.txt");

            if (LexicalPathIsProtected(fileLink) || LexicalPathIsProtected(throughLink))
            {
                Assert.Pass("Temp directory is inside a protected path.");
            }

            var controller = FileBrowserControllerTest.CreateController(protectedDir);

            controller.Download(fileLink).Should().BeOfType<BadRequestObjectResult>();
            controller.Stream(fileLink).Should().BeOfType<BadRequestObjectResult>();
            controller.GetPreview(fileLink).Should().BeOfType<BadRequestObjectResult>();
            controller.GetPlaylistM3u(fileLink).Should().BeOfType<BadRequestObjectResult>();
            controller.Download(throughLink).Should().BeOfType<BadRequestObjectResult>();
            controller.GetPreview(throughLink).Should().BeOfType<BadRequestObjectResult>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Pass("Symlink creation is not permitted in this environment.");
        }
        finally
        {
            DeleteTempDirectory(linkDir);
            DeleteTempDirectory(protectedDir);
        }
    }

    [Test]
    public void Download_WhenSymlinkTargetsOrdinaryFile_ReturnsPhysicalFile()
    {
        string protectedDir = null;
        string allowedDir = null;
        string linkDir = null;
        try
        {
            protectedDir = CreateTempDirectory();
            allowedDir = CreateTempDirectory();
            linkDir = CreateTempDirectory();
            var allowedFile = Path.Combine(allowedDir, "song.mp3");
            File.WriteAllText(allowedFile, "audio");

            var fileLink = Path.Combine(linkDir, "song.mp3");
            File.CreateSymbolicLink(fileLink, allowedFile);

            if (LexicalPathIsProtected(fileLink))
            {
                Assert.Pass("Temp directory is inside a protected path.");
            }

            var controller = FileBrowserControllerTest.CreateController(protectedDir);

            controller.Download(fileLink).Should().BeOfType<PhysicalFileResult>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Pass("Symlink creation is not permitted in this environment.");
        }
        finally
        {
            DeleteTempDirectory(linkDir);
            DeleteTempDirectory(allowedDir);
            DeleteTempDirectory(protectedDir);
        }
    }

    private static bool LexicalPathIsProtected(string path)
    {
        var service = new FileBrowserService(Substitute.For<IDiskProvider>(), Substitute.For<IConfigService>());
        return service.IsRootOrSystemDirectory(path);
    }

    private static FileBrowserController CreateController(string protectedDirectory)
    {
        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(protectedDirectory);
        var service = new FileBrowserService(
            Substitute.For<IDiskProvider>(),
            Substitute.For<IConfigService>(),
            appFolderInfo);
        return new FileBrowserController(service);
    }

    private static string CreateTempDirectory()
    {
        var root = OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp";
        var directory = Path.Combine(root, "leecharr-fb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(directory, false);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry, false);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }

        Directory.Delete(directory, true);
    }
}

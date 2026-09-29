// Copyright (c) PlaceholderCompany. All rights reserved.

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
}

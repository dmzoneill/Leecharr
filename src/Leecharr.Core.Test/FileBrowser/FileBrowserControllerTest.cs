// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.FileBrowser;
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
}

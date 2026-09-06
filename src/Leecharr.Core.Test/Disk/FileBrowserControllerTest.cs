// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using Leecharr.Api.V1.FileBrowser;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.FileBrowser;

namespace Leecharr.Core.Test.Disk;

[TestFixture]
public class FileBrowserControllerTest
{
    private IFileBrowserService service = null!;
    private FileBrowserController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = Substitute.For<IFileBrowserService>();
        this.controller = new FileBrowserController(this.service);
    }

    [Test]
    public void GetListing_WhenValid_ReturnsOkListing()
    {
        var listing = new FileBrowserListing { Path = "/downloads", Exists = true };
        this.service.ListDirectory("/downloads").Returns(listing);

        var result = this.controller.GetListing("/downloads");
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        okResult.Value.Should().BeSameAs(listing);
    }

    [Test]
    public void GetListing_WhenUnauthorizedAccess_ReturnsBadRequestWithoutInternalErrorLeak()
    {
        this.service.ListDirectory("/etc").Returns(_ => throw new UnauthorizedAccessException("Access denied"));

        var result = this.controller.GetListing("/etc");
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void CreateDirectory_WhenRequestNull_ReturnsBadRequest()
    {
        var result = this.controller.CreateDirectory(null!);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void CreateDirectory_WhenUnauthorizedAccess_ReturnsBadRequest()
    {
        this.service.When(s => s.CreateDirectory("/etc/test")).Do(_ => throw new UnauthorizedAccessException("Access denied"));

        var result = this.controller.CreateDirectory(new FileBrowserPathRequest { Path = "/etc/test" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Rename_WhenValid_ReturnsOk()
    {
        var result = this.controller.Rename(new FileBrowserRenameRequest { Path = "/downloads/old.txt", NewName = "new.txt" });
        result.Should().BeOfType<OkObjectResult>();

        this.service.Received(1).Rename("/downloads/old.txt", "new.txt");
    }

    [Test]
    public void Delete_WhenEmptyPath_ReturnsBadRequest()
    {
        var result = this.controller.Delete(string.Empty);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Delete_WhenValid_ReturnsOk()
    {
        var result = this.controller.Delete("/downloads/file.txt");
        result.Should().BeOfType<OkObjectResult>();

        this.service.Received(1).Delete("/downloads/file.txt");
    }
}

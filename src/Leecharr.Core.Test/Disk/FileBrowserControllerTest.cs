// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using Leecharr.Api.V1.FileBrowser;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.FileBrowser;

namespace Leecharr.Core.Test.Disk;

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
    public void GetListing_WhenUnauthorizedAccessExceptionThrown_Returns403Forbidden()
    {
        this.fileBrowserService.ListDirectory("/etc").Returns(_ => throw new UnauthorizedAccessException("Forbidden"));

        var result = this.controller.GetListing("/etc");

        result.Result.Should().BeOfType<ObjectResult>();
        var objResult = result.Result as ObjectResult;
        objResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Test]
    public void CreateDirectory_WhenUnauthorizedAccessExceptionThrown_Returns403Forbidden()
    {
        this.fileBrowserService.When(s => s.CreateDirectory("/etc/evil")).Do(_ => throw new UnauthorizedAccessException("Forbidden"));

        var result = this.controller.CreateDirectory(new FileBrowserPathRequest { Path = "/etc/evil" });

        result.Should().BeOfType<ObjectResult>();
        var objResult = result as ObjectResult;
        objResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Test]
    public void Rename_WhenUnauthorizedAccessExceptionThrown_Returns403Forbidden()
    {
        this.fileBrowserService.When(s => s.Rename("/etc/passwd", "pwned")).Do(_ => throw new UnauthorizedAccessException("Forbidden"));

        var result = this.controller.Rename(new FileBrowserRenameRequest { Path = "/etc/passwd", NewName = "pwned" });

        result.Should().BeOfType<ObjectResult>();
        var objResult = result as ObjectResult;
        objResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Test]
    public void Delete_WhenUnauthorizedAccessExceptionThrown_Returns403Forbidden()
    {
        this.fileBrowserService.When(s => s.Delete("/etc/shadow")).Do(_ => throw new UnauthorizedAccessException("Forbidden"));

        var result = this.controller.Delete("/etc/shadow");

        result.Should().BeOfType<ObjectResult>();
        var objResult = result as ObjectResult;
        objResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}

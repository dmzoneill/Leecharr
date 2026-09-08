// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Update;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Update;

namespace Leecharr.Core.Test.Update;

[TestFixture]
public class UpdateControllerTest
{
    [Test]
    public async Task GetUpdates_ReturnsUpdatesFromService()
    {
        var updateService = Substitute.For<IUpdateCheckService>();
        updateService.GetAvailableUpdatesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new List<UpdatePackage>
        {
            new UpdatePackage
            {
                Version = "1.0.30",
                ReleaseDate = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc),
                FileName = "Leecharr.1.0.30.linux-x64.tar.gz",
                Url = "https://github.com/dmzoneill/Leecharr/releases/tag/v1.0.30",
                Installed = true,
                Latest = true,
                Changes = new UpdatePackageChanges
                {
                    New = new List<string> { "Added DiskSpace and Memory health checks" },
                    Fixed = new List<string> { "Fixed async health check execution" },
                },
            },
        }));

        var controller = new UpdateController(updateService);
        var actionResult = await controller.GetUpdates();

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = actionResult.Result as OkObjectResult;
        var list = okResult.Value as List<UpdateResource>;

        list.Should().NotBeNull();
        list.Should().HaveCount(1);
        list[0].Version.Should().Be("1.0.30");
        list[0].Installed.Should().BeTrue();
        list[0].Latest.Should().BeTrue();
        list[0].Changes.New.Should().Contain("Added DiskSpace and Memory health checks");
        list[0].Changes.Fixed.Should().Contain("Fixed async health check execution");
    }
}

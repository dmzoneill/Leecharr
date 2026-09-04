// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.ArrIntegration;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrSyncControllerTest
{
    private IArrConnectionRepository arrRepository = null!;
    private ITorrentService torrentService = null!;
    private ArrSyncController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.arrRepository = Substitute.For<IArrConnectionRepository>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.controller = new ArrSyncController(this.arrRepository, this.torrentService);
    }

    [Test]
    public async Task Sync_WhenNoConnections_ReturnsZeroCounts()
    {
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var actionResult = await this.controller.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(0);
        result.TotalCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.Added.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(0);
        result.Message.Should().Be("Arr sync completed successfully (0/0 connected).");
    }

    [Test]
    public async Task Sync_WhenConnectionFails_ReturnsFailedCount()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new()
            {
                Id = 1,
                Name = "Sonarr",
                Url = "http://127.0.0.1:59999",
                ApiKey = "dummy-key",
            },
        };
        this.arrRepository.GetEnabled().Returns(connections);

        var actionResult = await this.controller.Sync();
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var result = okResult!.Value as SyncResultResource;
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.SyncedCount.Should().Be(0);
        result.TotalCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Added.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(1);
        result.Message.Should().Be("Arr sync completed successfully (0/1 connected).");
    }

    [Test]
    public void SyncResultResource_PropertiesCanBeSetAndRetrieved()
    {
        var resource = new SyncResultResource
        {
            Success = true,
            SyncedCount = 3,
            TotalCount = 4,
            FailedCount = 1,
            Added = 3,
            Skipped = 0,
            Failed = 1,
            Message = "Test message",
        };

        resource.Success.Should().BeTrue();
        resource.SyncedCount.Should().Be(3);
        resource.TotalCount.Should().Be(4);
        resource.FailedCount.Should().Be(1);
        resource.Added.Should().Be(3);
        resource.Skipped.Should().Be(0);
        resource.Failed.Should().Be(1);
        resource.Message.Should().Be("Test message");
    }
}

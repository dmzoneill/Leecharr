// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Reflection;
using FluentAssertions;
using Leecharr.Api.V1.Tracker;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class EmbeddedTrackerControllerTest
{
    private IEmbeddedTrackerService trackerService = null!;
    private EmbeddedTrackerController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.trackerService = Substitute.For<IEmbeddedTrackerService>();
        this.controller = new EmbeddedTrackerController(this.trackerService);
    }

    [Test]
    public void GetStats_ReturnsAllStatsMetrics()
    {
        this.trackerService.IsEnabled.Returns(true);
        this.trackerService.ActiveSwarmsCount.Returns(5);
        this.trackerService.ActivePeersCount.Returns(42);

        var actionResult = this.controller.GetStats();
        var okResult = actionResult as OkObjectResult;

        okResult.Should().NotBeNull();
        var val = okResult!.Value;
        val.Should().NotBeNull();

        var type = val!.GetType();
        type.GetProperty("enabled")!.GetValue(val).Should().Be(true);
        type.GetProperty("activeSwarms")!.GetValue(val).Should().Be(5);
        type.GetProperty("totalTorrents")!.GetValue(val).Should().Be(5);
        type.GetProperty("activePeers")!.GetValue(val).Should().Be(42);
        type.GetProperty("totalPeers")!.GetValue(val).Should().Be(42);
        type.GetProperty("totalAnnounces")!.GetValue(val).Should().Be(0);
        type.GetProperty("totalScrapes")!.GetValue(val).Should().Be(0);
        type.GetProperty("uptime")!.GetValue(val).Should().Be(0);
    }

    [Test]
    public void GetTorrents_ReturnsEmptyArray()
    {
        var actionResult = this.controller.GetTorrents();
        var okResult = actionResult as OkObjectResult;

        okResult.Should().NotBeNull();
        var val = okResult!.Value as object[];
        val.Should().NotBeNull();
        val!.Length.Should().Be(0);
    }

    [Test]
    public void Controller_HasExpectedRouteAttributes()
    {
        var getStatsMethod = typeof(EmbeddedTrackerController).GetMethod(nameof(EmbeddedTrackerController.GetStats));
        var statsAttributes = getStatsMethod!.GetCustomAttributes<HttpGetAttribute>();
        statsAttributes.Should().Contain(a => a.Template == "/api/v1/trackerserver/stats");
        statsAttributes.Should().Contain(a => a.Template == "/api/v1/tracker/stats");

        var getTorrentsMethod = typeof(EmbeddedTrackerController).GetMethod(nameof(EmbeddedTrackerController.GetTorrents));
        var torrentsAttributes = getTorrentsMethod!.GetCustomAttributes<HttpGetAttribute>();
        torrentsAttributes.Should().Contain(a => a.Template == "/api/v1/trackerserver/torrents");
        torrentsAttributes.Should().Contain(a => a.Template == "/api/v1/tracker/torrents");
    }
}

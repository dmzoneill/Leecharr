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

    [Test]
    public void Announce_ParsesNoPeerId_WhenSpecified()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?info_hash=0123456789012345678901234567890123456789&peer_id=-qB4650-123456789012&port=6881&uploaded=0&downloaded=0&left=100&no_peer_id=1");
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        this.trackerService.ProcessAnnounce(Arg.Any<TrackerAnnounceRequest>()).Returns(Array.Empty<byte>());

        var result = this.controller.Announce();
        result.Should().BeOfType<FileContentResult>();

        this.trackerService.Received(1).ProcessAnnounce(Arg.Is<TrackerAnnounceRequest>(r =>
            r.NoPeerId == true &&
            r.Port == 6881));
    }

    [Test]
    public void Announce_ParsesTrackerId_WhenSpecified()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?info_hash=0123456789012345678901234567890123456789&peer_id=-qB4650-123456789012&port=6881&uploaded=0&downloaded=0&left=100&trackerid=my-tracker-id-123");
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        this.trackerService.ProcessAnnounce(Arg.Any<TrackerAnnounceRequest>()).Returns(Array.Empty<byte>());

        var result = this.controller.Announce();
        result.Should().BeOfType<FileContentResult>();

        this.trackerService.Received(1).ProcessAnnounce(Arg.Is<TrackerAnnounceRequest>(r =>
            r.TrackerId == "my-tracker-id-123"));
    }

    [Test]
    public void Announce_ParsesTrackerIdWithUnderscore_WhenSpecified()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?info_hash=0123456789012345678901234567890123456789&peer_id=-qB4650-123456789012&port=6881&uploaded=0&downloaded=0&left=100&tracker_id=alt-tracker-id");
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        this.trackerService.ProcessAnnounce(Arg.Any<TrackerAnnounceRequest>()).Returns(Array.Empty<byte>());

        var result = this.controller.Announce();
        result.Should().BeOfType<FileContentResult>();

        this.trackerService.Received(1).ProcessAnnounce(Arg.Is<TrackerAnnounceRequest>(r =>
            r.TrackerId == "alt-tracker-id"));
    }

    [Test]
    public void Announce_ParsesIpv6Parameter_WhenSpecified()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?info_hash=0123456789012345678901234567890123456789&peer_id=-qB4650-123456789012&port=6881&uploaded=0&downloaded=0&left=100&ipv6=%5B2001:db8::1%5D:6882");
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        this.trackerService.ProcessAnnounce(Arg.Any<TrackerAnnounceRequest>()).Returns(Array.Empty<byte>());

        var result = this.controller.Announce();
        result.Should().BeOfType<FileContentResult>();

        this.trackerService.Received(1).ProcessAnnounce(Arg.Is<TrackerAnnounceRequest>(r =>
            r.Ipv6 != null &&
            r.Ipv6.ToString() == "2001:db8::1" &&
            r.Ipv6Port == 6882 &&
            r.RemoteIp.ToString() == "2001:db8::1"));
    }
}

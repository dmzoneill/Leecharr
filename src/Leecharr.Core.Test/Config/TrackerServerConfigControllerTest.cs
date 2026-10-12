// Copyright (c) FeedItOut. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Config;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent.Tracker;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Config;

[TestFixture]
public class TrackerServerConfigControllerTest
{
    private IConfigService configService = null!;
    private IUdpTrackerService udpTrackerService = null!;
    private IHttpTrackerService httpTrackerService = null!;
    private TrackerServerConfigController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.udpTrackerService = Substitute.For<IUdpTrackerService>();
        this.httpTrackerService = Substitute.For<IHttpTrackerService>();
        this.controller = new TrackerServerConfigController(
            this.configService,
            this.udpTrackerService,
            this.httpTrackerService);
    }

    [Test]
    public async Task SaveConfig_WhenValidationFails_DoesNotStopOrRestartTrackers()
    {
        var resource = new TrackerServerConfigResource
        {
            TrackerServerEnabled = true,
            TrackerUdpEnabled = false,
            TrackerHttpEnabled = true,
            TrackerHttpPort = 8080,
            TrackerUdpPort = 8081,
            TrackerAnnounceInterval = 30,
            TrackerMaxPeersPerAnnounce = 50,
            TrackerRateLimitPerMinute = 60,
        };

        var actionResult = await this.controller.SaveConfig(resource);

        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
        await Task.Delay(100);
        await this.udpTrackerService.DidNotReceive().StopAsync();
        await this.udpTrackerService.DidNotReceive().RestartAsync();
        await this.httpTrackerService.DidNotReceive().StopAsync();
        await this.httpTrackerService.DidNotReceive().RestartAsync();
        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Any<System.Collections.Generic.Dictionary<string, object>>());
    }

    [Test]
    public async Task SaveConfig_WhenPortValidationFails_DoesNotRestartTrackers()
    {
        var resource = new TrackerServerConfigResource
        {
            TrackerServerEnabled = true,
            TrackerUdpEnabled = true,
            TrackerHttpEnabled = true,
            TrackerHttpPort = 0,
            TrackerUdpPort = 8081,
            TrackerAnnounceInterval = 300,
            TrackerMaxPeersPerAnnounce = 50,
            TrackerRateLimitPerMinute = 60,
        };

        var actionResult = await this.controller.SaveConfig(resource);

        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
        await Task.Delay(100);
        await this.udpTrackerService.DidNotReceive().RestartAsync();
        await this.httpTrackerService.DidNotReceive().RestartAsync();
        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Any<System.Collections.Generic.Dictionary<string, object>>());
    }
}

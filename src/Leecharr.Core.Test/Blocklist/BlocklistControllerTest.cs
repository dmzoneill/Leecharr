// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Blocklist;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Blocklist;

namespace Leecharr.Core.Test.Blocklist;

[TestFixture]
public class BlocklistControllerTest
{
    private IConfigService configService = null!;
    private IBlocklistService blocklistService = null!;
    private IBlocklistUpdateService updateService = null!;
    private BlocklistController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.blocklistService = Substitute.For<IBlocklistService>();
        this.updateService = Substitute.For<IBlocklistUpdateService>();

        this.configService.BlocklistEnabled.Returns(true);
        this.configService.BlocklistUrl.Returns("https://example.com/blocklist.txt");
        this.configService.BlocklistUpdateIntervalHours.Returns(48);

        this.blocklistService.TotalRulesLoaded.Returns(1500);

        this.controller = new BlocklistController(
            this.configService,
            this.blocklistService,
            this.updateService);
    }

    [Test]
    public void GetBlocklist_WhenCalled_ReturnsOkObjectResultWithPopulatedResource()
    {
        var actionResult = this.controller.GetBlocklist();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var resource = okResult.Value as BlocklistResource;
        resource.Should().NotBeNull();
        resource!.Enabled.Should().BeTrue();
        resource.Url.Should().Be("https://example.com/blocklist.txt");
        resource.AutoUpdateEnabled.Should().BeTrue();
        resource.AutoUpdateIntervalDays.Should().Be(2);
        resource.Ipv4RuleCount.Should().Be(1500);
        resource.Ipv6RuleCount.Should().Be(0);
        resource.TotalRuleCount.Should().Be(1500);
        resource.RuleCount.Should().Be(1500);
        resource.LastSyncStatus.Should().Be("Active");
        resource.LastUpdatedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        resource.NextScheduledSyncUtc.Should().BeCloseTo(DateTime.UtcNow.AddHours(48), TimeSpan.FromSeconds(5));
    }

    [Test]
    public void GetBlocklist_WhenZeroRulesLoaded_ReturnsIdleSyncStatus()
    {
        this.blocklistService.TotalRulesLoaded.Returns(0);

        var actionResult = this.controller.GetBlocklist();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var resource = okResult!.Value as BlocklistResource;
        resource.Should().NotBeNull();
        resource!.LastSyncStatus.Should().Be("Idle");
        resource.TotalRuleCount.Should().Be(0);
    }

    [Test]
    public void GetBlocklist_WhenIntervalHoursLessThan24_DefaultsToAtLeastOneDayInterval()
    {
        this.configService.BlocklistUpdateIntervalHours.Returns(6);

        var actionResult = this.controller.GetBlocklist();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var resource = okResult!.Value as BlocklistResource;
        resource.Should().NotBeNull();
        resource!.AutoUpdateIntervalDays.Should().Be(1);
    }

    [Test]
    public void UpdateBlocklist_WhenRequestIsNull_ReturnsBadRequest()
    {
        var actionResult = this.controller.UpdateBlocklist(null!);

        var badResult = actionResult.Result as BadRequestObjectResult;
        badResult.Should().NotBeNull();
        badResult!.StatusCode.Should().Be(400);
        badResult.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public void UpdateBlocklist_WithAllFields_SavesConfigurationAndReturnsOkWithResource()
    {
        Dictionary<string, object> savedUpdates = null!;
        this.configService.SaveConfigDictionary(Arg.Do<Dictionary<string, object>>(x => savedUpdates = x));

        var request = new BlocklistConfigRequest
        {
            Enabled = true,
            Url = "  https://rules.example.org/blacklist.p2p  ",
            AutoUpdateIntervalDays = 7,
        };

        var actionResult = this.controller.UpdateBlocklist(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        savedUpdates.Should().NotBeNull();
        savedUpdates!.Should().ContainKey("BlocklistEnabled").WhoseValue.Should().Be(true);
        savedUpdates.Should().ContainKey("BlocklistUrl").WhoseValue.Should().Be("https://rules.example.org/blacklist.p2p");
        savedUpdates.Should().ContainKey("BlocklistUpdateIntervalHours").WhoseValue.Should().Be(168);
    }

    [Test]
    public void UpdateBlocklist_WithPartialFields_SavesOnlySpecifiedFields()
    {
        Dictionary<string, object> savedUpdates = null!;
        this.configService.SaveConfigDictionary(Arg.Do<Dictionary<string, object>>(x => savedUpdates = x));

        var request = new BlocklistConfigRequest
        {
            Enabled = false,
        };

        var actionResult = this.controller.UpdateBlocklist(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        savedUpdates.Should().NotBeNull();
        savedUpdates!.Should().HaveCount(1);
        savedUpdates.Should().ContainKey("BlocklistEnabled").WhoseValue.Should().Be(false);
    }

    [Test]
    public void UpdateBlocklist_WithOnlyUrl_TrimsAndSavesUrl()
    {
        Dictionary<string, object> savedUpdates = null!;
        this.configService.SaveConfigDictionary(Arg.Do<Dictionary<string, object>>(x => savedUpdates = x));

        var request = new BlocklistConfigRequest
        {
            Url = "   http://downloads.sourceforge.net/blocklist.txt   ",
        };

        var actionResult = this.controller.UpdateBlocklist(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        savedUpdates.Should().NotBeNull();
        savedUpdates!.Should().HaveCount(1);
        savedUpdates.Should().ContainKey("BlocklistUrl").WhoseValue.Should().Be("http://downloads.sourceforge.net/blocklist.txt");
    }

    [Test]
    public void UpdateBlocklist_WithZeroOrNegativeIntervalDays_DoesNotSaveInterval()
    {
        Dictionary<string, object> savedUpdates = null!;
        this.configService.SaveConfigDictionary(Arg.Do<Dictionary<string, object>>(x => savedUpdates = x));

        var request = new BlocklistConfigRequest
        {
            AutoUpdateIntervalDays = 0,
        };

        var actionResult = this.controller.UpdateBlocklist(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
        savedUpdates.Should().BeNull();
    }

    [Test]
    public void UpdateBlocklist_WithEmptyRequest_DoesNotCallSaveConfigDictionary()
    {
        var request = new BlocklistConfigRequest();

        var actionResult = this.controller.UpdateBlocklist(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task SyncBlocklistAsync_WhenUpdateReturnsPositiveCount_ReturnsSuccessResponse()
    {
        this.updateService.UpdateRulesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(2450));

        var actionResult = await this.controller.SyncBlocklistAsync(CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var response = okResult.Value as BlocklistSyncResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.Status.Should().Be("Success");
        response.RuleCount.Should().Be(2450);
        response.TotalRuleCount.Should().Be(2450);
        response.Message.Should().Contain("2,450 rules active");
        response.LastUpdatedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task SyncBlocklistAsync_WhenUpdateReturnsZeroRules_ReturnsWarningResponseWithExistingRuleCount()
    {
        this.updateService.UpdateRulesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        this.blocklistService.TotalRulesLoaded.Returns(350);

        var actionResult = await this.controller.SyncBlocklistAsync(CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var response = okResult!.Value as BlocklistSyncResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeFalse();
        response.Status.Should().Be("Warning");
        response.Message.Should().Be("No rules could be loaded from configured sources.");
        response.RuleCount.Should().Be(350);
        response.TotalRuleCount.Should().Be(350);
    }

    [Test]
    public async Task SyncBlocklistAsync_WhenUpdateServiceThrows_ReturnsErrorResponseWithExceptionMessage()
    {
        this.updateService.UpdateRulesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("Connection refused by peer.")));
        this.blocklistService.TotalRulesLoaded.Returns(800);

        var actionResult = await this.controller.SyncBlocklistAsync(CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var response = okResult!.Value as BlocklistSyncResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeFalse();
        response.Status.Should().Be("Error");
        response.Message.Should().Contain("Connection refused by peer.");
        response.RuleCount.Should().Be(800);
        response.TotalRuleCount.Should().Be(800);
    }

    [Test]
    public async Task SyncBlocklistAsync_PassesCancellationTokenToUpdateService()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        this.updateService.UpdateRulesAsync(token).Returns(Task.FromResult(10));

        await this.controller.SyncBlocklistAsync(token);

        await this.updateService.Received(1).UpdateRulesAsync(token);
    }

    [Test]
    public void TestIp_WhenRequestIsNull_ReturnsBadRequest()
    {
        var actionResult = this.controller.TestIp(null!);

        var badResult = actionResult.Result as BadRequestObjectResult;
        badResult.Should().NotBeNull();
        badResult!.StatusCode.Should().Be(400);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void TestIp_WhenIpIsNullOrEmptyOrWhitespace_ReturnsBadRequest(string ip)
    {
        var request = new BlocklistTestRequest { Ip = ip! };

        var actionResult = this.controller.TestIp(request);

        var badResult = actionResult.Result as BadRequestObjectResult;
        badResult.Should().NotBeNull();
        badResult!.StatusCode.Should().Be(400);
    }

    [TestCase("invalid-ip-string")]
    [TestCase("256.0.0.1")]
    [TestCase("1.2.3.4.5")]
    [TestCase("999.999.999.999")]
    [TestCase("example.com")]
    public void TestIp_WhenIpIsInvalidFormat_ReturnsBadRequest(string invalidIp)
    {
        var request = new BlocklistTestRequest { Ip = invalidIp };

        var actionResult = this.controller.TestIp(request);

        var badResult = actionResult.Result as BadRequestObjectResult;
        badResult.Should().NotBeNull();
        badResult!.StatusCode.Should().Be(400);
    }

    [Test]
    public void TestIp_WhenIpIsBlocked_ReturnsOkWithBlockedTrueAndRuleDescription()
    {
        this.blocklistService.IsIpBlocked("192.168.1.100").Returns(true);

        var request = new BlocklistTestRequest { Ip = "192.168.1.100" };

        var actionResult = this.controller.TestIp(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var response = okResult.Value as BlocklistTestResponse;
        response.Should().NotBeNull();
        response!.IsBlocked.Should().BeTrue();
        response.Rule.Should().Be("Active blocklist rule");
    }

    [Test]
    public void TestIp_WhenIpIsNotBlocked_ReturnsOkWithBlockedFalseAndNullRule()
    {
        this.blocklistService.IsIpBlocked("8.8.8.8").Returns(false);

        var request = new BlocklistTestRequest { Ip = "8.8.8.8" };

        var actionResult = this.controller.TestIp(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var response = okResult.Value as BlocklistTestResponse;
        response.Should().NotBeNull();
        response!.IsBlocked.Should().BeFalse();
        response.Rule.Should().BeNull();
    }

    [Test]
    public void TestIp_WithWhitespacePadding_TrimsAndEvaluatesSuccessfully()
    {
        this.blocklistService.IsIpBlocked("10.0.0.1").Returns(true);

        var request = new BlocklistTestRequest { Ip = "   10.0.0.1   " };

        var actionResult = this.controller.TestIp(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var response = okResult!.Value as BlocklistTestResponse;
        response.Should().NotBeNull();
        response!.IsBlocked.Should().BeTrue();
    }

    [Test]
    public void TestIp_WithIpv6Address_EvaluatesSuccessfully()
    {
        this.blocklistService.IsIpBlocked("2001:db8::1").Returns(true);

        var request = new BlocklistTestRequest { Ip = "2001:db8::1" };

        var actionResult = this.controller.TestIp(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var response = okResult!.Value as BlocklistTestResponse;
        response.Should().NotBeNull();
        response!.IsBlocked.Should().BeTrue();
    }
}

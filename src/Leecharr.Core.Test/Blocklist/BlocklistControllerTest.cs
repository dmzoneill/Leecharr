// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Blocklist;
using Microsoft.AspNetCore.Authorization;
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
    private IBlocklistSyncState syncState = null!;
    private DateTime storedLastSync;
    private DateTime storedNextSync;
    private BlocklistController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.blocklistService = Substitute.For<IBlocklistService>();
        this.updateService = Substitute.For<IBlocklistUpdateService>();
        this.syncState = Substitute.For<IBlocklistSyncState>();

        this.configService.BlocklistEnabled.Returns(true);
        this.configService.BlocklistAutoUpdateEnabled.Returns(true);
        this.configService.BlocklistUrl.Returns("https://example.com/blocklist.txt");
        this.configService.BlocklistUpdateIntervalHours.Returns(48);

        this.blocklistService.TotalRulesLoaded.Returns(1500);
        this.blocklistService.Ipv4RulesLoaded.Returns(1500);
        this.blocklistService.Ipv6RulesLoaded.Returns(0);

        this.storedLastSync = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        this.storedNextSync = new DateTime(2024, 6, 3, 12, 0, 0, DateTimeKind.Utc);
        this.syncState.LastSuccessfulSyncUtc.Returns(_ => this.storedLastSync);
        this.syncState.ResolveNextScheduledSyncUtc(Arg.Any<int>(), Arg.Any<bool>()).Returns(_ => this.storedNextSync);

        this.controller = new BlocklistController(
            this.configService,
            this.blocklistService,
            this.updateService,
            this.syncState);
    }

    [Test]
    public void GetBlocklist_DoesNotAllowAnonymousAccess()
    {
        var getMethod = typeof(BlocklistController).GetMethod(nameof(BlocklistController.GetBlocklist));
        getMethod.Should().NotBeNull();
        getMethod!.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
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
        resource.AutoUpdateIntervalHours.Should().Be(48);
        resource.AutoUpdateIntervalDays.Should().Be(2);
        resource.Ipv4RuleCount.Should().Be(1500);
        resource.Ipv6RuleCount.Should().Be(0);
        resource.TotalRuleCount.Should().Be(1500);
        resource.RuleCount.Should().Be(1500);
        resource.LastSyncStatus.Should().Be("Active");
        resource.LastUpdatedUtc.Should().Be(this.storedLastSync);
        resource.NextScheduledSyncUtc.Should().Be(this.storedNextSync);
        this.syncState.Received(1).ResolveNextScheduledSyncUtc(48, true);
    }

    [Test]
    public void GetBlocklist_WhenMixedIpv4AndIpv6Rules_ReturnsSeparateCounts()
    {
        this.blocklistService.TotalRulesLoaded.Returns(150);
        this.blocklistService.Ipv4RulesLoaded.Returns(100);
        this.blocklistService.Ipv6RulesLoaded.Returns(50);

        var actionResult = this.controller.GetBlocklist();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var resource = okResult!.Value as BlocklistResource;
        resource.Should().NotBeNull();
        resource!.Ipv4RuleCount.Should().Be(100);
        resource.Ipv6RuleCount.Should().Be(50);
        resource.TotalRuleCount.Should().Be(150);
        resource.RuleCount.Should().Be(150);
    }

    [Test]
    public void GetBlocklist_WhenZeroRulesLoaded_ReturnsIdleSyncStatus()
    {
        this.blocklistService.TotalRulesLoaded.Returns(0);
        this.blocklistService.Ipv4RulesLoaded.Returns(0);
        this.blocklistService.Ipv6RulesLoaded.Returns(0);

        var actionResult = this.controller.GetBlocklist();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var resource = okResult!.Value as BlocklistResource;
        resource.Should().NotBeNull();
        resource!.LastSyncStatus.Should().Be("Idle");
        resource.TotalRuleCount.Should().Be(0);
    }

    [Test]
    public void GetBlocklist_WhenIntervalHoursLessThan24_ReturnsCanonicalHoursAndDisplayDays()
    {
        this.configService.BlocklistUpdateIntervalHours.Returns(6);

        var actionResult = this.controller.GetBlocklist();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var resource = okResult!.Value as BlocklistResource;
        resource.Should().NotBeNull();
        resource!.AutoUpdateIntervalHours.Should().Be(6);
        resource.AutoUpdateIntervalDays.Should().Be(1);
    }

    [Test]
    public void GetBlocklist_WhenIntervalHoursNotMultipleOf24_ReturnsFloorDayCount()
    {
        this.configService.BlocklistUpdateIntervalHours.Returns(36);

        var actionResult = this.controller.GetBlocklist();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var resource = okResult!.Value as BlocklistResource;
        resource.Should().NotBeNull();
        resource!.AutoUpdateIntervalHours.Should().Be(36);
        resource.AutoUpdateIntervalDays.Should().Be(1);
    }

    [Test]
    public void GetBlocklist_WhenAutoUpdateDisabled_ReturnsAutoUpdateEnabledFalse()
    {
        this.configService.BlocklistAutoUpdateEnabled.Returns(false);

        var actionResult = this.controller.GetBlocklist();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var resource = okResult!.Value as BlocklistResource;
        resource.Should().NotBeNull();
        resource!.AutoUpdateEnabled.Should().BeFalse();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void UpdateBlocklist_WithAutoUpdateEnabled_SavesAutoUpdateEnabled(bool autoUpdate)
    {
        Dictionary<string, object> savedUpdates = null!;
        this.configService.SaveConfigDictionary(Arg.Do<Dictionary<string, object>>(x => savedUpdates = x));

        var request = new BlocklistConfigRequest
        {
            AutoUpdateEnabled = autoUpdate,
        };

        var actionResult = this.controller.UpdateBlocklist(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        savedUpdates.Should().NotBeNull();
        savedUpdates!.Should().HaveCount(1);
        savedUpdates.Should().ContainKey("BlocklistAutoUpdateEnabled").WhoseValue.Should().Be(autoUpdate);
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
            AutoUpdateEnabled = true,
            AutoUpdateIntervalDays = 7,
        };

        var actionResult = this.controller.UpdateBlocklist(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        savedUpdates.Should().NotBeNull();
        savedUpdates!.Should().ContainKey("BlocklistEnabled").WhoseValue.Should().Be(true);
        savedUpdates.Should().ContainKey("BlocklistUrl").WhoseValue.Should().Be("https://rules.example.org/blacklist.p2p");
        savedUpdates.Should().ContainKey("BlocklistAutoUpdateEnabled").WhoseValue.Should().Be(true);
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
    public void UpdateBlocklist_WithAutoUpdateIntervalHours_SavesHoursWithoutDayConversion()
    {
        Dictionary<string, object> savedUpdates = null!;
        this.configService.SaveConfigDictionary(Arg.Do<Dictionary<string, object>>(x => savedUpdates = x));

        var request = new BlocklistConfigRequest
        {
            AutoUpdateIntervalHours = 6,
            AutoUpdateIntervalDays = 1,
        };

        var actionResult = this.controller.UpdateBlocklist(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        savedUpdates.Should().NotBeNull();
        savedUpdates!.Should().ContainKey("BlocklistUpdateIntervalHours").WhoseValue.Should().Be(6);
    }

    [Test]
    public void UpdateBlocklist_WhenSavingUrlWithCanonicalHours_PreservesSubDayInterval()
    {
        this.configService.BlocklistUpdateIntervalHours.Returns(6);
        Dictionary<string, object> savedUpdates = null!;
        this.configService.SaveConfigDictionary(Arg.Do<Dictionary<string, object>>(x => savedUpdates = x));

        var request = new BlocklistConfigRequest
        {
            Url = "https://rules.example.org/list.p2p",
            AutoUpdateIntervalHours = 6,
            AutoUpdateIntervalDays = 1,
        };

        var actionResult = this.controller.UpdateBlocklist(request);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        savedUpdates.Should().NotBeNull();
        savedUpdates!.Should().ContainKey("BlocklistUpdateIntervalHours").WhoseValue.Should().Be(6);
        savedUpdates.Should().ContainKey("BlocklistUrl").WhoseValue.Should().Be("https://rules.example.org/list.p2p");
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
        this.syncState.Received(1).RecordSuccessfulSync(Arg.Any<DateTime>());
    }

    [Test]
    public async Task SyncBlocklistAsync_WhenBlocklistDisabled_ReturnsSkippedResponseWithoutCallingUpdate()
    {
        var previousSync = new DateTime(2023, 1, 15, 8, 30, 0, DateTimeKind.Utc);
        this.syncState.LastSuccessfulSyncUtc.Returns(previousSync);
        this.configService.BlocklistEnabled.Returns(false);
        this.blocklistService.TotalRulesLoaded.Returns(350);
        this.blocklistService.Ipv4RulesLoaded.Returns(350);
        this.blocklistService.Ipv6RulesLoaded.Returns(0);

        var actionResult = await this.controller.SyncBlocklistAsync(CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var response = okResult!.Value as BlocklistSyncResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.Status.Should().Be("Skipped");
        response.Message.Should().Be("Blocklist update skipped: blocklist is disabled in configuration.");
        response.RuleCount.Should().Be(350);
        response.TotalRuleCount.Should().Be(350);
        response.LastUpdatedUtc.Should().Be(previousSync);

        await this.updateService.DidNotReceive().UpdateRulesAsync(Arg.Any<CancellationToken>());
        this.syncState.DidNotReceive().RecordSuccessfulSync(Arg.Any<DateTime>());
    }

    [Test]
    public async Task SyncBlocklistAsync_WhenUpdateReturnsZeroRules_ReturnsWarningResponseWithExistingRuleCount()
    {
        this.updateService.UpdateRulesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        this.blocklistService.TotalRulesLoaded.Returns(350);
        this.blocklistService.Ipv4RulesLoaded.Returns(350);
        this.blocklistService.Ipv6RulesLoaded.Returns(0);

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
        this.syncState.DidNotReceive().RecordSuccessfulSync(Arg.Any<DateTime>());
    }

    [Test]
    public async Task SyncBlocklistAsync_WhenUpdateServiceThrows_ReturnsErrorResponseWithExceptionMessage()
    {
        var previousSync = new DateTime(2022, 5, 10, 16, 45, 0, DateTimeKind.Utc);
        this.syncState.LastSuccessfulSyncUtc.Returns(previousSync);
        this.updateService.UpdateRulesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("Connection refused by peer.")));
        this.blocklistService.TotalRulesLoaded.Returns(800);
        this.blocklistService.Ipv4RulesLoaded.Returns(800);
        this.blocklistService.Ipv6RulesLoaded.Returns(0);

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
        response.LastUpdatedUtc.Should().Be(previousSync);
        this.syncState.DidNotReceive().RecordSuccessfulSync(Arg.Any<DateTime>());
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

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Notifications;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Notifications;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class NotificationControllerTest
{
    private INotificationRepository notificationRepository = null!;
    private IWebhookDispatcher webhookDispatcher = null!;
    private ICustomScriptService customScriptService = null!;
    private NotificationController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.notificationRepository = Substitute.For<INotificationRepository>();
        this.webhookDispatcher = Substitute.For<IWebhookDispatcher>();
        this.customScriptService = Substitute.For<ICustomScriptService>();

        this.controller = new NotificationController(
            this.notificationRepository,
            this.webhookDispatcher,
            this.customScriptService);
    }

    [Test]
    public async Task Test_WhenTelegramNotification_ResolvesTargetUrlAndDispatches()
    {
        var notif = new NotificationDefinition
        {
            Id = 1,
            Name = "Telegram Bot",
            Implementation = "Telegram",
            Settings = "{\"token\":\"12345:telegram-token\",\"chat_id\":\"chat-789\"}",
        };

        this.notificationRepository.Get(1).Returns(notif);
        this.webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var actionResult = await this.controller.Test(1);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "https://api.telegram.org/bot12345:telegram-token/sendMessage",
            Arg.Any<object>());
    }

    [Test]
    public async Task Test_WhenPushoverNotification_ResolvesTargetUrlAndDispatches()
    {
        var notif = new NotificationDefinition
        {
            Id = 2,
            Name = "Pushover Alert",
            Implementation = "Pushover",
            Settings = "{\"token\":\"app-token-abc\",\"user\":\"user-key-xyz\"}",
        };

        this.notificationRepository.Get(2).Returns(notif);
        this.webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var actionResult = await this.controller.Test(2);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "https://api.pushover.net/1/messages.json",
            Arg.Any<object>());
    }

    [Test]
    public async Task TestDirect_WhenDiscordNotificationWithJson_ResolvesTargetUrlAndDispatches()
    {
        var resource = new NotificationResource
        {
            Name = "Discord Webhook",
            Implementation = "Discord",
            Settings = "{\"url\":\"https://discord.com/api/webhooks/999/token\"}",
        };

        this.webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var actionResult = await this.controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "https://discord.com/api/webhooks/999/token",
            Arg.Any<object>());
    }

    [Test]
    public async Task Test_WhenEmailNotificationWithMissingRecipient_ReturnsFailureDiagnostic()
    {
        var notif = new NotificationDefinition
        {
            Id = 3,
            Name = "Email Alerts",
            Implementation = "Email",
            Settings = "{\"server\":\"smtp.example.com\",\"port\":587}",
        };

        this.notificationRepository.Get(3).Returns(notif);

        var actionResult = await this.controller.Test(3);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeFalse();
        testResult!.Message.Should().Contain("Recipient email address ('to') is required");

        await this.webhookDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<object>());
    }

    [Test]
    public async Task TestDirect_WhenEmailNotificationWithInvalidHost_ReturnsFailureDiagnostic()
    {
        var resource = new NotificationResource
        {
            Name = "Email Alerts Direct",
            Implementation = "Email",
            Settings = "{\"server\":\"invalid-nonexistent-smtp.example\",\"port\":587,\"to\":\"user@example.com\"}",
        };

        var actionResult = await this.controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeFalse();
        testResult!.Message.Should().StartWith("Failed to send email test notification:");

        await this.webhookDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<object>());
    }

    [Test]
    public async Task Test_WhenEmailNotificationWithInvalidHost_ReturnsFailureDiagnostic()
    {
        var notif = new NotificationDefinition
        {
            Id = 4,
            Name = "Email Alerts Invalid Host",
            Implementation = "Email",
            Settings = "{\"server\":\"invalid-nonexistent-smtp.example\",\"port\":587,\"to\":\"user@example.com\"}",
        };

        this.notificationRepository.Get(4).Returns(notif);

        var actionResult = await this.controller.Test(4);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeFalse();
        testResult!.Message.Should().StartWith("Failed to send email test notification:");

        await this.webhookDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<object>());
    }

    [Test]
    public async Task Test_WhenWebhookNotificationWithCustomHeaders_ForwardsHeadersToWebhookDispatcher()
    {
        var notif = new NotificationDefinition
        {
            Id = 10,
            Name = "Custom Webhook",
            Implementation = "Webhook",
            Settings = "{\"url\":\"https://example.com/webhook\",\"headers\":{\"Authorization\":\"Bearer test-secret-token\",\"X-Custom\":\"header-val\"}}",
        };

        this.notificationRepository.Get(10).Returns(notif);
        this.webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var actionResult = await this.controller.Test(10);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "https://example.com/webhook",
            Arg.Any<object>(),
            Arg.Is<string>(s => s.Contains("Bearer test-secret-token") && s.Contains("header-val")));
    }

    [Test]
    public async Task TestDirect_WhenNotificationWithCustomHeaders_ForwardsHeadersToWebhookDispatcher()
    {
        var resource = new NotificationResource
        {
            Name = "Direct Webhook",
            Implementation = "Webhook",
            Settings = "{\"url\":\"https://example.com/webhook/direct\",\"headers\":{\"X-Custom-Auth\":\"direct-secret-456\"}}",
        };

        this.webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var actionResult = await this.controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "https://example.com/webhook/direct",
            Arg.Any<object>(),
            Arg.Is<string>(s => s.Contains("direct-secret-456")));
    }

    [Test]
    public async Task Test_WhenNotificationWithNoCustomHeaders_ForwardsNullCustomHeadersToWebhookDispatcher()
    {
        var notif = new NotificationDefinition
        {
            Id = 11,
            Name = "Plain Webhook",
            Implementation = "Webhook",
            Settings = "{\"url\":\"https://example.com/plain\"}",
        };

        this.notificationRepository.Get(11).Returns(notif);
        this.webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var actionResult = await this.controller.Test(11);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "https://example.com/plain",
            Arg.Any<object>(),
            null);
    }

    [Test]
    public async Task Test_WhenSlackNotification_FormatsSlackPayloadWithTextAndDispatches()
    {
        var notif = new NotificationDefinition
        {
            Id = 12,
            Name = "Slack Channel",
            Implementation = "Slack",
            Settings = "{\"url\":\"https://hooks.slack.com/services/T00/B00/X00\"}",
        };

        this.notificationRepository.Get(12).Returns(notif);
        this.webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var actionResult = await this.controller.Test(12);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "https://hooks.slack.com/services/T00/B00/X00",
            Arg.Is<object>(p => p.GetType().GetProperty("text") != null &&
                                p.GetType().GetProperty("username") != null));
    }

    [Test]
    public async Task Test_WhenCustomScriptNotificationWithJsonSettings_ParsesPathAndArgumentsAndExecutes()
    {
        var notif = new NotificationDefinition
        {
            Id = 13,
            Name = "Custom Script Test",
            Implementation = "CustomScript",
            Settings = "{\"path\":\"/opt/scripts/notify.sh\",\"arguments\":\"--test --verbose\"}",
        };

        this.notificationRepository.Get(13).Returns(notif);
        this.customScriptService.ExecuteScriptAsync("/opt/scripts/notify.sh", null, "Test", "--test --verbose")
            .Returns(Task.FromResult(true));

        var actionResult = await this.controller.Test(13);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();
        testResult.Message.Should().Be("Script executed successfully.");

        await this.customScriptService.Received(1).ExecuteScriptAsync(
            "/opt/scripts/notify.sh",
            null,
            "Test",
            "--test --verbose");
    }

    [Test]
    public async Task TestDirect_WhenCustomScriptNotificationWithPlainPath_ExecutesWithNullArguments()
    {
        var resource = new NotificationResource
        {
            Name = "Custom Script Direct",
            Implementation = "CustomScript",
            Settings = "/opt/scripts/notify.sh",
        };

        this.customScriptService.ExecuteScriptAsync("/opt/scripts/notify.sh", null, "Test", null)
            .Returns(Task.FromResult(true));

        var actionResult = await this.controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();

        await this.customScriptService.Received(1).ExecuteScriptAsync(
            "/opt/scripts/notify.sh",
            null,
            "Test",
            null);
    }

    [Test]
    public async Task Test_WhenCustomScriptNotificationExecutionFails_ReturnsFailureTestResult()
    {
        var notif = new NotificationDefinition
        {
            Id = 14,
            Name = "Failing Script",
            Implementation = "CustomScript",
            Settings = "{\"path\":\"/opt/scripts/fail.sh\"}",
        };

        this.notificationRepository.Get(14).Returns(notif);
        this.customScriptService.ExecuteScriptAsync("/opt/scripts/fail.sh", null, "Test", null)
            .Returns(Task.FromResult(false));

        var actionResult = await this.controller.Test(14);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeFalse();
        testResult.Message.Should().Be("Script execution failed.");
    }
}

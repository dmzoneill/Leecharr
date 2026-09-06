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
    public async Task Test_WhenEmailNotification_DispatchesEmailAndReturnsSuccess()
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
        testResult!.Success.Should().BeTrue();
        testResult!.Message.Should().Be("Email test notification sent successfully.");

        await this.webhookDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<object>());
    }

    [Test]
    public async Task TestDirect_WhenEmailNotification_DispatchesEmailAndReturnsSuccess()
    {
        var resource = new NotificationResource
        {
            Name = "Email Alerts Direct",
            Implementation = "Email",
            Settings = "{\"server\":\"smtp.example.com\",\"port\":587}",
        };

        var actionResult = await this.controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var testResult = okResult!.Value as NotificationTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();
        testResult!.Message.Should().Be("Email test notification sent successfully.");

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
    public void GetById_ReturnsMaskedSecrets_InSettingsJson()
    {
        var notif = new NotificationDefinition
        {
            Id = 42,
            Name = "Telegram Bot",
            Implementation = "Telegram",
            Settings = "{\"token\":\"12345:telegram-secret\",\"chat_id\":\"chat-789\"}",
        };

        this.notificationRepository.Get(42).Returns(notif);

        var actionResult = this.controller.GetById(42);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var resource = okResult!.Value as NotificationResource;
        resource.Should().NotBeNull();
        resource!.Settings.Should().Contain("\"token\":\"********\"");
        resource.Settings.Should().Contain("\"chat_id\":\"chat-789\"");
        resource.Settings.Should().NotContain("telegram-secret");
    }

    [Test]
    public void Update_PreservesStoredSecrets_WhenMaskedSettingsProvided()
    {
        var existing = new NotificationDefinition
        {
            Id = 42,
            Name = "Telegram Bot",
            Implementation = "Telegram",
            Settings = "{\"token\":\"12345:telegram-secret\",\"chat_id\":\"chat-789\"}",
        };

        this.notificationRepository.Get(42).Returns(existing);

        var updateResource = new NotificationResource
        {
            Id = 42,
            Name = "Updated Telegram Bot",
            Implementation = "Telegram",
            Settings = "{\"token\":\"********\",\"chat_id\":\"chat-999\"}",
        };

        var actionResult = this.controller.Update(42, updateResource);
        var okResult = actionResult.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        this.notificationRepository.Received(1).Update(Arg.Is<NotificationDefinition>(n =>
            n.Id == 42 &&
            n.Settings.Contains("12345:telegram-secret") &&
            n.Settings.Contains("chat-999")));
    }
}

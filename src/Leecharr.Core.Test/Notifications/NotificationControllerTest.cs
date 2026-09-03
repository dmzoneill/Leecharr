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
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class NotificationEventHandlerTest
{
    private INotificationRepository notificationRepository = null!;
    private IWebhookDispatcher webhookDispatcher = null!;
    private ICustomScriptService customScriptService = null!;
    private IConfigService configService = null!;
    private NotificationEventHandler handler = null!;

    [SetUp]
    public void SetUp()
    {
        this.notificationRepository = Substitute.For<INotificationRepository>();
        this.webhookDispatcher = Substitute.For<IWebhookDispatcher>();
        this.customScriptService = Substitute.For<ICustomScriptService>();
        this.configService = Substitute.For<IConfigService>();

        this.handler = new NotificationEventHandler(
            this.notificationRepository,
            this.webhookDispatcher,
            this.customScriptService,
            this.configService);
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenNewStatusIsStalled_DispatchesHealthIssueNotification()
    {
        var notification = new NotificationDefinition
        {
            Id = 1,
            Name = "Webhook 1",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/webhook",
            OnHealthIssue = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 10,
            Name = "Stalled ISO",
            Status = TorrentStatus.Stalled,
            ErrorMessage = "Tracker offline",
        };

        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Downloading,
            NewStatus = TorrentStatus.Stalled,
        });

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://test/webhook",
            Arg.Any<object>());
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenOldStatusIsStalled_DispatchesHealthRestoredNotification()
    {
        var notification = new NotificationDefinition
        {
            Id = 2,
            Name = "Webhook 2",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/webhook",
            OnHealthRestored = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 11,
            Name = "Recovered ISO",
            Status = TorrentStatus.Downloading,
        };

        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Stalled,
            NewStatus = TorrentStatus.Downloading,
        });

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://test/webhook",
            Arg.Any<object>());
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WhenNotResolved_DispatchesHealthIssueNotification()
    {
        var notification = new NotificationDefinition
        {
            Id = 3,
            Name = "Webhook 3",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/webhook",
            OnHealthIssue = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 12,
            Name = "Tracker Failed Torrent",
            Status = TorrentStatus.Stalled,
            ErrorMessage = "Tracker error: Connection refused",
        };

        this.handler.Handle(new HealthIssueEvent(torrent, "Tracker", "Tracker error: Connection refused", isResolved: false));

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://test/webhook",
            Arg.Any<object>());
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WhenResolved_DispatchesHealthRestoredNotification()
    {
        var notification = new NotificationDefinition
        {
            Id = 4,
            Name = "Webhook 4",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/webhook",
            OnHealthRestored = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 13,
            Name = "Tracker Restored Torrent",
            Status = TorrentStatus.Downloading,
        };

        this.handler.Handle(new HealthIssueEvent(torrent, "Tracker", "Tracker recovered", isResolved: true));

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://test/webhook",
            Arg.Any<object>());
    }

    [Test]
    public void ResolveTargetUrl_Telegram_WithTokenInJson_ResolvesToTelegramApiUrl()
    {
        var settings = "{\"token\":\"12345:abcdef\",\"chat_id\":\"987654\"}";
        var url = NotificationEventHandler.ResolveTargetUrl("Telegram", settings);

        url.Should().Be("https://api.telegram.org/bot12345:abcdef/sendMessage");
    }

    [Test]
    public void ResolveTargetUrl_Telegram_WithBotTokenInJson_ResolvesToTelegramApiUrl()
    {
        var settings = "{\"botToken\":\"99999:xyzuvw\",\"chatId\":\"112233\"}";
        var url = NotificationEventHandler.ResolveTargetUrl("Telegram", settings);

        url.Should().Be("https://api.telegram.org/bot99999:xyzuvw/sendMessage");
    }

    [Test]
    public void ResolveTargetUrl_Pushover_ResolvesToPushoverApiUrl()
    {
        var settings = "{\"token\":\"pushover_token_123\",\"user\":\"pushover_user_456\"}";
        var url = NotificationEventHandler.ResolveTargetUrl("Pushover", settings);

        url.Should().Be("https://api.pushover.net/1/messages.json");
    }

    [Test]
    public void ResolveTargetUrl_Discord_WithUrlInJson_ExtractsUrl()
    {
        var settings = "{\"url\":\"https://discord.com/api/webhooks/123/xyz\"}";
        var url = NotificationEventHandler.ResolveTargetUrl("Discord", settings);

        url.Should().Be("https://discord.com/api/webhooks/123/xyz");
    }

    [Test]
    public void ResolveTargetUrl_Webhook_WithRawUrlString_ReturnsRawUrl()
    {
        var settings = "https://example.com/custom/webhook";
        var url = NotificationEventHandler.ResolveTargetUrl("Webhook", settings);

        url.Should().Be("https://example.com/custom/webhook");
    }

    [Test]
    public void ResolveTargetUrl_Gotify_WithUrlInJson_ExtractsUrl()
    {
        var settings = "{\"url\":\"https://gotify.example.com/message\",\"token\":\"gotify-token\"}";
        var url = NotificationEventHandler.ResolveTargetUrl("Gotify", settings);

        url.Should().Be("https://gotify.example.com/message");
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenTelegramNotification_DispatchesToTelegramApiUrl()
    {
        var notification = new NotificationDefinition
        {
            Id = 5,
            Name = "Telegram Alert",
            Implementation = "Telegram",
            ConfigContract = "TelegramSettings",
            Settings = "{\"token\":\"bot-secret-123\",\"chat_id\":\"chat-456\"}",
            OnHealthIssue = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 20,
            Name = "Stalled Linux ISO",
            Status = TorrentStatus.Stalled,
            ErrorMessage = "Connection timeout",
        };

        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Downloading,
            NewStatus = TorrentStatus.Stalled,
        });

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "https://api.telegram.org/botbot-secret-123/sendMessage",
            Arg.Any<object>());
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenPushoverNotification_DispatchesToPushoverApiUrl()
    {
        var notification = new NotificationDefinition
        {
            Id = 6,
            Name = "Pushover Alert",
            Implementation = "Pushover",
            ConfigContract = "PushoverSettings",
            Settings = "{\"token\":\"pushover-app-token\",\"user\":\"pushover-user-key\"}",
            OnHealthIssue = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 21,
            Name = "Stalled Video",
            Status = TorrentStatus.Stalled,
            ErrorMessage = "Tracker unreachable",
        };

        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Downloading,
            NewStatus = TorrentStatus.Stalled,
        });

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "https://api.pushover.net/1/messages.json",
            Arg.Any<object>());
    }

    [Test]
    public void SendEmailNotification_WithNullOrEmptySettings_DoesNotThrow()
    {
        var act1 = () => NotificationEventHandler.SendEmailNotification(null, "Test", null, null, new { Message = "Test" });
        var act2 = () => NotificationEventHandler.SendEmailNotification(string.Empty, "Test", null, null, new { Message = "Test" });

        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Test]
    public void SendEmailNotification_WithoutRecipient_DoesNotThrow()
    {
        var settings = "{\"server\":\"smtp.example.com\",\"port\":587}";
        var act = () => NotificationEventHandler.SendEmailNotification(settings, "Test", null, null, new { Message = "Test" });

        act.Should().NotThrow();
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenStoppedAndCompleted_DoesNotFireSeedGoalReached()
    {
        var notification = new NotificationDefinition
        {
            Id = 7,
            Name = "Seed Goal Notification",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/seedgoal",
            OnSeedGoalReached = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });
        this.configService.OnSeedGoalReachedScript.Returns("/scripts/seed_goal.sh");

        var torrent = new Torrent
        {
            Id = 30,
            Name = "Manually Paused Seeding Torrent",
            Status = TorrentStatus.Stopped,
            Progress = 1.0,
            Ratio = 0.5,
        };

        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Seeding,
            NewStatus = TorrentStatus.Stopped,
        });

        await Task.Delay(100);

        await this.webhookDispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<object>());

        await this.customScriptService.DidNotReceive().ExecuteScriptAsync(
            Arg.Any<string>(),
            Arg.Any<Torrent>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task Handle_TorrentSeedGoalReachedEvent_FiresSeedGoalReachedNotificationAndScript()
    {
        var notification = new NotificationDefinition
        {
            Id = 8,
            Name = "Seed Goal Webhook",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/seedgoal",
            OnSeedGoalReached = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });
        this.configService.OnSeedGoalReachedScript.Returns("/scripts/seed_goal.sh");

        var torrent = new Torrent
        {
            Id = 31,
            Name = "Seed Goal Torrent",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Ratio = 2.1,
            TargetRatio = 2.0,
        };

        this.handler.Handle(new TorrentSeedGoalReachedEvent(torrent));

        await Task.Delay(150);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://test/seedgoal",
            Arg.Any<object>());

        await this.customScriptService.Received().ExecuteScriptAsync(
            "/scripts/seed_goal.sh",
            torrent,
            "OnSeedGoalReached");
    }

    [Test]
    public void Handle_TorrentSeedGoalReachedEvent_WhenMessageOrTorrentIsNull_DoesNotThrow()
    {
        var act1 = () => this.handler.Handle((TorrentSeedGoalReachedEvent)null!);
        var act2 = () => this.handler.Handle(new TorrentSeedGoalReachedEvent(null!));

        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenCustomHeadersConfigured_ForwardsHeadersToWebhookDispatcher()
    {
        var notification = new NotificationDefinition
        {
            Id = 50,
            Name = "Webhook with Headers",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "{\"url\":\"http://test/webhook\",\"headers\":{\"Authorization\":\"Bearer token-123\",\"X-Tracking\":\"track-abc\"}}",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 51,
            Name = "Ubuntu ISO",
            Status = TorrentStatus.Downloading,
        };

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://test/webhook",
            Arg.Any<object>(),
            Arg.Is<string>(h => h.Contains("Bearer token-123") && h.Contains("track-abc")));
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WhenCustomHeadersConfigured_ForwardsHeadersToWebhookDispatcher()
    {
        var notification = new NotificationDefinition
        {
            Id = 52,
            Name = "Health Alert Webhook",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "{\"url\":\"http://test/webhook\",\"headers\":{\"X-Health-Alert\":\"warning-123\"}}",
            OnHealthIssue = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 53,
            Name = "Tracker Error Torrent",
            Status = TorrentStatus.Stalled,
        };

        this.handler.Handle(new HealthIssueEvent(torrent, "Tracker", "Connection reset", isResolved: false));

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://test/webhook",
            Arg.Any<object>(),
            Arg.Is<string>(h => h.Contains("warning-123")));
    }

    [Test]
    public async Task Handle_ApplicationUpdatedEvent_WhenCustomHeadersConfigured_ForwardsHeadersToWebhookDispatcher()
    {
        var notification = new NotificationDefinition
        {
            Id = 54,
            Name = "Update Webhook",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "{\"url\":\"http://test/webhook\",\"headers\":{\"X-App-Update\":\"ver-2\"}}",
            OnApplicationUpdate = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        this.handler.Handle(new ApplicationUpdatedEvent { PreviousVersion = "1.0.0", NewVersion = "1.1.0" });

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://test/webhook",
            Arg.Any<object>(),
            Arg.Is<string>(h => h.Contains("ver-2")));
    }

    [Test]
    public async Task Handle_VpnKillSwitchTriggeredEvent_WhenCustomHeadersConfigured_ForwardsHeadersToWebhookDispatcher()
    {
        var notification = new NotificationDefinition
        {
            Id = 55,
            Name = "VPN Kill Switch Webhook",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "{\"url\":\"http://test/webhook\",\"headers\":{\"X-KillSwitch\":\"vpn-alert\"}}",
            OnHealthIssue = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        this.handler.Handle(new VpnKillSwitchTriggeredEvent("wg0"));

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://test/webhook",
            Arg.Any<object>(),
            Arg.Is<string>(h => h.Contains("vpn-alert")));
    }

    [Test]
    public void ResolveCustomHeaders_VariousFormats_ExtractsExpectedHeaders()
    {
        // JSON object
        NotificationEventHandler.ResolveCustomHeaders("{\"url\":\"http://test\",\"headers\":{\"Authorization\":\"Bearer abc\"}}")
            .Should().Contain("Bearer abc");

        // Stringified JSON
        NotificationEventHandler.ResolveCustomHeaders("{\"url\":\"http://test\",\"headers\":\"{\\\"Authorization\\\":\\\"Bearer abc\\\"}\"}")
            .Should().Contain("Bearer abc");

        // CustomHeaders property
        NotificationEventHandler.ResolveCustomHeaders("{\"url\":\"http://test\",\"customHeaders\":{\"X-Key\":\"val\"}}")
            .Should().Contain("val");

        // Key-value string in headers
        NotificationEventHandler.ResolveCustomHeaders("{\"url\":\"http://test\",\"headers\":\"X-Custom: header-value\"}")
            .Should().Be("X-Custom: header-value");

        // Query string format
        NotificationEventHandler.ResolveCustomHeaders("url=http://test&headers=%7B%22Key%22%3A%22Val%22%7D")
            .Should().Contain("Val");

        // Missing headers or empty
        NotificationEventHandler.ResolveCustomHeaders("{\"url\":\"http://test\"}").Should().BeNull();
        NotificationEventHandler.ResolveCustomHeaders("{\"url\":\"http://test\",\"headers\":{}}").Should().BeNull();
        NotificationEventHandler.ResolveCustomHeaders("http://test/plain").Should().BeNull();
        NotificationEventHandler.ResolveCustomHeaders(string.Empty).Should().BeNull();
        NotificationEventHandler.ResolveCustomHeaders(null).Should().BeNull();
    }

    [TestCase("Movie.Name.(2024).1080p", @"Movie.Name.\(2024\).1080p")]
    [TestCase("Test_Name*With[Brackets]And`Code`", @"Test\_Name\*With\[Brackets\]And\`Code\`")]
    [TestCase(@"Spoiler|Bar and >Quote and ~Strikethrough~ and \Path", @"Spoiler\|Bar and \>Quote and \~Strikethrough\~ and \\Path")]
    [TestCase(null, "")]
    [TestCase("", "")]
    public void EscapeMarkdown_EscapesAllMetacharacters(string input, string expected)
    {
        var result = NotificationEventHandler.EscapeMarkdown(input);
        result.Should().Be(expected);
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenTelegramNotificationWithParenthesesInName_EscapesParenthesesProperly()
    {
        var notification = new NotificationDefinition
        {
            Id = 60,
            Name = "Telegram Alert",
            Implementation = "Telegram",
            ConfigContract = "TelegramSettings",
            Settings = "{\"token\":\"bot-token-abc\",\"chat_id\":\"chat-123\"}",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 61,
            Name = "Movie.Title.(2024).[1080p].x264-GROUP",
            Category = "Movies (HD)",
            Status = TorrentStatus.Downloading,
        };

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "https://api.telegram.org/botbot-token-abc/sendMessage",
            Arg.Is<object>(payload =>
                payload != null &&
                ((Dictionary<string, object>)payload)["text"].ToString()!.Contains(@"Movie.Title.\(2024\).\[1080p\].x264-GROUP") &&
                ((Dictionary<string, object>)payload)["text"].ToString()!.Contains(@"Movies \(HD\)") &&
                ((Dictionary<string, object>)payload)["parse_mode"].ToString() == "Markdown"));
    }

    [Test]
    public void ResolveTargetUrl_Apprise_NormalizesUrlToIncludeNotifyEndpoint()
    {
        NotificationEventHandler.ResolveTargetUrl("Apprise", "http://apprise-server:8000")
            .Should().Be("http://apprise-server:8000/notify");

        NotificationEventHandler.ResolveTargetUrl("Apprise", "http://apprise-server:8000/")
            .Should().Be("http://apprise-server:8000/notify");

        NotificationEventHandler.ResolveTargetUrl("Apprise", "http://apprise-server:8000/notify")
            .Should().Be("http://apprise-server:8000/notify");

        NotificationEventHandler.ResolveTargetUrl("Apprise", "{\"url\":\"http://apprise-server:8000\"}")
            .Should().Be("http://apprise-server:8000/notify");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenAppriseNotification_DispatchesValidBodyPayload()
    {
        var notification = new NotificationDefinition
        {
            Id = 70,
            Name = "Apprise Alert",
            Implementation = "Apprise",
            ConfigContract = "AppriseSettings",
            Settings = "{\"url\":\"http://apprise-server:8000\"}",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 71,
            Name = "Apprise Linux ISO",
            Category = "Linux",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 1000 * 1024 * 1024L,
        };

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await Task.Delay(100);

        await this.webhookDispatcher.Received().DispatchAsync(
            "http://apprise-server:8000/notify",
            Arg.Is<object>(payload =>
                payload != null &&
                payload.GetType().GetProperty("title") != null &&
                payload.GetType().GetProperty("body") != null &&
                payload.GetType().GetProperty("type") != null &&
                (string)payload.GetType().GetProperty("title")!.GetValue(payload)! == "Leecharr: OnGrab" &&
                ((string)payload.GetType().GetProperty("body")!.GetValue(payload)!).Contains("Apprise Linux ISO")));
    }
}

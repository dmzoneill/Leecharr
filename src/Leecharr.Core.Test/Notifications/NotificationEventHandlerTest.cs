// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
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
    private IMediaEnrichmentService mediaEnrichmentService = null!;
    private NotificationEventHandler handler = null!;
    private TaskCompletionSource<bool> webhookTcs = null!;
    private TaskCompletionSource<bool> scriptTcs = null!;

    [SetUp]
    public void SetUp()
    {
        this.webhookTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.scriptTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        this.notificationRepository = Substitute.For<INotificationRepository>();
        this.webhookDispatcher = Substitute.For<IWebhookDispatcher>();
        this.webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                this.webhookTcs.TrySetResult(true);
                return Task.FromResult(true);
            });

        this.customScriptService = Substitute.For<ICustomScriptService>();
        this.customScriptService.ExecuteScriptAsync(Arg.Any<string>(), Arg.Any<Torrent>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                this.scriptTcs.TrySetResult(true);
                return Task.FromResult(true);
            });

        this.configService = Substitute.For<IConfigService>();
        this.mediaEnrichmentService = Substitute.For<IMediaEnrichmentService>();

        this.handler = new NotificationEventHandler(
            this.notificationRepository,
            this.webhookDispatcher,
            this.customScriptService,
            this.configService,
            this.mediaEnrichmentService);
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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received().DispatchAsync(
            "https://api.pushover.net/1/messages.json",
            Arg.Any<object>());
    }

    [Test]
    public void SendEmailNotification_WithNullOrEmptySettings_ThrowsArgumentException()
    {
        var act1 = () => NotificationEventHandler.SendEmailNotification(null, "Test", null, null, new { Message = "Test" });
        var act2 = () => NotificationEventHandler.SendEmailNotification(string.Empty, "Test", null, null, new { Message = "Test" });

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    [Test]
    public void SendEmailNotification_WithoutRecipient_ThrowsInvalidOperationException()
    {
        var settings = "{\"server\":\"smtp.example.com\",\"port\":587}";
        var act = () => NotificationEventHandler.SendEmailNotification(settings, "Test", null, null, new { Message = "Test" });

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void SendEmailNotification_WithValidSettingsAndCustomSender_ExecutesSuccessfully()
    {
        var settings = "{\"server\":\"smtp.example.com\",\"port\":587,\"to\":\"user@example.com\",\"from\":\"bot@example.com\"}";
        var senderCalled = false;
        NotificationEventHandler.SendEmailNotification(settings, "Test", null, null, new { Message = "Test" }, (client, mail) =>
        {
            senderCalled = true;
            client.Host.Should().Be("smtp.example.com");
            client.Port.Should().Be(587);
            mail.To[0].Address.Should().Be("user@example.com");
            mail.From!.Address.Should().Be("bot@example.com");
        });

        senderCalled.Should().BeTrue();
    }

    [Test]
    public void SendEmailNotification_WhenSmtpSenderThrows_PropagatesException()
    {
        var settings = "{\"server\":\"smtp.example.com\",\"port\":587,\"to\":\"user@example.com\"}";
        var act = () => NotificationEventHandler.SendEmailNotification(settings, "Test", null, null, new { Message = "Test" }, (client, mail) =>
        {
            throw new System.Net.Mail.SmtpException("Connection refused");
        });

        act.Should().Throw<System.Net.Mail.SmtpException>().WithMessage("Connection refused");
    }

    [Test]
    public void SendEmailNotification_WithQueryStringSettings_ParsesAndExecutesSuccessfully()
    {
        var settings = "server=smtp.mailgun.org&port=465&ssl=true&user=myuser&pass=mypass&from=alerts%40leecharr.local&to=admin%40example.com";
        var senderCalled = false;
        NotificationEventHandler.SendEmailNotification(settings, "OnDownloadComplete", null, null, new { Message = "Test Download Complete" }, (client, mail) =>
        {
            senderCalled = true;
            client.Host.Should().Be("smtp.mailgun.org");
            client.Port.Should().Be(465);
            client.EnableSsl.Should().BeTrue();
            mail.To[0].Address.Should().Be("admin@example.com");
            mail.From!.Address.Should().Be("alerts@leecharr.local");
        });

        senderCalled.Should().BeTrue();
    }

    [Test]
    public void SendEmailNotification_WithQueryStringSettings_WithoutRecipient_ThrowsInvalidOperationException()
    {
        var settings = "server=smtp.example.com&port=587&ssl=false";
        var act = () => NotificationEventHandler.SendEmailNotification(settings, "Test", null, null, new { Message = "Test" });

        act.Should().Throw<InvalidOperationException>();
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

        await Task.Delay(10);

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

        await Task.WhenAll(this.webhookTcs.Task, this.scriptTcs.Task).WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

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

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenSlackNotification_DispatchesFormattedSlackPayload()
    {
        var notification = new NotificationDefinition
        {
            Id = 80,
            Name = "Slack Alert",
            Implementation = "Slack",
            ConfigContract = "SlackSettings",
            Settings = "{\"url\":\"https://hooks.slack.com/services/T00/B00/X00\"}",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 81,
            Name = "Slack Test Torrent",
            Category = "Movies",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 500 * 1024 * 1024L,
        };

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received().DispatchAsync(
            "https://hooks.slack.com/services/T00/B00/X00",
            Arg.Is<object>(payload =>
                payload != null &&
                payload.GetType().GetProperty("text") != null &&
                ((string)payload.GetType().GetProperty("text")!.GetValue(payload)!).Contains("Slack Test Torrent")));
    }

    [Test]
    public void BuildProviderPayload_Discord_WithoutMediaMetadata_ContainsTorrentDetailsAndStatus()
    {
        var torrent = new Torrent
        {
            Id = 90,
            Name = "Inception.2010.1080p",
            Category = "Movies",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 2048 * 1024 * 1024L,
        };

        var payload = NotificationEventHandler.BuildProviderPayload("Discord", "OnGrab", torrent, null, null);

        var embeds = (object[])payload.GetType().GetProperty("embeds")!.GetValue(payload)!;
        embeds.Should().NotBeEmpty();
        var embed = embeds[0];
        var title = (string)embed.GetType().GetProperty("title")!.GetValue(embed)!;
        var description = (string)embed.GetType().GetProperty("description")!.GetValue(embed)!;

        title.Should().Be("[OnGrab] Inception.2010.1080p");
        description.Should().Be("Category: Movies | Status: Downloading | Progress: 50.0% | Size: 2048.00 MB");
    }

    [Test]
    public void BuildProviderPayload_Discord_WithMediaMetadataOverview_PreservesTorrentDetailsAndAppendsOverview()
    {
        var torrent = new Torrent
        {
            Id = 91,
            Name = "Interstellar.2014.2160p",
            Category = "Movies (4K)",
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            TotalSize = 15360 * 1024 * 1024L,
        };

        var meta = new TorrentMediaMetadata
        {
            TorrentId = 91,
            Title = "Interstellar",
            Year = 2014,
            Overview = "A team of explorers travel through a wormhole in space in an attempt to ensure humanity's survival.",
        };

        var payload = NotificationEventHandler.BuildProviderPayload("Discord", "OnDownloadComplete", torrent, meta, null);

        var embeds = (object[])payload.GetType().GetProperty("embeds")!.GetValue(payload)!;
        embeds.Should().NotBeEmpty();
        var embed = embeds[0];
        var title = (string)embed.GetType().GetProperty("title")!.GetValue(embed)!;
        var description = (string)embed.GetType().GetProperty("description")!.GetValue(embed)!;

        title.Should().Be("[OnDownloadComplete] Interstellar.2014.2160p");
        description.Should().StartWith("Category: Movies (4K) | Status: Seeding | Progress: 100.0% | Size: 15360.00 MB\n\n");
        description.Should().Contain("A team of explorers travel through a wormhole in space in an attempt to ensure humanity's survival.");
    }

    [Test]
    public void BuildProviderPayload_Discord_WithEmptyMediaMetadataOverview_ContainsOnlyTorrentDetails()
    {
        var torrent = new Torrent
        {
            Id = 92,
            Name = "Test.Torrent.2024",
            Category = "Default",
            Status = TorrentStatus.Downloading,
            Progress = 0.25,
            TotalSize = 1024 * 1024 * 1024L,
        };

        var meta = new TorrentMediaMetadata
        {
            TorrentId = 92,
            Title = "Test Torrent",
            Overview = "   ",
        };

        var payload = NotificationEventHandler.BuildProviderPayload("Discord", "OnGrab", torrent, meta, null);

        var embeds = (object[])payload.GetType().GetProperty("embeds")!.GetValue(payload)!;
        var embed = embeds[0];
        var description = (string)embed.GetType().GetProperty("description")!.GetValue(embed)!;

        description.Should().Be("Category: Default | Status: Downloading | Progress: 25.0% | Size: 1024.00 MB");
    }

    [Test]
    public void BuildProviderPayload_Discord_WhenTorrentNull_UsesGenericPayloadMessageAndOverview()
    {
        var meta = new TorrentMediaMetadata
        {
            Overview = "System overview details.",
        };

        var genericPayload = new { Message = "Disk usage is 95%" };

        var payload = NotificationEventHandler.BuildProviderPayload("Discord", "OnHealthIssue", null, meta, genericPayload);

        var embeds = (object[])payload.GetType().GetProperty("embeds")!.GetValue(payload)!;
        var embed = embeds[0];
        var title = (string)embed.GetType().GetProperty("title")!.GetValue(embed)!;
        var description = (string)embed.GetType().GetProperty("description")!.GetValue(embed)!;

        title.Should().Be("[OnHealthIssue] Disk usage is 95%");
        description.Should().Be("Disk usage is 95%\n\nSystem overview details.");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenDiscordNotificationWithMediaEnrichment_DispatchesCombinedDetailsAndOverview()
    {
        var notification = new NotificationDefinition
        {
            Id = 95,
            Name = "Discord Channel",
            Implementation = "Discord",
            ConfigContract = "DiscordSettings",
            Settings = "{\"url\":\"https://discord.com/api/webhooks/123/xyz\"}",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 96,
            Name = "Dune.Part.Two.2024.1080p",
            Category = "Movies",
            Status = TorrentStatus.Downloading,
            Progress = 0.1,
            TotalSize = 4096 * 1024 * 1024L,
        };

        var meta = new TorrentMediaMetadata
        {
            TorrentId = 96,
            Title = "Dune: Part Two",
            Year = 2024,
            Overview = "Paul Atreides unites with Chani and the Fremen while seeking revenge.",
        };

        this.mediaEnrichmentService.GetMetadata(torrent.Id).Returns(meta);

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received().DispatchAsync(
            "https://discord.com/api/webhooks/123/xyz",
            Arg.Is<object>(payload =>
                payload != null &&
                payload.GetType().GetProperty("embeds") != null &&
                ((string)((object[])payload.GetType().GetProperty("embeds")!.GetValue(payload)!)[0].GetType().GetProperty("description")!.GetValue(((object[])payload.GetType().GetProperty("embeds")!.GetValue(payload)!)[0])!)
                    .Contains("Category: Movies | Status: Downloading | Progress: 10.0% | Size: 4096.00 MB") &&
                ((string)((object[])payload.GetType().GetProperty("embeds")!.GetValue(payload)!)[0].GetType().GetProperty("description")!.GetValue(((object[])payload.GetType().GetProperty("embeds")!.GetValue(payload)!)[0])!)
                    .Contains("Paul Atreides unites with Chani and the Fremen while seeking revenge.")));
    }

    [Test]
    public void SendEmailNotification_WithValidSettingsAndMediaMetadata_DoesNotThrow()
    {
        var torrent = new Torrent
        {
            Id = 97,
            Name = "Oppenheimer.2023.2160p",
            Category = "Movies",
            Status = TorrentStatus.Downloading,
            Progress = 0.8,
            TotalSize = 8192 * 1024 * 1024L,
        };

        var meta = new TorrentMediaMetadata
        {
            TorrentId = 97,
            Title = "Oppenheimer",
            Overview = "The story of American scientist J. Robert Oppenheimer and his role in the Manhattan Project.",
        };

        var settings = "{\"server\":\"127.0.0.1\",\"port\":2525,\"to\":\"user@example.com\",\"from\":\"leecharr@example.com\"}";

        var act = () => NotificationEventHandler.SendEmailNotification(settings, "OnGrab", torrent, meta, null, (client, mail) =>
        {
            mail.Body.Should().Contain("Oppenheimer");
            mail.Subject.Should().Contain("Oppenheimer.2023.2160p");
        });

        act.Should().NotThrow();
    }

    [Test]
    public async Task Handle_ArchiveExtractionFailedEvent_DispatchesHealthIssueAndManualInteractionRequiredNotifications()
    {
        var healthNotification = new NotificationDefinition
        {
            Id = 98,
            Name = "Health Issue Webhook",
            Implementation = "Webhook",
            Settings = "http://test/webhook-health",
            OnHealthIssue = true,
            OnManualInteractionRequired = false,
        };

        var manualNotification = new NotificationDefinition
        {
            Id = 99,
            Name = "Manual Interaction Webhook",
            Implementation = "Webhook",
            Settings = "http://test/webhook-manual",
            OnHealthIssue = false,
            OnManualInteractionRequired = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { healthNotification, manualNotification });

        var multiWebhookTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchCount = 0;
        this.webhookDispatcher.DispatchAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(ci =>
            {
                if (System.Threading.Interlocked.Increment(ref dispatchCount) >= 2)
                {
                    multiWebhookTcs.TrySetResult(true);
                }

                return Task.CompletedTask;
            });

        var torrent = new Torrent
        {
            Id = 55,
            Name = "Extraction.Failed.Movie",
            Status = TorrentStatus.Downloading,
            SavePath = "/downloads/Extraction.Failed.Movie",
        };

        this.handler.Handle(new ArchiveExtractionFailedEvent
        {
            Torrent = torrent,
            ArchivePath = "/downloads/Extraction.Failed.Movie/movie.rar",
            DestinationDirectory = "/downloads/Extraction.Failed.Movie",
            ErrorMessage = "Corrupt archive volume",
        });

        await multiWebhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "http://test/webhook-health",
            Arg.Any<object>());

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "http://test/webhook-manual",
            Arg.Any<object>());
    }

    [Test]
    public async Task Handle_TorrentDownloadCompletedEvent_DispatchesNestedWebhookPayloadSchema()
    {
        var notification = new NotificationDefinition
        {
            Id = 101,
            Name = "Nested Webhook",
            Implementation = "Webhook",
            Settings = "http://test/webhook-nested",
            OnDownloadComplete = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 42,
            Name = "House.of.the.Dragon.S02E08.2160p.HMAX.WEB-DL.DDP5.1.Atmos.DV.HDR10.H.265-FLUX",
            InfoHash = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
            Category = "tv",
            SavePath = "/downloads/tv/House.of.the.Dragon.S02E08.2160p.HMAX.WEB-DL.DDP5.1.Atmos.DV.HDR10.H.265-FLUX",
            TotalSize = 8589934592,
            Downloaded = 8589934592,
            Uploaded = 4294967296,
            Ratio = 0.5,
            Progress = 1.0,
            Status = TorrentStatus.Seeding,
            DownloadSpeed = 0,
            UploadSpeed = 1048576,
            Eta = 0,
            Seeders = 50,
            Leechers = 10,
        };

        var meta = new TorrentMediaMetadata
        {
            TorrentId = 42,
            Title = "House of the Dragon",
            Year = 2022,
            Overview = "As the season reaches its climax...",
            PosterUrl = "http://localhost:7889/api/v1/media/artwork/42/poster",
            BackdropUrl = "http://localhost:7889/api/v1/media/artwork/42/backdrop",
            Rating = 8.5,
            ImdbId = "tt11198330",
            TmdbId = "94997",
            TvdbId = "371572",
            MediaInfoJson = "{\"ContainerFormat\":\"Matroska\",\"Resolution\":\"3840x2160 (4K UHD)\",\"VideoCodec\":\"HEVC (H.265)\",\"HdrFormat\":\"Dolby Vision / HDR10\",\"AudioCodec\":\"E-AC-3 (Dolby Digital Plus)\",\"AudioChannels\":\"5.1 Surround\",\"SubtitleTracks\":[\"eng\",\"fre\"]}",
        };

        this.mediaEnrichmentService.GetMetadata(42).Returns(meta);

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "http://test/webhook-nested",
            Arg.Is<object>(payload =>
                payload != null &&
                (string)payload.GetType().GetProperty("eventType")!.GetValue(payload)! == "OnDownloadComplete" &&
                (string)payload.GetType().GetProperty("instanceName")!.GetValue(payload)! == "Leecharr" &&
                payload.GetType().GetProperty("torrent") != null &&
                payload.GetType().GetProperty("media") != null &&
                payload.GetType().GetProperty("streamSpecs") != null &&
                (int)payload.GetType().GetProperty("torrent")!.GetValue(payload)!.GetType().GetProperty("id")!.GetValue(payload.GetType().GetProperty("torrent")!.GetValue(payload)!)! == 42 &&
                (string)payload.GetType().GetProperty("torrent")!.GetValue(payload)!.GetType().GetProperty("state")!.GetValue(payload.GetType().GetProperty("torrent")!.GetValue(payload)!)! == "Seeding" &&
                (string)payload.GetType().GetProperty("media")!.GetValue(payload)!.GetType().GetProperty("title")!.GetValue(payload.GetType().GetProperty("media")!.GetValue(payload)!)! == "House of the Dragon" &&
                (int?)payload.GetType().GetProperty("media")!.GetValue(payload)!.GetType().GetProperty("seasonNumber")!.GetValue(payload.GetType().GetProperty("media")!.GetValue(payload)!) == 2 &&
                (int?)payload.GetType().GetProperty("media")!.GetValue(payload)!.GetType().GetProperty("episodeNumber")!.GetValue(payload.GetType().GetProperty("media")!.GetValue(payload)!) == 8 &&
                (string)payload.GetType().GetProperty("streamSpecs")!.GetValue(payload)!.GetType().GetProperty("videoCodec")!.GetValue(payload.GetType().GetProperty("streamSpecs")!.GetValue(payload)!)! == "HEVC (H.265)" &&
                (string)payload.GetType().GetProperty("streamSpecs")!.GetValue(payload)!.GetType().GetProperty("containerFormat")!.GetValue(payload.GetType().GetProperty("streamSpecs")!.GetValue(payload)!)! == "Matroska"));
    }

    [TestCase(double.PositiveInfinity, 0.0)]
    [TestCase(double.NegativeInfinity, 0.0)]
    [TestCase(double.NaN, 0.0)]
    [TestCase(1.75, 1.75)]
    public async Task Handle_TorrentAddedEvent_WhenRatioIsNonFinite_NormalizesRatioToFiniteValue(double inputRatio, double expectedRatio)
    {
        var notification = new NotificationDefinition
        {
            Id = 50,
            Name = "Webhook NonFinite",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/webhook-ratio",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 50,
            Name = "NonFiniteRatio.mkv",
            Status = TorrentStatus.Downloading,
            Ratio = inputRatio,
            Progress = double.PositiveInfinity, // Also test progress
        };

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "http://test/webhook-ratio",
            Arg.Is<object>(payload =>
                payload != null &&
                payload.GetType().GetProperty("torrent") != null &&
                (double)payload.GetType().GetProperty("torrent")!.GetValue(payload)!.GetType().GetProperty("ratio")!.GetValue(payload.GetType().GetProperty("torrent")!.GetValue(payload)!)! == expectedRatio &&
                (double)payload.GetType().GetProperty("torrent")!.GetValue(payload)!.GetType().GetProperty("progress")!.GetValue(payload.GetType().GetProperty("torrent")!.GetValue(payload)!)! == 0.0));
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenCustomScriptNotificationWithJsonSettings_ParsesPathAndArguments()
    {
        var notification = new NotificationDefinition
        {
            Id = 60,
            Name = "Custom Script JSON",
            Implementation = "CustomScript",
            ConfigContract = "CustomScriptSettings",
            Settings = "{\"path\": \"/opt/scripts/notify.sh\", \"arguments\": \"--arg1 --arg2\"}",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 60,
            Name = "ScriptTest.iso",
            Status = TorrentStatus.Downloading,
        };

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await this.scriptTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.customScriptService.Received(1).ExecuteScriptAsync(
            "/opt/scripts/notify.sh",
            torrent,
            "OnGrab",
            "--arg1 --arg2");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenCustomScriptNotificationWithPlainPath_ParsesPathAndNullArguments()
    {
        var notification = new NotificationDefinition
        {
            Id = 61,
            Name = "Custom Script Plain",
            Implementation = "CustomScript",
            ConfigContract = "CustomScriptSettings",
            Settings = "/opt/scripts/notify.sh",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 61,
            Name = "ScriptTestPlain.iso",
            Status = TorrentStatus.Downloading,
        };

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await this.scriptTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.customScriptService.Received(1).ExecuteScriptAsync(
            "/opt/scripts/notify.sh",
            torrent,
            "OnGrab",
            null);
    }

    [Test]
    public async Task Handle_VpnKillSwitchTriggeredEvent_WhenCustomScriptNotificationWithJsonSettings_ParsesPathAndArguments()
    {
        var notification = new NotificationDefinition
        {
            Id = 62,
            Name = "VPN Kill Script",
            Implementation = "CustomScript",
            ConfigContract = "CustomScriptSettings",
            Settings = "{\"path\": \"/opt/scripts/vpn-down.sh\", \"arguments\": \"--kill\"}",
            OnHealthIssue = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        this.handler.Handle(new VpnKillSwitchTriggeredEvent("tun0"));

        await this.scriptTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.customScriptService.Received(1).ExecuteScriptAsync(
            "/opt/scripts/vpn-down.sh",
            null,
            "OnHealthIssue",
            "--kill");
    }

    [Test]
    public async Task Handle_ApplicationUpdatedEvent_WhenCustomScriptNotificationWithJsonSettings_ParsesPathAndArguments()
    {
        var notification = new NotificationDefinition
        {
            Id = 63,
            Name = "App Update Script",
            Implementation = "CustomScript",
            ConfigContract = "CustomScriptSettings",
            Settings = "{\"path\": \"/opt/scripts/app-update.sh\", \"arguments\": \"--update\"}",
            OnApplicationUpdate = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        this.handler.Handle(new ApplicationUpdatedEvent { PreviousVersion = "1.0.0", NewVersion = "1.1.0" });

        await this.scriptTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.customScriptService.Received(1).ExecuteScriptAsync(
            "/opt/scripts/app-update.sh",
            null,
            "OnApplicationUpdate",
            "--update");
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WhenSystemLevelNotResolved_DispatchesHealthIssueNotification()
    {
        var notification = new NotificationDefinition
        {
            Id = 64,
            Name = "System Health Webhook",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/system-health",
            OnHealthIssue = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        this.handler.Handle(new HealthIssueEvent(torrent: null, "DiskSpace", "Disk space is critically low (<5GB free)", isResolved: false));

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "http://test/system-health",
            Arg.Is<object>(payload =>
                payload != null &&
                (string)payload.GetType().GetProperty("EventType")!.GetValue(payload)! == "OnHealthIssue" &&
                (string)payload.GetType().GetProperty("Source")!.GetValue(payload)! == "DiskSpace" &&
                (string)payload.GetType().GetProperty("Message")!.GetValue(payload)! == "Disk space is critically low (<5GB free)" &&
                (bool)payload.GetType().GetProperty("IsResolved")!.GetValue(payload)! == false));
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WhenSystemLevelResolved_DispatchesHealthRestoredNotification()
    {
        var notification = new NotificationDefinition
        {
            Id = 65,
            Name = "System Health Webhook",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/system-health-restored",
            OnHealthRestored = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        this.handler.Handle(new HealthIssueEvent(torrentId: 0, "DiskSpace", "Disk space has been restored", isResolved: true));

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "http://test/system-health-restored",
            Arg.Is<object>(payload =>
                payload != null &&
                (string)payload.GetType().GetProperty("EventType")!.GetValue(payload)! == "OnHealthRestored" &&
                (string)payload.GetType().GetProperty("Source")!.GetValue(payload)! == "DiskSpace" &&
                (string)payload.GetType().GetProperty("Message")!.GetValue(payload)! == "Disk space has been restored" &&
                (bool)payload.GetType().GetProperty("IsResolved")!.GetValue(payload)! == true));
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WhenSystemLevelAndCustomScriptConfigured_ExecutesCustomScriptWithNullTorrent()
    {
        var notification = new NotificationDefinition
        {
            Id = 66,
            Name = "System Health Script",
            Implementation = "CustomScript",
            ConfigContract = "CustomScriptSettings",
            Settings = "{\"path\": \"/opt/scripts/health-alert.sh\", \"arguments\": \"--system\"}",
            OnHealthIssue = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        this.handler.Handle(new HealthIssueEvent(torrentId: 0, "Database", "Database connection timeout", isResolved: false));

        await this.scriptTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.customScriptService.Received(1).ExecuteScriptAsync(
            "/opt/scripts/health-alert.sh",
            null,
            "OnHealthIssue",
            "--system");
    }

    [Test]
    public void Handle_HealthIssueEvent_WhenMessageIsNull_DoesNotThrow()
    {
        var act = () => this.handler.Handle((HealthIssueEvent)null!);
        act.Should().NotThrow();
    }

    [Test]
    public void ResolveTargetUrl_Gotify_NormalizesUrlToIncludeMessageEndpoint()
    {
        NotificationEventHandler.ResolveTargetUrl("Gotify", "http://gotify-server:8080")
            .Should().Be("http://gotify-server:8080/message");

        NotificationEventHandler.ResolveTargetUrl("Gotify", "http://gotify-server:8080/")
            .Should().Be("http://gotify-server:8080/message");

        NotificationEventHandler.ResolveTargetUrl("Gotify", "http://gotify-server:8080/message")
            .Should().Be("http://gotify-server:8080/message");

        NotificationEventHandler.ResolveTargetUrl("Gotify", "{\"url\":\"http://gotify-server:8080\"}")
            .Should().Be("http://gotify-server:8080/message");

        NotificationEventHandler.ResolveTargetUrl("Gotify", "{\"serverUrl\":\"http://gotify-server:8080\"}")
            .Should().Be("http://gotify-server:8080/message");
    }

    [Test]
    public void ResolveCustomHeaders_Gotify_InjectsXGotifyKeyHeader()
    {
        NotificationEventHandler.ResolveCustomHeaders("Gotify", "{\"url\":\"http://gotify-server:8080\",\"token\":\"A12345678\"}")
            .Should().Be("{\"X-Gotify-Key\":\"A12345678\"}");

        NotificationEventHandler.ResolveCustomHeaders("Gotify", "{\"url\":\"http://gotify-server:8080\",\"appToken\":\"B98765432\"}")
            .Should().Be("{\"X-Gotify-Key\":\"B98765432\"}");

        NotificationEventHandler.ResolveCustomHeaders("Gotify", "url=http://gotify-server:8080&token=C11223344")
            .Should().Be("{\"X-Gotify-Key\":\"C11223344\"}");

        var combined = NotificationEventHandler.ResolveCustomHeaders("Gotify", "{\"url\":\"http://gotify-server:8080\",\"token\":\"A123\",\"headers\":{\"Custom-Header\":\"Value\"}}");
        combined.Should().Contain("\"Custom-Header\":\"Value\"");
        combined.Should().Contain("\"X-Gotify-Key\":\"A123\"");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenGotifyNotification_DispatchesToNormalizedUrlWithAuthHeader()
    {
        var notification = new NotificationDefinition
        {
            Id = 80,
            Name = "Gotify Alert",
            Implementation = "Gotify",
            ConfigContract = "GotifySettings",
            Settings = "{\"url\":\"http://gotify-server:8080\",\"token\":\"secret-app-token\"}",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 81,
            Name = "Gotify Test Torrent",
            Category = "TV",
            Status = TorrentStatus.Downloading,
        };

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "http://gotify-server:8080/message",
            Arg.Is<object>(p => p != null),
            "{\"X-Gotify-Key\":\"secret-app-token\"}");
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenDownloadCompletedAndSeeding_CalculatesDownloadTimeAndSeedingTimeIndependently()
    {
        var notification = new NotificationDefinition
        {
            Id = 90,
            Name = "Webhook Seed Goal",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/webhook-seeding",
            OnSeedGoalReached = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var dateAdded = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var dateCompleted = new DateTime(2026, 1, 1, 12, 10, 0, DateTimeKind.Utc); // 600s download duration

        var torrent = new Torrent
        {
            Id = 91,
            Name = "Seeding Torrent",
            Category = "Movies",
            Status = TorrentStatus.Seeding,
            DateAdded = dateAdded,
            DateCompleted = dateCompleted,
            CumulativeSeedingTimeSeconds = 3600, // 3600s seeding duration
        };

        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Downloading,
            NewStatus = TorrentStatus.Seeding,
        });

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "http://test/webhook-seeding",
            Arg.Is<object>(payload =>
                payload != null &&
                AssertTorrentPayloadTimes(payload, 600L, 3600L)));
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenTorrentHasNotCompleted_SetsDownloadTimeAndSeedingTimeToZero()
    {
        var notification = new NotificationDefinition
        {
            Id = 92,
            Name = "Webhook Added",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "http://test/webhook-added",
            OnGrab = true,
        };

        this.notificationRepository.GetEnabled().Returns(new List<NotificationDefinition> { notification });

        var torrent = new Torrent
        {
            Id = 93,
            Name = "Added Torrent",
            Category = "Movies",
            Status = TorrentStatus.Downloading,
            DateAdded = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            DateCompleted = null,
            CumulativeSeedingTimeSeconds = 0,
        };

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        await this.webhookTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await this.webhookDispatcher.Received(1).DispatchAsync(
            "http://test/webhook-added",
            Arg.Is<object>(payload =>
                payload != null &&
                AssertTorrentPayloadTimes(payload, 0L, 0L)));
    }

    private static bool AssertTorrentPayloadTimes(object payload, long expectedDownloadTimeSeconds, long expectedSeedingTimeSeconds)
    {
        var torrentProp = payload.GetType().GetProperty("torrent");
        if (torrentProp == null)
        {
            return false;
        }

        var torrentObj = torrentProp.GetValue(payload);
        if (torrentObj == null)
        {
            return false;
        }

        var downloadTimeProp = torrentObj.GetType().GetProperty("downloadTimeSeconds");
        var seedingTimeProp = torrentObj.GetType().GetProperty("seedingTimeSeconds");

        if (downloadTimeProp == null || seedingTimeProp == null)
        {
            return false;
        }

        var downloadTime = (long)downloadTimeProp.GetValue(torrentObj)!;
        var seedingTime = (long)seedingTimeProp.GetValue(torrentObj)!;

        return downloadTime == expectedDownloadTimeSeconds && seedingTime == expectedSeedingTimeSeconds;
    }
}

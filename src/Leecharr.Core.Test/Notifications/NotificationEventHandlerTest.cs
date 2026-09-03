// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;
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
}

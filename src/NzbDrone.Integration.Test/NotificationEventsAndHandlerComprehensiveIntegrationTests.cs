// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class NotificationEventsAndHandlerComprehensiveIntegrationTests : IntegrationTestBase
{
    private NotificationEventHandler handler;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        var notifRepo = services.GetRequiredService<INotificationRepository>();
        var webhookDispatcher = services.GetRequiredService<IWebhookDispatcher>();
        var customScript = services.GetService<ICustomScriptService>();
        var config = services.GetService<IConfigService>();
        this.handler = new NotificationEventHandler(notifRepo, webhookDispatcher, customScript, config);
    }

    [Test]
    public void NotificationEventHandler_HandleLifecycleEvents_ExecutesCleanly()
    {
        var dummyTorrent = new Torrent
        {
            Id = 42,
            Name = "Integration.Test.Event.Torrent.2026.1080p",
            InfoHash = "1234567890ABCDEF1234567890ABCDEF12345678",
            TotalSize = 1024L * 1024 * 1024,
            Downloaded = 1024L * 1024 * 1024,
            Uploaded = 2048L * 1024 * 1024,
            Status = TorrentStatus.Seeding,
            Ratio = 2.0,
        };

        // 1. TorrentAddedEvent
        var actAdded = new Action(() => this.handler.Handle(new TorrentAddedEvent { Torrent = dummyTorrent }));
        actAdded.Should().NotThrow();

        // 2. TorrentDownloadCompletedEvent
        var actComplete = new Action(() => this.handler.Handle(new TorrentDownloadCompletedEvent(dummyTorrent)));
        actComplete.Should().NotThrow();

        // 3. TorrentStatusChangedEvent
        var actStatus = new Action(() => this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = dummyTorrent,
            OldStatus = TorrentStatus.Downloading,
        }));
        actStatus.Should().NotThrow();

        // 4. TorrentSeedGoalReachedEvent
        var actGoal = new Action(() => this.handler.Handle(new TorrentSeedGoalReachedEvent(dummyTorrent)));
        actGoal.Should().NotThrow();

        // 5. TorrentDeletedEvent
        var actDeleted = new Action(() => this.handler.Handle(new TorrentDeletedEvent { Torrent = dummyTorrent, DeleteFiles = false }));
        actDeleted.Should().NotThrow();

        // 6. MediaEnrichedEvent
        var actEnriched = new Action(() => this.handler.Handle(new MediaEnrichedEvent
        {
            TorrentId = 42,
            Metadata = new TorrentMediaMetadata { TorrentId = 42, Title = "Enriched Movie", Year = 2026 },
        }));
        actEnriched.Should().NotThrow();

        // 7. Archive extraction events
        var actExtractComplete = new Action(() => this.handler.Handle(new ArchiveExtractionCompletedEvent
        {
            Torrent = dummyTorrent,
            ArchivePath = "/downloads/sample.zip",
            DestinationDirectory = "/downloads/sample",
        }));
        actExtractComplete.Should().NotThrow();

        var actExtractFailed = new Action(() => this.handler.Handle(new ArchiveExtractionFailedEvent
        {
            Torrent = dummyTorrent,
            ArchivePath = "/downloads/corrupted.rar",
            DestinationDirectory = "/downloads/corrupted",
            ErrorMessage = "CRC checksum failure",
        }));
        actExtractFailed.Should().NotThrow();

        // 8. VPN Kill switch event
        var actVpn = new Action(() => this.handler.Handle(new VpnKillSwitchTriggeredEvent("tun0")));
        actVpn.Should().NotThrow();

        // 9. Application updated event
        var actAppUpdate = new Action(() => this.handler.Handle(new ApplicationUpdatedEvent
        {
            PreviousVersion = "1.0.0",
            NewVersion = "1.0.1",
        }));
        actAppUpdate.Should().NotThrow();

        // 10. Health issue event
        var actHealth = new Action(() => this.handler.Handle(new HealthIssueEvent(dummyTorrent, "Disk", "Low free space on primary download volume")));
        actHealth.Should().NotThrow();
    }
}

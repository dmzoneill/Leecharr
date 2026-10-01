// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Network;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class NotificationEventHandlerComprehensiveIntegrationTests : IntegrationTestBase
{
    private readonly List<NotificationDefinition> configuredNotifications = new();

    private INotificationRepository notificationRepository = null!;
    private IWebhookDispatcher webhookDispatcher = null!;
    private ICustomScriptService customScriptService = null!;
    private IConfigService configService = null!;
    private IMediaEnrichmentService mediaEnrichmentService = null!;
    private ITorrentRepository torrentRepository = null!;
    private ITorrentFileRepository torrentFileRepository = null!;
    private IDownloadEngine downloadEngine = null!;
    private NotificationEventHandler handler = null!;

    private ConcurrentQueue<WebhookDispatchRecord> recordedDispatches = null!;
    private SemaphoreSlim dispatchSemaphore = null!;
    private ConcurrentQueue<ScriptExecutionRecord> recordedScriptCalls = null!;
    private SemaphoreSlim scriptSemaphore = null!;

    private class WebhookDispatchRecord
    {
        public string TargetUrl { get; set; } = string.Empty;

        public object Payload { get; set; } = null!;

        public string CustomHeaders { get; set; } = string.Empty;
    }

    private class ScriptExecutionRecord
    {
        public string Path { get; set; } = string.Empty;

        public Torrent Torrent { get; set; }

        public string EventType { get; set; } = string.Empty;

        public string Args { get; set; } = string.Empty;
    }

    [SetUp]
    public void SetUp()
    {
        this.recordedDispatches = new ConcurrentQueue<WebhookDispatchRecord>();
        this.dispatchSemaphore = new SemaphoreSlim(0);
        this.recordedScriptCalls = new ConcurrentQueue<ScriptExecutionRecord>();
        this.scriptSemaphore = new SemaphoreSlim(0);
        this.configuredNotifications.Clear();

        this.notificationRepository = Substitute.For<INotificationRepository>();
        this.notificationRepository.GetEnabled().Returns(_ => this.configuredNotifications.Where(n => n.Enable).ToList());

        this.webhookDispatcher = Substitute.For<IWebhookDispatcher>();
        this.webhookDispatcher.DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var targetUrl = ci.ArgAt<string>(0);
                var payload = ci.ArgAt<object>(1);
                var headers = ci.ArgAt<string>(2);
                this.recordedDispatches.Enqueue(new WebhookDispatchRecord
                {
                    TargetUrl = targetUrl ?? string.Empty,
                    Payload = payload,
                    CustomHeaders = headers ?? string.Empty,
                });
                this.dispatchSemaphore.Release();
                return Task.FromResult(true);
            });

        this.webhookDispatcher.DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<string>(),
            Arg.Any<HttpMethod>(),
            Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var targetUrl = ci.ArgAt<string>(0);
                var payload = ci.ArgAt<object>(1);
                var headers = ci.ArgAt<string>(2);
                this.recordedDispatches.Enqueue(new WebhookDispatchRecord
                {
                    TargetUrl = targetUrl ?? string.Empty,
                    Payload = payload,
                    CustomHeaders = headers ?? string.Empty,
                });
                this.dispatchSemaphore.Release();
                return Task.FromResult(true);
            });

        this.customScriptService = Substitute.For<ICustomScriptService>();
        this.customScriptService.ExecuteScriptAsync(
            Arg.Any<string>(),
            Arg.Any<Torrent>(),
            Arg.Any<string>(),
            Arg.Any<string>())
            .Returns(ci =>
            {
                this.recordedScriptCalls.Enqueue(new ScriptExecutionRecord
                {
                    Path = ci.ArgAt<string>(0),
                    Torrent = ci.ArgAt<Torrent>(1),
                    EventType = ci.ArgAt<string>(2),
                    Args = ci.ArgAt<string>(3),
                });
                this.scriptSemaphore.Release();
                return Task.FromResult(true);
            });

        this.configService = Substitute.For<IConfigService>();
        this.mediaEnrichmentService = Substitute.For<IMediaEnrichmentService>();
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.torrentFileRepository = Substitute.For<ITorrentFileRepository>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.downloadEngine.StopAsync().Returns(Task.CompletedTask);

        this.handler = new NotificationEventHandler(
            this.notificationRepository,
            this.webhookDispatcher,
            this.customScriptService,
            this.configService,
            this.mediaEnrichmentService,
            this.torrentRepository,
            this.torrentFileRepository,
            this.downloadEngine);
    }

    [TearDown]
    public void TearDown()
    {
        this.dispatchSemaphore?.Dispose();
        this.scriptSemaphore?.Dispose();
    }

    private void AddNotification(Action<NotificationDefinition> configure)
    {
        var notif = new NotificationDefinition
        {
            Id = this.configuredNotifications.Count + 1,
            Name = $"TestWebhook_{this.configuredNotifications.Count + 1}",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "https://webhook.internal.lan/receiver",
            Enable = true,
            OnGrab = false,
            OnDownloadComplete = false,
            OnHealthIssue = false,
            OnHealthRestored = false,
            OnManualInteractionRequired = false,
            OnSeedGoalReached = false,
            OnTorrentDeleted = false,
            OnApplicationUpdate = false,
            OnExtractComplete = false,
            OnMediaInspected = false,
        };

        configure(notif);
        this.configuredNotifications.Add(notif);
    }

    private async Task<WebhookDispatchRecord> WaitForDispatchAsync(int timeoutMs = 3000)
    {
        var acquired = await this.dispatchSemaphore.WaitAsync(timeoutMs);
        if (!acquired)
        {
            return null;
        }

        this.recordedDispatches.TryDequeue(out var record);
        return record;
    }

    private static Torrent CreateSampleTorrent(int id = 42, string name = "Ubuntu.24.04.LTS.Desktop.amd64.iso", TorrentStatus status = TorrentStatus.Downloading)
    {
        return new Torrent
        {
            Id = id,
            Name = name,
            InfoHash = "4A234567890ABCDEF1234567890ABCDEF1234567",
            Category = "Linux",
            SavePath = "/downloads/torrents/ubuntu",
            Status = status,
            Progress = 0.85,
            TotalSize = 2_500_000_000L,
            Downloaded = 2_125_000_000L,
            Uploaded = 500_000_000L,
            DownloadSpeed = 15_000_000,
            UploadSpeed = 1_500_000,
            Eta = 25,
            Seeders = 120,
            Leechers = 15,
            Ratio = 0.23,
            DateAdded = DateTime.UtcNow.AddMinutes(-45),
            DateCompleted = null,
            CumulativeSeedingTimeSeconds = 0,
            TagIds = new List<int> { 10, 20 },
        };
    }

    // -----------------------------------------------------------------------------------------
    // 1. TorrentAddedEvent, TorrentDownloadCompletedEvent, TorrentStatusChangedEvent, TorrentDeletedEvent
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenOnGrabEnabled_DispatchesWebhookNotification()
    {
        this.AddNotification(n =>
        {
            n.OnGrab = true;
            n.Settings = "https://webhook.internal.lan/grab";
        });

        var torrent = CreateSampleTorrent(101, "Arch.Linux.2026.09.x86_64.iso");
        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/grab");

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnGrab\"");
        json.Should().Contain("Arch.Linux.2026.09.x86_64.iso");
    }

    [Test]
    public async Task Handle_TorrentAddedEvent_WhenTorrentIsNull_DoesNotDispatch()
    {
        this.AddNotification(n => n.OnGrab = true);

        this.handler.Handle(new TorrentAddedEvent { Torrent = null });

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_TorrentDownloadCompletedEvent_WhenOnDownloadCompleteEnabled_DispatchesWebhookNotification()
    {
        this.AddNotification(n =>
        {
            n.OnDownloadComplete = true;
            n.Settings = "https://webhook.internal.lan/download-complete";
        });

        var torrent = CreateSampleTorrent(102, "Fedora.Workstation.42.x86_64.iso", TorrentStatus.Completed);
        torrent.DateCompleted = DateTime.UtcNow;

        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/download-complete");

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnDownloadComplete\"");
        json.Should().Contain("Fedora.Workstation.42.x86_64.iso");
    }

    [Test]
    public async Task Handle_TorrentDownloadCompletedEvent_WhenTorrentIsNull_DoesNotDispatch()
    {
        this.AddNotification(n => n.OnDownloadComplete = true);

        this.handler.Handle(new TorrentDownloadCompletedEvent(null));

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenStatusChangesToError_DispatchesHealthIssueAndManualInteraction()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.OnManualInteractionRequired = true;
            n.Settings = "https://webhook.internal.lan/alerts";
        });

        var torrent = CreateSampleTorrent(103, "Debian.13.Netinst.iso", TorrentStatus.Error);
        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Downloading,
            NewStatus = TorrentStatus.Error,
        });

        var first = await this.WaitForDispatchAsync();
        var second = await this.WaitForDispatchAsync();

        first.Should().NotBeNull();
        second.Should().NotBeNull();

        var payloads = new[] { JsonSerializer.Serialize(first.Payload), JsonSerializer.Serialize(second.Payload) };
        payloads.Should().Contain(p => p.Contains("\"eventType\":\"OnHealthIssue\""));
        payloads.Should().Contain(p => p.Contains("\"eventType\":\"OnManualInteractionRequired\""));
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenStatusChangesToStalled_DispatchesHealthIssueAndManualInteraction()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.OnManualInteractionRequired = true;
            n.Settings = "https://webhook.internal.lan/alerts";
        });

        var torrent = CreateSampleTorrent(104, "FreeBSD.15.RELEASE.iso", TorrentStatus.Stalled);
        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Downloading,
            NewStatus = TorrentStatus.Stalled,
        });

        var first = await this.WaitForDispatchAsync();
        var second = await this.WaitForDispatchAsync();

        first.Should().NotBeNull();
        second.Should().NotBeNull();

        var payloads = new[] { JsonSerializer.Serialize(first.Payload), JsonSerializer.Serialize(second.Payload) };
        payloads.Should().Contain(p => p.Contains("\"eventType\":\"OnHealthIssue\""));
        payloads.Should().Contain(p => p.Contains("\"eventType\":\"OnManualInteractionRequired\""));
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenStatusRecoversFromError_DispatchesHealthRestored()
    {
        this.AddNotification(n =>
        {
            n.OnHealthRestored = true;
            n.Settings = "https://webhook.internal.lan/recovery";
        });

        var torrent = CreateSampleTorrent(105, "OpenSUSE.Tumbleweed.iso", TorrentStatus.Downloading);
        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Error,
            NewStatus = TorrentStatus.Downloading,
        });

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/recovery");

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnHealthRestored\"");
        json.Should().Contain("OpenSUSE.Tumbleweed.iso");
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenStatusRecoversFromStalled_DispatchesHealthRestored()
    {
        this.AddNotification(n =>
        {
            n.OnHealthRestored = true;
            n.Settings = "https://webhook.internal.lan/recovery";
        });

        var torrent = CreateSampleTorrent(106, "AlmaLinux.9.iso", TorrentStatus.Downloading);
        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Stalled,
            NewStatus = TorrentStatus.Downloading,
        });

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnHealthRestored\"");
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenStatusChangeIsBenign_DoesNotDispatchHealthEvents()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.OnHealthRestored = true;
        });

        var torrent = CreateSampleTorrent(107, "Rocky.Linux.9.iso", TorrentStatus.Downloading);
        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = torrent,
            OldStatus = TorrentStatus.Paused,
            NewStatus = TorrentStatus.Downloading,
        });

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_TorrentStatusChangedEvent_WhenTorrentIsNull_DoesNotDispatch()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.OnHealthRestored = true;
        });

        this.handler.Handle(new TorrentStatusChangedEvent
        {
            Torrent = null,
            OldStatus = TorrentStatus.Error,
            NewStatus = TorrentStatus.Downloading,
        });

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_TorrentDeletedEvent_WhenOnTorrentDeletedEnabled_DispatchesWebhookNotification()
    {
        this.AddNotification(n =>
        {
            n.OnTorrentDeleted = true;
            n.Settings = "https://webhook.internal.lan/deleted";
        });

        var torrent = CreateSampleTorrent(108, "VoidLinux.x86_64.iso");
        this.handler.Handle(new TorrentDeletedEvent
        {
            Torrent = torrent,
            DeleteFiles = false,
        });

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/deleted");

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnTorrentDeleted\"");
        json.Should().Contain("VoidLinux.x86_64.iso");
    }

    [Test]
    public async Task Handle_TorrentDeletedEvent_WhenTorrentIsNull_DoesNotDispatch()
    {
        this.AddNotification(n => n.OnTorrentDeleted = true);

        this.handler.Handle(new TorrentDeletedEvent
        {
            Torrent = null,
            DeleteFiles = true,
        });

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    // -----------------------------------------------------------------------------------------
    // 2. VpnKillSwitchTriggeredEvent, HealthIssueEvent, TorrentSeedGoalReachedEvent,
    //    ApplicationUpdatedEvent, ArchiveExtractionCompletedEvent, ArchiveExtractionFailedEvent
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task Handle_VpnKillSwitchTriggeredEvent_DispatchesHealthIssueAndHaltsDownloadEngine()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.Settings = "https://webhook.internal.lan/vpn";
        });

        this.handler.Handle(new VpnKillSwitchTriggeredEvent("wg0"));

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/vpn");

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("OnHealthIssue");
        json.Should().Contain("VPN Kill Switch triggered");

        await this.downloadEngine.Received(1).StopAsync();
    }

    [Test]
    public async Task Handle_VpnKillSwitchTriggeredEvent_WhenEngineThrows_CatchesAndStillDispatchesWebhook()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.Settings = "https://webhook.internal.lan/vpn";
        });

        this.downloadEngine.StopAsync().Returns(Task.FromException(new InvalidOperationException("Engine error")));

        this.handler.Handle(new VpnKillSwitchTriggeredEvent("tun0"));

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("VPN Kill Switch triggered");
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WithTorrent_WhenUnresolved_DispatchesHealthIssue()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.Settings = "https://webhook.internal.lan/health";
        });

        var torrent = CreateSampleTorrent(109, "Gentoo.Minimal.iso");
        this.handler.Handle(new HealthIssueEvent(torrent, "Tracker", "Connection timed out", isResolved: false));

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnHealthIssue\"");
        json.Should().Contain("Gentoo.Minimal.iso");
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WithTorrent_WhenResolved_DispatchesHealthRestored()
    {
        this.AddNotification(n =>
        {
            n.OnHealthRestored = true;
            n.Settings = "https://webhook.internal.lan/health-restored";
        });

        var torrent = CreateSampleTorrent(110, "Alpine.Standard.iso");
        this.handler.Handle(new HealthIssueEvent(torrent, "Tracker", "Connection restored", isResolved: true));

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnHealthRestored\"");
        json.Should().Contain("Alpine.Standard.iso");
    }

    [Test]
    public async Task Handle_HealthIssueEvent_GenericWithoutTorrent_WhenUnresolved_DispatchesGenericHealthIssue()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.Settings = "https://webhook.internal.lan/generic-health";
        });

        this.handler.Handle(new HealthIssueEvent(torrent: null, source: "Disk", message: "Low storage on volume /data", isResolved: false));

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"EventType\":\"OnHealthIssue\"");
        json.Should().Contain("\"Source\":\"Disk\"");
        json.Should().Contain("Low storage on volume /data");
        json.Should().Contain("\"IsResolved\":false");
    }

    [Test]
    public async Task Handle_HealthIssueEvent_GenericWithoutTorrent_WhenResolved_DispatchesGenericHealthRestored()
    {
        this.AddNotification(n =>
        {
            n.OnHealthRestored = true;
            n.Settings = "https://webhook.internal.lan/generic-health";
        });

        this.handler.Handle(new HealthIssueEvent(torrent: null, source: "Disk", message: "Storage freed on volume /data", isResolved: true));

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"EventType\":\"OnHealthRestored\"");
        json.Should().Contain("\"Source\":\"Disk\"");
        json.Should().Contain("Storage freed on volume /data");
        json.Should().Contain("\"IsResolved\":true");
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WhenTorrentIdResolvesViaRepository_DispatchesTorrentSpecificHealthNotification()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.Settings = "https://webhook.internal.lan/torrent-health";
        });

        var torrent = CreateSampleTorrent(111, "Kali.Linux.Installer.iso");
        this.torrentRepository.Get(111).Returns(torrent);

        this.handler.Handle(new HealthIssueEvent(111, "Tracker", "Private tracker passkey invalid", isResolved: false));

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnHealthIssue\"");
        json.Should().Contain("Kali.Linux.Installer.iso");
    }

    [Test]
    public async Task Handle_HealthIssueEvent_WhenNull_DoesNotThrowOrDispatch()
    {
        this.AddNotification(n => n.OnHealthIssue = true);

        this.handler.Handle((HealthIssueEvent)null!);

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_TorrentSeedGoalReachedEvent_WhenOnSeedGoalReachedEnabled_DispatchesWebhookNotification()
    {
        this.AddNotification(n =>
        {
            n.OnSeedGoalReached = true;
            n.Settings = "https://webhook.internal.lan/seed-goal";
        });

        var torrent = CreateSampleTorrent(112, "Manjaro.KDE.2026.iso", TorrentStatus.Seeding);
        torrent.Ratio = 2.5;

        this.handler.Handle(new TorrentSeedGoalReachedEvent(torrent));

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/seed-goal");

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnSeedGoalReached\"");
        json.Should().Contain("Manjaro.KDE.2026.iso");
    }

    [Test]
    public async Task Handle_TorrentSeedGoalReachedEvent_WhenTorrentIsNull_DoesNotDispatch()
    {
        this.AddNotification(n => n.OnSeedGoalReached = true);

        this.handler.Handle(new TorrentSeedGoalReachedEvent(null));

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_ApplicationUpdatedEvent_DispatchesApplicationUpdateNotification()
    {
        this.AddNotification(n =>
        {
            n.OnApplicationUpdate = true;
            n.Settings = "https://webhook.internal.lan/app-update";
        });

        this.handler.Handle(new ApplicationUpdatedEvent
        {
            PreviousVersion = "1.0.0",
            NewVersion = "1.2.0",
        });

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/app-update");

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"EventType\":\"OnApplicationUpdate\"");
        json.Should().Contain("\"PreviousVersion\":\"1.0.0\"");
        json.Should().Contain("\"NewVersion\":\"1.2.0\"");
        json.Should().Contain("Leecharr updated to version 1.2.0");
    }

    [Test]
    public async Task Handle_ApplicationUpdatedEvent_WhenNull_DoesNotThrowOrDispatch()
    {
        this.AddNotification(n => n.OnApplicationUpdate = true);

        this.handler.Handle((ApplicationUpdatedEvent)null!);

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_ArchiveExtractionCompletedEvent_WhenOnExtractCompleteEnabled_DispatchesExtractComplete()
    {
        this.AddNotification(n =>
        {
            n.OnExtractComplete = true;
            n.Settings = "https://webhook.internal.lan/extract-complete";
        });

        var torrent = CreateSampleTorrent(113, "Archived.Payload.zip.torrent");
        this.handler.Handle(new ArchiveExtractionCompletedEvent
        {
            Torrent = torrent,
            ArchivePath = "/downloads/torrents/payload.zip",
            DestinationDirectory = "/downloads/extracted/payload",
        });

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/extract-complete");

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnExtractComplete\"");
        json.Should().Contain("Archived.Payload.zip.torrent");
    }

    [Test]
    public async Task Handle_ArchiveExtractionCompletedEvent_WhenTorrentIsNull_DoesNotDispatch()
    {
        this.AddNotification(n => n.OnExtractComplete = true);

        this.handler.Handle(new ArchiveExtractionCompletedEvent
        {
            Torrent = null,
            ArchivePath = "/downloads/sample.zip",
            DestinationDirectory = "/downloads/extracted",
        });

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_ArchiveExtractionFailedEvent_DispatchesHealthIssueAndManualInteractionRequired()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.OnManualInteractionRequired = true;
            n.Settings = "https://webhook.internal.lan/extract-failure";
        });

        var torrent = CreateSampleTorrent(114, "Damaged.Archive.rar.torrent");
        this.handler.Handle(new ArchiveExtractionFailedEvent
        {
            Torrent = torrent,
            ArchivePath = "/downloads/corrupt.rar",
            DestinationDirectory = "/downloads/extracted",
            ErrorMessage = "CRC mismatch in volume 3",
        });

        var first = await this.WaitForDispatchAsync();
        var second = await this.WaitForDispatchAsync();

        first.Should().NotBeNull();
        second.Should().NotBeNull();

        var payloads = new[] { JsonSerializer.Serialize(first.Payload), JsonSerializer.Serialize(second.Payload) };
        payloads.Should().Contain(p => p.Contains("\"eventType\":\"OnHealthIssue\""));
        payloads.Should().Contain(p => p.Contains("\"eventType\":\"OnManualInteractionRequired\""));
    }

    [Test]
    public async Task Handle_ArchiveExtractionFailedEvent_WhenTorrentIsNull_DoesNotDispatch()
    {
        this.AddNotification(n =>
        {
            n.OnHealthIssue = true;
            n.OnManualInteractionRequired = true;
        });

        this.handler.Handle(new ArchiveExtractionFailedEvent
        {
            Torrent = null,
            ErrorMessage = "Extraction failed",
        });

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_MediaEnrichedEvent_WhenTorrentExistsInRepository_DispatchesOnMediaInspected()
    {
        this.AddNotification(n =>
        {
            n.OnMediaInspected = true;
            n.Settings = "https://webhook.internal.lan/media-enriched";
        });

        var torrent = CreateSampleTorrent(115, "Cosmos.Laundromat.2015.1080p.mkv");
        this.torrentRepository.Get(115).Returns(torrent);

        this.handler.Handle(new MediaEnrichedEvent
        {
            TorrentId = 115,
            Metadata = new TorrentMediaMetadata
            {
                TorrentId = 115,
                Title = "Cosmos Laundromat",
                Year = 2015,
            },
        });

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/media-enriched");

        var json = JsonSerializer.Serialize(dispatch.Payload);
        json.Should().Contain("\"eventType\":\"OnMediaInspected\"");
        json.Should().Contain("Cosmos.Laundromat.2015.1080p.mkv");
    }

    [Test]
    public async Task Handle_MediaEnrichedEvent_WhenTorrentNotFound_DoesNotDispatch()
    {
        this.AddNotification(n => n.OnMediaInspected = true);

        this.torrentRepository.Get(999).Returns((Torrent)null!);

        this.handler.Handle(new MediaEnrichedEvent { TorrentId = 999 });

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task Handle_MediaEnrichedEvent_WhenNull_DoesNotThrowOrDispatch()
    {
        this.AddNotification(n => n.OnMediaInspected = true);

        this.handler.Handle((MediaEnrichedEvent)null!);

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    // -----------------------------------------------------------------------------------------
    // 3. Configure a mock or test Notification entity in INotificationRepository
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task ConfigureNotificationEntity_WithOnGrabAndOnDownload_SelectivelyDispatchesOnlyConfiguredEvents()
    {
        this.AddNotification(n =>
        {
            n.OnGrab = true;
            n.OnDownloadComplete = true;
            n.OnHealthIssue = true;
            n.OnTorrentDeleted = false;
            n.Settings = "https://webhook.internal.lan/events";
        });

        var torrent = CreateSampleTorrent(116, "Selective.Event.Torrent.iso");

        // 1. OnGrab should trigger
        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });
        var grabDispatch = await this.WaitForDispatchAsync();
        grabDispatch.Should().NotBeNull();
        JsonSerializer.Serialize(grabDispatch.Payload).Should().Contain("\"eventType\":\"OnGrab\"");

        // 2. OnDownloadComplete should trigger
        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));
        var downloadDispatch = await this.WaitForDispatchAsync();
        downloadDispatch.Should().NotBeNull();
        JsonSerializer.Serialize(downloadDispatch.Payload).Should().Contain("\"eventType\":\"OnDownloadComplete\"");

        // 3. OnHealthIssue should trigger
        this.handler.Handle(new HealthIssueEvent(torrent, "Disk", "Disk warning", isResolved: false));
        var healthDispatch = await this.WaitForDispatchAsync();
        healthDispatch.Should().NotBeNull();
        JsonSerializer.Serialize(healthDispatch.Payload).Should().Contain("\"eventType\":\"OnHealthIssue\"");

        // 4. OnTorrentDeleted is false, so it should NOT trigger
        this.handler.Handle(new TorrentDeletedEvent { Torrent = torrent });
        var deletedDispatch = await this.WaitForDispatchAsync(200);
        deletedDispatch.Should().BeNull();
    }

    [Test]
    public async Task ConfigureNotificationEntity_WhenDisabled_DoesNotDispatchAnyEvent()
    {
        this.AddNotification(n =>
        {
            n.Enable = false;
            n.OnGrab = true;
            n.OnDownloadComplete = true;
            n.OnHealthIssue = true;
        });

        var torrent = CreateSampleTorrent(117, "Disabled.Notification.Torrent.iso");

        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });
        this.handler.Handle(new TorrentDownloadCompletedEvent(torrent));
        this.handler.Handle(new HealthIssueEvent(torrent, "Tracker", "Offline", isResolved: false));

        var dispatch = await this.WaitForDispatchAsync(200);
        dispatch.Should().BeNull();
    }

    [Test]
    public async Task ConfigureNotificationEntity_WithTags_OnlyDispatchesForTorrentsWithMatchingTags()
    {
        this.AddNotification(n =>
        {
            n.OnGrab = true;
            n.Tags = new List<int> { 99, 100 };
            n.Settings = "https://webhook.internal.lan/tagged";
        });

        var nonMatchingTorrent = CreateSampleTorrent(118, "NoMatch.Tags.iso");
        nonMatchingTorrent.TagIds = new List<int> { 1, 2, 3 };

        this.handler.Handle(new TorrentAddedEvent { Torrent = nonMatchingTorrent });
        var nonMatchDispatch = await this.WaitForDispatchAsync(200);
        nonMatchDispatch.Should().BeNull();

        var matchingTorrent = CreateSampleTorrent(119, "Match.Tags.iso");
        matchingTorrent.TagIds = new List<int> { 5, 99 };

        this.handler.Handle(new TorrentAddedEvent { Torrent = matchingTorrent });
        var matchDispatch = await this.WaitForDispatchAsync();
        matchDispatch.Should().NotBeNull();
        matchDispatch.TargetUrl.Should().Be("https://webhook.internal.lan/tagged");
    }

    [Test]
    public async Task ConfigureNotificationEntity_WithCustomHeaders_PassesHeadersToWebhookDispatcher()
    {
        var settingsJson = "{\"url\":\"https://webhook.internal.lan/custom-headers\",\"headers\":\"Authorization: Bearer secret-token\\nX-System: Leecharr\"}";

        this.AddNotification(n =>
        {
            n.OnGrab = true;
            n.Settings = settingsJson;
        });

        var torrent = CreateSampleTorrent(120, "Headers.Test.iso");
        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        var dispatch = await this.WaitForDispatchAsync();
        dispatch.Should().NotBeNull();
        dispatch.TargetUrl.Should().Be("https://webhook.internal.lan/custom-headers");
        dispatch.CustomHeaders.Should().Contain("Authorization: Bearer secret-token");
    }

    [Test]
    public async Task ConfigureNotificationEntity_CustomScriptImplementation_ExecutesScriptAsync()
    {
        var scriptSettings = "{\"path\":\"/opt/scripts/notify.sh\",\"arguments\":\"--priority high\"}";

        this.AddNotification(n =>
        {
            n.Implementation = "CustomScript";
            n.OnGrab = true;
            n.Settings = scriptSettings;
        });

        var torrent = CreateSampleTorrent(121, "Script.Execution.Torrent.iso");
        this.handler.Handle(new TorrentAddedEvent { Torrent = torrent });

        var acquired = await this.scriptSemaphore.WaitAsync(3000);
        acquired.Should().BeTrue();

        this.recordedScriptCalls.TryDequeue(out var scriptRecord);
        scriptRecord.Should().NotBeNull();
        scriptRecord.Path.Should().Be("/opt/scripts/notify.sh");
        scriptRecord.EventType.Should().Be("OnGrab");
        scriptRecord.Args.Should().Be("--priority high");
        scriptRecord.Torrent.Should().BeSameAs(torrent);
    }

    [Test]
    public async Task NotificationRepository_DatabaseIntegration_PersistsAndRetrievesNotificationForEventHandler()
    {
        var realRepo = GlobalSetup.Factory.Services.GetRequiredService<INotificationRepository>();

        var testDefinition = new NotificationDefinition
        {
            Name = "Real SQLite Database Integration Notification",
            Implementation = "Webhook",
            ConfigContract = "WebhookSettings",
            Settings = "https://webhook.database.local/ingest",
            Enable = true,
            OnGrab = true,
            OnDownloadComplete = false,
            OnHealthIssue = false,
            OnHealthRestored = false,
            OnManualInteractionRequired = false,
            OnSeedGoalReached = false,
            OnTorrentDeleted = false,
            OnApplicationUpdate = false,
            OnExtractComplete = false,
            OnMediaInspected = false,
        };

        var inserted = realRepo.Insert(testDefinition);
        try
        {
            inserted.Id.Should().BeGreaterThan(0);

            var enabledNotifications = realRepo.GetEnabled().ToList();
            enabledNotifications.Should().Contain(n => n.Id == inserted.Id);

            var dbIntegratedHandler = new NotificationEventHandler(
                realRepo,
                this.webhookDispatcher,
                this.customScriptService,
                this.configService,
                this.mediaEnrichmentService,
                this.torrentRepository,
                this.torrentFileRepository,
                this.downloadEngine);

            var sampleTorrent = CreateSampleTorrent(122, "RealDb.Integration.Torrent.iso");
            dbIntegratedHandler.Handle(new TorrentAddedEvent { Torrent = sampleTorrent });

            var dispatch = await this.WaitForDispatchAsync();
            dispatch.Should().NotBeNull();
            dispatch.TargetUrl.Should().Be("https://webhook.database.local/ingest");

            var json = JsonSerializer.Serialize(dispatch.Payload);
            json.Should().Contain("\"eventType\":\"OnGrab\"");
            json.Should().Contain("RealDb.Integration.Torrent.iso");
        }
        finally
        {
            if (inserted.Id > 0)
            {
                realRepo.Delete(inserted.Id);
            }
        }
    }
}

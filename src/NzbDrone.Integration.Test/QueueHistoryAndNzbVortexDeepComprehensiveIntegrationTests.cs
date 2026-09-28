// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class QueueHistoryAndNzbVortexDeepComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task NzbVortexApi_AuthAppInfoAndQueue_ReturnsExpectedVortexResponses()
    {
        // 1. Nonce
        var nonceResp = await this.Client.GetAsync("/nzbvortex/api/v1/auth/nonce");
        nonceResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var nonceJson = await nonceResp.Content.ReadAsStringAsync();
        using var nonceDoc = JsonDocument.Parse(nonceJson);
        nonceDoc.RootElement.GetProperty("authNonce").GetString().Should().NotBeNullOrWhiteSpace();

        // 2. Login
        var loginResp = await this.Client.GetAsync("/nzbvortex/api/v1/auth/login");
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginJson = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginJson);
        loginDoc.RootElement.GetProperty("auth").GetBoolean().Should().BeTrue();
        var session = loginDoc.RootElement.GetProperty("session").GetString();
        session.Should().NotBeNullOrWhiteSpace();

        // 3. App version and level
        var versionResp = await this.Client.GetAsync($"/nzbvortex/api/v1/app/appversion?session={session}");
        versionResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var levelResp = await this.Client.GetAsync($"/nzbvortex/api/v1/app/apilevel?session={session}");
        levelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Groups
        var groupsResp = await this.Client.GetAsync($"/nzbvortex/api/v1/group?session={session}");
        groupsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Queue and NZBs
        var queueResp = await this.Client.GetAsync($"/nzbvortex/api/v1/queue?session={session}");
        queueResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var nzbResp = await this.Client.GetAsync($"/nzbvortex/api/v1/nzb?session={session}");
        nzbResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Server and Status
        var serverResp = await this.Client.GetAsync($"/nzbvortex/api/v1/server?session={session}");
        serverResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusResp = await this.Client.GetAsync($"/nzbvortex/api/v1/status?session={session}");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var statResp = await this.Client.GetAsync($"/nzbvortex/api/v1/statistic?session={session}");
        statResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task QueueManagerService_ProcessQueueAndEvents_HandlesQueueScheduling()
    {
        var queueManager = (IQueueManagerService)GlobalSetup.Factory.Services.GetService(typeof(IQueueManagerService))!;
        queueManager.Should().NotBeNull();

        // 1. ProcessQueue directly
        await queueManager.ProcessQueueAsync();

        // 2. Trigger events via consumer
        if (queueManager is QueueManagerService service)
        {
            var testTorrent = new Torrent
            {
                Id = 888,
                Name = "QueueTestTorrent",
                Status = TorrentStatus.Downloading,
                Priority = 1,
            };

            service.Handle(new TorrentAddedEvent { Torrent = testTorrent });
            service.Handle(new TorrentStatusChangedEvent { Torrent = testTorrent });
            service.Handle(new ConfigSavedEvent());
            service.Handle(new TorrentDeletedEvent { Torrent = testTorrent });
        }
    }

    [Test]
    public void DownloadHistoryService_Lifecycle_RecordsAndQueriesEntries()
    {
        var historyService = (IDownloadHistoryService)GlobalSetup.Factory.Services.GetService(typeof(IDownloadHistoryService))!;
        historyService.Should().NotBeNull();

        var testTorrent = new Torrent
        {
            Id = 777,
            Name = "HistoryRecordedTorrent.iso",
            InfoHash = "7777777777777777777777777777777777777777",
            Category = "ISOs",
            Downloaded = 1_000_000,
            TotalSize = 2_000_000,
            Status = TorrentStatus.Downloading,
        };

        // 1. Record addition
        var record = historyService.RecordTorrentAdded(testTorrent, source: "Manual", magnetUrl: null, downloadUrl: null, indexerName: "LocalTest");
        record.Should().NotBeNull();

        // 2. Query history
        var all = historyService.GetAll(query: "HistoryRecordedTorrent");
        all.Should().NotBeEmpty();

        var byHash = historyService.GetByInfoHash("7777777777777777777777777777777777777777");
        byHash.Should().NotBeNull();

        // 3. Update and remove records
        historyService.RecordTorrentUpdated(testTorrent);
        historyService.RecordTorrentRemoved(testTorrent, "Completed and cleaned");

        // 4. Prune history
        historyService.PruneHistory(retentionDays: 30);
    }

    [Test]
    public void RssSyncService_MatchesRule_EvaluatesFilterRules()
    {
        var rssService = (IRssSyncService)GlobalSetup.Factory.Services.GetService(typeof(IRssSyncService))!;
        rssService.Should().NotBeNull();

        var release = new TorznabSearchResult
        {
            Title = "Severance.S01E01.1080p.ATVP.WEB-DL.DDP5.1.Atmos.H.264-FLUX",
            Size = 2_500_000_000,
            Category = "5000",
            Seeders = 10,
        };

        // 1. Matching rule
        var matchingRule = new RssRule
        {
            MustContain = "Severance",
            MustNotContain = "2160p",
            MinSizeBytes = 100_000_000,
            MaxSizeBytes = 10_000_000_000,
            MinSeeders = 1,
        };
        rssService.MatchesRule(release, matchingRule).Should().BeTrue();

        // 2. Failing must not contain
        var failingRule1 = new RssRule
        {
            MustContain = "Severance",
            MustNotContain = "1080p",
        };
        rssService.MatchesRule(release, failingRule1).Should().BeFalse();

        // 3. Failing size boundary
        var failingRule2 = new RssRule
        {
            MustContain = "Severance",
            MinSizeBytes = 5_000_000_000,
        };
        rssService.MatchesRule(release, failingRule2).Should().BeFalse();
    }
}

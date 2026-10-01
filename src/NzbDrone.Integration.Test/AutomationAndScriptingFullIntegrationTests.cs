// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class AutomationAndScriptingFullIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Marketplace_TemplateListAndInstallation_ExecutesSuccessfully()
    {
        // 1. Get Marketplace templates
        var marketResp = await this.GetAsync("/api/v1/automation/marketplace");
        marketResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var marketJson = await marketResp.Content.ReadAsStringAsync();
        using var mDoc = JsonDocument.Parse(marketJson);
        mDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        mDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

        var firstTemplate = mDoc.RootElement[0];
        var templateId = firstTemplate.GetProperty("id").GetString()!;

        // 2. Install template
        const string customName = "IntegInstalledTemplateScript";
        var installPayload = new
        {
            templateId = templateId,
            customName = customName,
        };

        var installResp = await this.PostJsonAsync("/api/v1/automation/marketplace/install", installPayload);
        installResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var installJson = await installResp.Content.ReadAsStringAsync();
        using var iDoc = JsonDocument.Parse(installJson);
        var scriptId = iDoc.RootElement.GetProperty("id").GetInt32();
        scriptId.Should().BeGreaterThan(0);

        try
        {
            // 3. Verify in all scripts
            var allResp = await this.GetAsync("/api/v1/automation");
            allResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var allJson = await allResp.Content.ReadAsStringAsync();
            allJson.Should().Contain(customName);
        }
        finally
        {
            // Clean up
            await this.DeleteAsync($"/api/v1/automation/{scriptId}");
        }
    }

    [Test]
    public async Task AutomationScript_TestEvaluation_ReturnsExecutionResult()
    {
        var testPayload = new
        {
            script = new
            {
                name = "DirectTestScript",
                language = 0, // JavaScript
                code = "console.log('Testing script evaluation directly: ' + (torrent ? torrent.name : 'no-torrent'));",
                trigger = 0,
            },
            customInputs = new
            {
                customKey = "customValue",
            },
        };

        var testResp = await this.PostJsonAsync("/api/v1/automation/test", testPayload);
        testResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var testJson = await testResp.Content.ReadAsStringAsync();
        using var tDoc = JsonDocument.Parse(testJson);
        tDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        tDoc.RootElement.TryGetProperty("outputLog", out var log).Should().BeTrue();
        log.GetString().Should().Contain("Testing script evaluation directly");
    }

    [Test]
    public void AutomationEventService_DispatchesAllEvents_ExecutesGracefully()
    {
        var eventAggregator = GlobalSetup.Factory.Services.GetRequiredService<IEventAggregator>();
        var sampleTorrent = new Torrent
        {
            Id = 99,
            Name = "AutomationEventTestTorrent",
            Category = "movies",
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 1000000,
        };

        // Publish lifecycle events handled by AutomationEventService
        eventAggregator.PublishEvent(new TorrentAddedEvent { Torrent = sampleTorrent });
        eventAggregator.PublishEvent(new TorrentDownloadCompletedEvent(sampleTorrent));
        eventAggregator.PublishEvent(new TorrentSeedGoalReachedEvent(sampleTorrent));
        eventAggregator.PublishEvent(new TorrentDeletedEvent { Torrent = sampleTorrent, DeleteFiles = false });
        eventAggregator.PublishEvent(new TorrentStatusChangedEvent { Torrent = sampleTorrent, OldStatus = TorrentStatus.Downloading, NewStatus = TorrentStatus.Paused });
        eventAggregator.PublishEvent(new TorrentUpdatedEvent { Torrent = sampleTorrent });
        eventAggregator.PublishEvent(new CategoryUpdatedEvent { Category = new Category { Id = 1, Name = "movies" } });
        eventAggregator.PublishEvent(new ApplicationStartedEvent());

        // Assert no unhandled exceptions were thrown
        true.Should().BeTrue();
    }
}

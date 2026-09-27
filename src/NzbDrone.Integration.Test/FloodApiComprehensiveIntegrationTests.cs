// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class FloodApiComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string FloodHash = "d8d8d8d8d8d8d8d8d8d8d8d8d8d8d8d8d8d8d8d8";
    private const string FloodName = "FloodApiTestMovie";
    private Torrent testTorrent = null!;
    private TorrentFile testFile = null!;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;
        var torrentFileRepo = (ITorrentFileRepository)services.GetService(typeof(ITorrentFileRepository))!;

        this.testTorrent = new Torrent
        {
            Name = FloodName,
            InfoHash = FloodHash,
            SavePath = "/downloads/movies",
            Category = "movies",
            Status = TorrentStatus.Paused,
            TotalSize = 1048576000L,
            Progress = 0.5,
            DateAdded = DateTime.UtcNow,
        };
        torrentRepo.Insert(this.testTorrent);

        this.testFile = new TorrentFile
        {
            TorrentId = this.testTorrent.Id,
            Path = "movie_sample.mkv",
            Size = 1048576000L,
            PieceOffset = 0,
            PieceCount = 100,
            Priority = 3,
            Progress = 0.5,
        };
        torrentFileRepo.Insert(this.testFile);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            var services = GlobalSetup.Factory.Services;
            var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;
            var torrentFileRepo = (ITorrentFileRepository)services.GetService(typeof(ITorrentFileRepository))!;

            if (this.testFile != null)
            {
                torrentFileRepo.Delete(this.testFile.Id);
            }

            if (this.testTorrent != null)
            {
                torrentRepo.Delete(this.testTorrent.Id);
            }
        }
        catch
        {
        }
    }

    [Test]
    public async Task FloodApi_ClientSettingsAndConnectionTest_OperateSuccessfully()
    {
        // 1. GET /api/client/connection-test
        var connTestResp = await this.Client.GetAsync("/api/client/connection-test");
        connTestResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var connJson = await connTestResp.Content.ReadAsStringAsync();
        using var connDoc = JsonDocument.Parse(connJson);
        connDoc.RootElement.GetProperty("isConnected").GetBoolean().Should().BeTrue();

        // 2. GET /api/client/settings
        var getSettingsResp = await this.Client.GetAsync("/api/client/settings");
        getSettingsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var settingsJson = await getSettingsResp.Content.ReadAsStringAsync();
        using var settingsDoc = JsonDocument.Parse(settingsJson);
        settingsDoc.RootElement.TryGetProperty("directoryDefault", out _).Should().BeTrue();
    }

    [Test]
    public async Task FloodApi_TorrentMutationsAndTagsAndContents_OperateSuccessfully()
    {
        // 1. GET /api/torrents
        var getAllResp = await this.Client.GetAsync("/api/torrents");
        getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getAllJson = await getAllResp.Content.ReadAsStringAsync();
        using var allDoc = JsonDocument.Parse(getAllJson);
        allDoc.RootElement.GetProperty("torrents").TryGetProperty(FloodHash.ToLowerInvariant(), out _).Should().BeTrue();

        // 2. GET /api/torrents/tags
        var getTagsResp = await this.Client.GetAsync("/api/torrents/tags");
        getTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. POST /api/torrents/set-tags
        var setTagsPayload = new
        {
            hashes = new List<string> { FloodHash },
            tags = new List<string> { "movies", "flood-tag" },
        };
        var setTagsResp = await this.PostJsonAsync("/api/torrents/set-tags", setTagsPayload);
        setTagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. POST /api/torrents/start and POST /api/torrents/stop
        var startPayload = new { hashes = new List<string> { FloodHash } };
        var startResp = await this.PostJsonAsync("/api/torrents/start", startPayload);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var stopResp = await this.PostJsonAsync("/api/torrents/stop", startPayload);
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. POST /api/torrents/check-hash
        var recheckResp = await this.PostJsonAsync("/api/torrents/check-hash", startPayload);
        recheckResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. GET /api/torrents/{hash}/contents
        var contentsResp = await this.Client.GetAsync($"/api/torrents/{FloodHash}/contents");
        contentsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var contentsJson = await contentsResp.Content.ReadAsStringAsync();
        using var contentsDoc = JsonDocument.Parse(contentsJson);
        contentsDoc.RootElement.GetArrayLength().Should().Be(1);

        // 7. POST /api/torrents/contents-priority
        var prioPayload = new
        {
            hashes = new List<string> { FloodHash },
            indices = new List<int> { 0 },
            priority = 2, // High priority
        };
        var prioResp = await this.PostJsonAsync("/api/torrents/contents-priority", prioPayload);
        prioResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. POST /api/torrents/reannounce
        var reannouncePayload = new
        {
            hashes = new List<string> { FloodHash },
        };
        var reannounceResp = await this.PostJsonAsync("/api/torrents/reannounce", reannouncePayload);
        reannounceResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 9. GET /api/torrents/{hash}/peers
        var peersResp = await this.Client.GetAsync($"/api/torrents/{FloodHash}/peers");
        peersResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

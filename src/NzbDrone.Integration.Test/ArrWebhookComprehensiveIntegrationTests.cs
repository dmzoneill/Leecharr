// Copyright (c) FeedItOut. All rights reserved.

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
public class ArrWebhookComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string ArrTorrentHash = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";
    private const string ArrTorrentName = "ArrTestMovie.2024.1080p.mkv";
    private Torrent testTorrent = null!;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;

        this.testTorrent = new Torrent
        {
            Name = ArrTorrentName,
            InfoHash = ArrTorrentHash,
            SavePath = "/downloads/movies",
            Category = "movies",
            Status = TorrentStatus.Downloading,
            TotalSize = 1048576000L,
            Progress = 0.5,
            DateAdded = DateTime.UtcNow,
        };
        torrentRepo.Insert(this.testTorrent);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            var services = GlobalSetup.Factory.Services;
            var torrentRepo = (ITorrentRepository)services.GetService(typeof(ITorrentRepository))!;
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
    public async Task ArrWebhook_TestEvents_ReturnSuccessForClientTypes()
    {
        var testPayload = new
        {
            eventType = "Test",
            instanceName = "SonarrInstance",
        };

        // 1. Generic Arr webhook
        var arrResp = await this.PostJsonAsync("/api/v1/webhooks/arr", testPayload);
        arrResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var arrDoc = JsonDocument.Parse(await arrResp.Content.ReadAsStringAsync());
        arrDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // 2. Radarr webhook
        var radarrResp = await this.PostJsonAsync("/api/v1/webhooks/radarr", testPayload);
        radarrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Sonarr webhook
        var sonarrResp = await this.PostJsonAsync("/api/v1/webhooks/sonarr", testPayload);
        sonarrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Lidarr webhook
        var lidarrResp = await this.PostJsonAsync("/api/v1/webhooks/lidarr", testPayload);
        lidarrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Readarr webhook
        var readarrResp = await this.PostJsonAsync("/api/v1/webhooks/readarr", testPayload);
        readarrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Whisparr webhook
        var whisparrResp = await this.PostJsonAsync("/api/v1/webhooks/whisparr", testPayload);
        whisparrResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ArrWebhook_RadarrAndSonarr_GrabAndDownloadEvents_ProcessCleanly()
    {
        // 1. Radarr Grab event
        var radarrGrabPayload = new
        {
            eventType = "Grab",
            instanceName = "Radarr-4K",
            downloadId = ArrTorrentHash,
            movie = new
            {
                id = 42,
                title = "Inception",
                year = 2010,
                tmdbId = 27205,
                imdbId = "tt1375666",
            },
            release = new
            {
                releaseTitle = ArrTorrentName,
                indexer = "ProwlarrIndexer",
                size = 1048576000L,
            },
        };

        var grabResp = await this.PostJsonAsync("/api/v1/webhooks/radarr", radarrGrabPayload);
        grabResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var grabDoc = JsonDocument.Parse(await grabResp.Content.ReadAsStringAsync());
        grabDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // 2. Radarr Download event
        var radarrDownloadPayload = new
        {
            eventType = "Download",
            instanceName = "Radarr-4K",
            downloadId = ArrTorrentHash,
            movie = new
            {
                id = 42,
                title = "Inception",
                year = 2010,
                tmdbId = 27205,
            },
            movieFile = new
            {
                id = 101,
                relativePath = "Inception (2010)/Inception.2010.mkv",
                size = 1048576000L,
            },
        };

        var dlResp = await this.PostJsonAsync("/api/v1/webhooks/radarr", radarrDownloadPayload);
        dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dlDoc = JsonDocument.Parse(await dlResp.Content.ReadAsStringAsync());
        dlDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // 3. Sonarr Grab & Download events
        var sonarrGrabPayload = new
        {
            eventType = "Grab",
            instanceName = "Sonarr-HD",
            downloadId = ArrTorrentHash,
            series = new
            {
                id = 15,
                title = "Breaking Bad",
                tvdbId = 81189,
            },
            episodes = new[]
            {
                new { seasonNumber = 1, episodeNumber = 1, title = "Pilot" },
            },
            release = new
            {
                releaseTitle = ArrTorrentName,
                indexer = "TorznabIndexer",
            },
        };

        var sonarrGrabResp = await this.PostJsonAsync("/api/v1/webhooks/sonarr", sonarrGrabPayload);
        sonarrGrabResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ArrWebhook_LidarrAndReadarr_GrabAndDownloadEvents_ProcessCleanly()
    {
        // 1. Lidarr Grab event
        var lidarrGrab = new
        {
            eventType = "Grab",
            instanceName = "LidarrInstance",
            downloadId = ArrTorrentHash,
            artist = new
            {
                name = "Pink Floyd",
            },
            album = new
            {
                title = "The Dark Side of the Moon",
                releaseDate = "1973-03-01",
            },
        };

        var lidarrResp = await this.PostJsonAsync("/api/v1/webhooks/lidarr", lidarrGrab);
        lidarrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Readarr Grab event
        var readarrGrab = new
        {
            eventType = "Grab",
            instanceName = "ReadarrInstance",
            downloadId = ArrTorrentHash,
            author = new
            {
                name = "George Orwell",
            },
            book = new
            {
                title = "1984",
            },
        };

        var readarrResp = await this.PostJsonAsync("/api/v1/webhooks/readarr", readarrGrab);
        readarrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Prowlarr sync event
        var prowlarrPayload = new
        {
            eventType = "IndexerSync",
            instanceName = "ProwlarrSync",
        };

        var prowlarrResp = await this.PostJsonAsync("/api/v1/webhooks/prowlarr", prowlarrPayload);
        prowlarrResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

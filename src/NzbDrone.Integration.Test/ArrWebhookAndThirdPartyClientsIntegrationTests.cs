// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ArrWebhookAndThirdPartyClientsIntegrationTests : IntegrationTestBase
{
    private const string WebhookHash = "f666666666666666666666666666666666666666";
    private const string WebhookName = "ArrWebhookMovie";

    [Test]
    public async Task ArrWebhook_LifecycleEventsMatrix_Succeeds()
    {
        // Add a torrent first so webhooks have a target to enrich
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{WebhookHash}&dn={WebhookName}",
            category = "NONE",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 1. Test event across endpoints
            var endpoints = new[] { "/api/v1/webhook/arr", "/api/v1/webhook/sonarr", "/api/v1/webhook/radarr", "/api/v1/webhook/lidarr", "/api/v1/webhook/readarr", "/api/v1/webhook/prowlarr" };
            foreach (var ep in endpoints)
            {
                var testResp = await this.PostJsonAsync(ep, new
                {
                    eventType = "Test",
                    instanceName = "TestArrInstance",
                });
                testResp.StatusCode.Should().Be(HttpStatusCode.OK);
                var testDoc = JsonDocument.Parse(await testResp.Content.ReadAsStringAsync());
                testDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            }

            // 2. Grab event from Radarr
            var grabResp = await this.PostJsonAsync("/api/v1/webhook/radarr", new
            {
                eventType = "Grab",
                downloadId = WebhookHash,
                movie = new
                {
                    id = 42,
                    title = "Arr Webhook Movie",
                    year = 2024,
                },
            });
            grabResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Download / Import event from Radarr
            var downloadResp = await this.PostJsonAsync("/api/v1/webhook/radarr", new
            {
                eventType = "MovieImport",
                downloadId = WebhookHash,
                downloadClientId = torrentId.ToString(),
                movieFile = new
                {
                    path = "/media/movies/ArrWebhookMovie (2024)/ArrWebhookMovie.mkv",
                },
                movie = new
                {
                    id = 42,
                    title = "Arr Webhook Movie",
                    year = 2024,
                },
            });
            downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dlDoc = JsonDocument.Parse(await downloadResp.Content.ReadAsStringAsync());
            dlDoc.RootElement.GetProperty("updated").GetBoolean().Should().BeTrue();

            // 4. Rename event
            var renameResp = await this.PostJsonAsync("/api/v1/webhook/radarr", new
            {
                eventType = "Rename",
                downloadId = WebhookHash,
                movie = new { title = "Renamed Movie" },
            });
            renameResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. EpisodeFileDelete / MovieFileDelete
            var deleteFileResp = await this.PostJsonAsync("/api/v1/webhook/radarr", new
            {
                eventType = "MovieFileDelete",
                downloadId = WebhookHash,
            });
            deleteFileResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }

    [Test]
    public async Task HadoukenRpc_MethodsMatrix_Succeeds()
    {
        // 1. core.getsysteminfo
        var sysInfoReq = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "core.getsysteminfo",
            @params = System.Array.Empty<object>(),
        };
        var sysInfoResp = await this.PostJsonAsync("/hadouken/api", sysInfoReq);
        sysInfoResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. webui.getsettings
        var getSettingsReq = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "webui.getsettings",
            @params = System.Array.Empty<object>(),
        };
        var getSettingsResp = await this.PostJsonAsync("/hadouken/api", getSettingsReq);
        getSettingsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. torrents.addurl
        var addReq = new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "torrents.addurl",
            @params = new object[] { $"magnet:?xt=urn:btih:{WebhookHash}&dn=HadoukenMovie", new { } },
        };
        var addResp = await this.PostJsonAsync("/hadouken/api", addReq);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. torrents.getdetails
        var detailsReq = new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "torrents.getdetails",
            @params = new object[] { WebhookHash },
        };
        var detailsResp = await this.PostJsonAsync("/hadouken/api", detailsReq);
        detailsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. torrents.pause & resume
        var pauseReq = new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "torrents.pause",
            @params = new object[] { WebhookHash },
        };
        var pauseResp = await this.PostJsonAsync("/hadouken/api", pauseReq);
        pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resumeReq = new
        {
            jsonrpc = "2.0",
            id = 6,
            method = "torrents.resume",
            @params = new object[] { WebhookHash },
        };
        var resumeResp = await this.PostJsonAsync("/hadouken/api", resumeReq);
        resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. torrents.remove
        var removeReq = new
        {
            jsonrpc = "2.0",
            id = 7,
            method = "torrents.remove",
            @params = new object[] { WebhookHash, false },
        };
        var removeResp = await this.PostJsonAsync("/hadouken/api", removeReq);
        removeResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task SynologyDownloadStation_EndpointsMatrix_Succeeds()
    {
        // 1. SYNO.API.Info
        var infoResp = await this.Client.GetAsync("/webapi/entry.cgi?api=SYNO.API.Info&version=1&method=query");
        infoResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. SYNO.API.Auth
        var authResp = await this.Client.GetAsync("/webapi/entry.cgi?api=SYNO.API.Auth&version=2&method=login&account=admin&passwd=password");
        authResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. SYNO.DownloadStation.Info
        var dsInfoResp = await this.Client.GetAsync("/webapi/entry.cgi?api=SYNO.DownloadStation.Info&version=2&method=getinfo");
        dsInfoResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. SYNO.DownloadStation.Statistic
        var statResp = await this.Client.GetAsync("/webapi/entry.cgi?api=SYNO.DownloadStation.Statistic&version=1&method=getinfo");
        statResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. SYNO.DownloadStation.Task list
        var taskListResp = await this.Client.GetAsync("/webapi/entry.cgi?api=SYNO.DownloadStation.Task&version=1&method=list");
        taskListResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task SabnzbdApi_ModesMatrix_Succeeds()
    {
        var modes = new[] { "version", "queue", "history", "config", "server_stats", "warnings", "pause", "resume" };
        foreach (var m in modes)
        {
            var resp = await this.Client.GetAsync($"/sabnzbd/api?mode={m}&output=json");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}

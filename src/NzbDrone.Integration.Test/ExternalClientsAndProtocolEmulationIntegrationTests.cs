// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ExternalClientsAndProtocolEmulationIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task FreeboxApi_ConfigAuthAndDownloads_ReturnsSuccess()
    {
        // 1. Config
        var configResp = await this.Client.GetAsync("/api/v4/downloads/config");
        configResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var configJson = await configResp.Content.ReadAsStringAsync();
        using var configDoc = JsonDocument.Parse(configJson);
        configDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // 2. Login authorize
        var authResp = await this.Client.GetAsync("/api/v4/login/authorize");
        authResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var authJson = await authResp.Content.ReadAsStringAsync();
        using var authDoc = JsonDocument.Parse(authJson);
        authDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // 3. Downloads list
        var dlResp = await this.Client.GetAsync("/api/v4/downloads");
        dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dlJson = await dlResp.Content.ReadAsStringAsync();
        using var dlDoc = JsonDocument.Parse(dlJson);
        dlDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task SynologyDownloadStationApi_QueryAuthInfoAndTasks_ReturnsSuccess()
    {
        // 1. Query CGI
        var queryResp = await this.Client.GetAsync("/webapi/query.cgi?api=SYNO.API.Info&version=1&method=query");
        queryResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var queryJson = await queryResp.Content.ReadAsStringAsync();
        using var queryDoc = JsonDocument.Parse(queryJson);
        queryDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // 2. Auth CGI login
        var authResp = await this.Client.GetAsync("/webapi/auth.cgi?api=SYNO.API.Auth&version=2&method=login&account=admin&passwd=secret");
        authResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var authJson = await authResp.Content.ReadAsStringAsync();
        using var authDoc = JsonDocument.Parse(authJson);
        authDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // 3. Info CGI
        var infoResp = await this.Client.GetAsync("/webapi/DownloadStation/info.cgi?api=SYNO.DownloadStation.Info&version=1&method=getinfo");
        infoResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Statistic CGI
        var statResp = await this.Client.GetAsync("/webapi/DownloadStation/statistic.cgi?api=SYNO.DownloadStation.Statistic&version=1&method=getinfo");
        statResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Task CGI list
        var taskResp = await this.Client.GetAsync("/webapi/DownloadStation/task.cgi?api=SYNO.DownloadStation.Task&version=1&method=list");
        taskResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task SabnzbdApi_VersionQueueHistoryStatus_ReturnsValidJson()
    {
        var modes = new[] { "version", "queue", "history", "status" };

        foreach (var mode in modes)
        {
            var resp = await this.Client.GetAsync($"/api?mode={mode}&output=json");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await resp.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrWhiteSpace();
            using var doc = JsonDocument.Parse(content);
            doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        }

        // Subpath /sabnzbd/api
        var subpathResp = await this.Client.GetAsync("/sabnzbd/api?mode=version&output=json");
        subpathResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task NzbgetRpc_JsonRpcMethods_ExecutesSuccessfully()
    {
        var rpcMethods = new[] { "version", "status", "listgroups", "history" };

        foreach (var method in rpcMethods)
        {
            var payload = new
            {
                method = method,
                @params = Array.Empty<object>(),
                id = 1,
            };

            var resp = await this.Client.PostAsJsonAsync("/nzbget/jsonrpc", payload);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("result", out _).Should().BeTrue();
        }
    }

    [Test]
    public async Task DelugeJsonRpc_AuthAndTorrentsStatus_ExecutesSuccessfully()
    {
        // 1. auth.login
        var loginPayload = new
        {
            method = "auth.login",
            @params = new object[] { "deluge" },
            id = 1,
        };

        var loginResp = await this.Client.PostAsJsonAsync("/json", loginPayload);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. auth.check_session
        var checkPayload = new
        {
            method = "auth.check_session",
            @params = Array.Empty<object>(),
            id = 2,
        };
        var checkResp = await this.Client.PostAsJsonAsync("/json", checkPayload);
        checkResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. core.get_config
        var configPayload = new
        {
            method = "core.get_config",
            @params = Array.Empty<object>(),
            id = 3,
        };
        var configResp = await this.Client.PostAsJsonAsync("/json", configPayload);
        configResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. web.get_torrents_status
        var statusPayload = new
        {
            method = "web.get_torrents_status",
            @params = new object[] { new { }, Array.Empty<string>() },
            id = 4,
        };
        var statusResp = await this.Client.PostAsJsonAsync("/json", statusPayload);
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task InternalSubsystems_HistoryPeerLogsAndRss_ReturnsCollections()
    {
        // 1. Download history
        var histResp = await this.Client.GetAsync("/api/v1/downloadhistory");
        histResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Peer log
        var peerResp = await this.Client.GetAsync("/api/v1/peerlog");
        peerResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. RSS rules
        var rssResp = await this.Client.GetAsync("/api/v1/rssrules");
        rssResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Media
        var mediaResp = await this.Client.GetAsync("/api/v1/media");
        mediaResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

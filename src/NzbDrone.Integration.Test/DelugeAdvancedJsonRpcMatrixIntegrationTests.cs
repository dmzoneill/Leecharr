// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class DelugeAdvancedJsonRpcMatrixIntegrationTests : IntegrationTestBase
{
    private const string DelugeHash = "a999999999999999999999999999999999999999";
    private const string DelugeName = "DelugeAdvMovie";

    [Test]
    public async Task Deluge_WebAndDaemonRpcMethods_ReturnExpectedResults()
    {
        // 1. web.connected, web.get_version, web.get_plugins
        var webMethods = new[]
        {
            "web.connected",
            "web.get_version",
            "web.get_plugins",
            "web.get_installed_plugins",
            "web.get_hosts",
            "web.get_host_status",
            "web.get_filter_tree",
            "web.get_events",
            "web.get_config",
            "daemon.get_method_list",
            "daemon.get_version",
            "daemon.info",
            "system.listMethods",
        };

        var id = 1;
        foreach (var m in webMethods)
        {
            var req = new
            {
                method = m,
                @params = new object[0],
                id = id++,
            };

            var resp = await this.PostJsonAsync("/json", req);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // 2. web.disconnect
        var disconnReq = new
        {
            method = "web.disconnect",
            @params = new object[0],
            id = id++,
        };
        var disconnResp = await this.PostJsonAsync("/json", disconnReq);
        disconnResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Deluge_PluginRpcMethods_ReturnConfigurations()
    {
        var id = 100;

        // 1. Scheduler plugin
        var schedGetReq = new { method = "scheduler.get_config", @params = new object[0], id = id++ };
        var schedGetResp = await this.PostJsonAsync("/json", schedGetReq);
        schedGetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var schedEnableReq = new { method = "scheduler.enable", @params = new object[0], id = id++ };
        var schedEnableResp = await this.PostJsonAsync("/json", schedEnableReq);
        schedEnableResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. AutoAdd plugin
        var autoAddGetReq = new { method = "autoadd.get_watchdirs", @params = new object[0], id = id++ };
        var autoAddGetResp = await this.PostJsonAsync("/json", autoAddGetReq);
        autoAddGetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Blocklist plugin
        var blkGetReq = new { method = "blocklist.get_config", @params = new object[0], id = id++ };
        var blkGetResp = await this.PostJsonAsync("/json", blkGetReq);
        blkGetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var blkStatusReq = new { method = "blocklist.get_status", @params = new object[0], id = id++ };
        var blkStatusResp = await this.PostJsonAsync("/json", blkStatusReq);
        blkStatusResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Extractor & Execute plugins
        var extGetReq = new { method = "extractor.get_config", @params = new object[0], id = id++ };
        var extGetResp = await this.PostJsonAsync("/json", extGetReq);
        extGetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var execGetReq = new { method = "execute.get_commands", @params = new object[0], id = id++ };
        var execGetResp = await this.PostJsonAsync("/json", execGetReq);
        execGetResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Deluge_LabelPluginAndTorrentSetters_ExecutesSuccessfully()
    {
        // Add a temporary torrent
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{DelugeHash}&dn={DelugeName}",
            category = "deluge-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            var id = 200;

            // 1. label.get_labels
            var getLabelsReq = new { method = "label.get_labels", @params = new object[0], id = id++ };
            var getLabelsResp = await this.PostJsonAsync("/json", getLabelsReq);
            getLabelsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. label.add & label.set_torrent
            var addLabelReq = new { method = "label.add", @params = new object[] { "special-label" }, id = id++ };
            var addLabelResp = await this.PostJsonAsync("/json", addLabelReq);
            addLabelResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setLabelReq = new { method = "label.set_torrent", @params = new object[] { DelugeHash, "special-label" }, id = id++ };
            var setLabelResp = await this.PostJsonAsync("/json", setLabelReq);
            setLabelResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Core property setters
            var setters = new Dictionary<string, object>
            {
                { "core.set_torrent_max_connections", 100 },
                { "core.set_torrent_max_upload_slots", 10 },
                { "core.set_torrent_auto_managed", true },
                { "core.set_torrent_max_download_speed", 2048 },
                { "core.set_torrent_max_upload_speed", 1024 },
                { "core.set_torrent_stop_at_ratio", true },
                { "core.set_torrent_stop_ratio", 2.0 },
                { "core.set_torrent_prioritize_first_last", true },
                { "core.set_torrent_super_seeding", false },
            };

            foreach (var (m, val) in setters)
            {
                var setReq = new
                {
                    method = m,
                    @params = new object[] { DelugeHash, val },
                    id = id++,
                };

                var setResp = await this.PostJsonAsync("/json", setReq);
                setResp.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }

    [Test]
    public async Task Deluge_StatsAndCoreConfig_ReturnSystemInformation()
    {
        var id = 300;

        // 1. stats.get_stats unfiltered
        var statsReq = new { method = "stats.get_stats", @params = new object[0], id = id++ };
        var statsResp = await this.PostJsonAsync("/json", statsReq);
        statsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statsDoc = JsonDocument.Parse(await statsResp.Content.ReadAsStringAsync());
        statsDoc.RootElement.GetProperty("result").GetProperty("free_space").GetInt64().Should().BeGreaterThanOrEqualTo(0);

        // 2. stats.get_stats filtered by specific keys
        var filteredStatsReq = new
        {
            method = "stats.get_stats",
            @params = new object[] { new string[] { "download_rate", "upload_rate", "free_space" } },
            id = id++,
        };
        var filteredResp = await this.PostJsonAsync("/json", filteredStatsReq);
        filteredResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filteredDoc = JsonDocument.Parse(await filteredResp.Content.ReadAsStringAsync());
        filteredDoc.RootElement.GetProperty("result").TryGetProperty("free_space", out _).Should().BeTrue();

        // 3. core.get_config
        var getCfgReq = new { method = "core.get_config", @params = new object[0], id = id++ };
        var getCfgResp = await this.PostJsonAsync("/json", getCfgReq);
        getCfgResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. core.set_config
        var setCfgReq = new
        {
            method = "core.set_config",
            @params = new object[]
            {
                new Dictionary<string, object>
                {
                    { "max_download_speed", 5120 },
                    { "max_upload_speed", 2048 },
                },
            },
            id = id++,
        };
        var setCfgResp = await this.PostJsonAsync("/json", setCfgReq);
        setCfgResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. core.get_free_space, core.get_path_size, core.get_listen_port, core.get_session_status
        var freeSpaceReq = new { method = "core.get_free_space", @params = new object[] { "/downloads" }, id = id++ };
        var freeSpaceResp = await this.PostJsonAsync("/json", freeSpaceReq);
        freeSpaceResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listenPortReq = new { method = "core.get_listen_port", @params = new object[0], id = id++ };
        var listenPortResp = await this.PostJsonAsync("/json", listenPortReq);
        listenPortResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var sessStatusReq = new { method = "core.get_session_status", @params = new object[] { new string[] { "has_incoming_connections", "upload_rate" } }, id = id++ };
        var sessStatusResp = await this.PostJsonAsync("/json", sessStatusReq);
        sessStatusResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. web.update_ui
        var updateUiReq = new
        {
            method = "web.update_ui",
            @params = new object[]
            {
                new string[] { "name", "state", "progress" },
                new Dictionary<string, object>(),
            },
            id = id++,
        };
        var updateUiResp = await this.PostJsonAsync("/json", updateUiReq);
        updateUiResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Deluge_CoreQueueAndStorageManagement_ExecutesSuccessfully()
    {
        const string queueHash = "b555555555555555555555555555555555555555";
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{queueHash}&dn=DelugeQueueTestMovie",
            category = "deluge-queue-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            var id = 400;

            // 1. web.get_torrent_status
            var getStatusReq = new
            {
                method = "web.get_torrent_status",
                @params = new object[]
                {
                    queueHash,
                    new string[] { "name", "state", "progress", "save_path", "total_size" },
                },
                id = id++,
            };
            var getStatusResp = await this.PostJsonAsync("/json", getStatusReq);
            getStatusResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. web.get_torrent_files
            var getFilesReq = new { method = "web.get_torrent_files", @params = new object[] { queueHash }, id = id++ };
            var getFilesResp = await this.PostJsonAsync("/json", getFilesReq);
            getFilesResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Queue operations: queue_top, queue_bottom, queue_up, queue_down
            var queueMethods = new[] { "core.queue_top", "core.queue_up", "core.queue_down", "core.queue_bottom" };
            foreach (var qm in queueMethods)
            {
                var qReq = new { method = qm, @params = new object[] { new string[] { queueHash } }, id = id++ };
                var qResp = await this.PostJsonAsync("/json", qReq);
                qResp.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 4. Force recheck and reannounce
            var recheckReq = new { method = "core.force_recheck", @params = new object[] { new string[] { queueHash } }, id = id++ };
            var recheckResp = await this.PostJsonAsync("/json", recheckReq);
            recheckResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var reannounceReq = new { method = "core.force_reannounce", @params = new object[] { new string[] { queueHash } }, id = id++ };
            var reannounceResp = await this.PostJsonAsync("/json", reannounceReq);
            reannounceResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Pause and resume
            var pauseReq = new { method = "core.pause_torrent", @params = new object[] { new string[] { queueHash } }, id = id++ };
            var pauseResp = await this.PostJsonAsync("/json", pauseReq);
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var resumeReq = new { method = "core.resume_torrent", @params = new object[] { new string[] { queueHash } }, id = id++ };
            var resumeResp = await this.PostJsonAsync("/json", resumeReq);
            resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. Move storage
            var moveReq = new { method = "core.move_storage", @params = new object[] { new string[] { queueHash }, "/downloads/moved" }, id = id++ };
            var moveResp = await this.PostJsonAsync("/json", moveReq);
            moveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=true");
        }
    }
}

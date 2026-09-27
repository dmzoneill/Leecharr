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
}

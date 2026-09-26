// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class RTorrentAndUTorrentFullRpcMatrixIntegrationTests : IntegrationTestBase
{
    private const string RTHash = "c444444444444444444444444444444444444444";
    private const string RTName = "RTorrentMatrixMovie";

    [Test]
    public async Task RTorrent_XmlRpc_SystemAndCapabilitiesMethods_ReturnsValidData()
    {
        // 1. system.listMethods
        var listMethodsXml = "<?xml version=\"1.0\"?><methodCall><methodName>system.listMethods</methodName></methodCall>";
        var listResp = await this.Client.PostAsync("/RPC2", new StringContent(listMethodsXml, Encoding.UTF8, "text/xml"));
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listXml = await listResp.Content.ReadAsStringAsync();
        listXml.Should().Contain("d.multicall2");
        listXml.Should().Contain("system.listMethods");

        // 2. system.client_version & system.api_version
        var clientVerXml = "<?xml version=\"1.0\"?><methodCall><methodName>system.client_version</methodName></methodCall>";
        var clientVerResp = await this.Client.PostAsync("/RPC2", new StringContent(clientVerXml, Encoding.UTF8, "text/xml"));
        clientVerResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var apiVerXml = "<?xml version=\"1.0\"?><methodCall><methodName>system.api_version</methodName></methodCall>";
        var apiVerResp = await this.Client.PostAsync("/RPC2", new StringContent(apiVerXml, Encoding.UTF8, "text/xml"));
        apiVerResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. get_directory
        var dirXml = "<?xml version=\"1.0\"?><methodCall><methodName>get_directory</methodName></methodCall>";
        var dirResp = await this.Client.PostAsync("/RPC2", new StringContent(dirXml, Encoding.UTF8, "text/xml"));
        dirResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task RTorrent_XmlRpc_GettersSettersAndMulticalls_Succeeds()
    {
        // Add a torrent
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{RTHash}&dn={RTName}",
            category = "r-matrix-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 1. d.get_name, d.get_hash, d.get_size_bytes, d.get_custom1
            var methodsToTest = new[]
            {
                "d.get_name",
                "d.get_hash",
                "d.get_size_bytes",
                "d.get_bytes_done",
                "d.get_up_rate",
                "d.get_down_rate",
                "d.get_complete",
                "d.is_active",
                "d.is_open",
                "d.get_custom1",
                "d.get_peers_connected",
                "d.get_peers_not_connected",
                "d.get_peers_complete",
                "d.get_priority",
                "d.get_chunk_size",
                "d.get_is_hash_checking",
            };

            foreach (var m in methodsToTest)
            {
                var xml = $"<?xml version=\"1.0\"?><methodCall><methodName>{m}</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
                var resp = await this.Client.PostAsync("/RPC2", new StringContent(xml, Encoding.UTF8, "text/xml"));
                resp.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 2. Setters: d.set_custom1, d.priority.set, d.check_hash
            var setCustomXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.set_custom1</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string>custom_tag_value</string></value></param></params></methodCall>";
            var setResp = await this.Client.PostAsync("/RPC2", new StringContent(setCustomXml, Encoding.UTF8, "text/xml"));
            setResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setPrioXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.priority.set</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><i4>2</i4></param></params></methodCall>";
            var prioResp = await this.Client.PostAsync("/RPC2", new StringContent(setPrioXml, Encoding.UTF8, "text/xml"));
            prioResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var checkHashXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.check_hash</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var checkResp = await this.Client.PostAsync("/RPC2", new StringContent(checkHashXml, Encoding.UTF8, "text/xml"));
            checkResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. d.multicall2 with various views: main, started, stopped, complete
            var views = new[] { "main", "started", "stopped", "complete", "incomplete", "hashing" };
            foreach (var v in views)
            {
                var multiXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.multicall2</methodName><params><param><value><string></string></value></param><param><value><string>{v}</string></value></param><param><value><string>d.hash=</string></value></param><param><value><string>d.name=</string></value></param></params></methodCall>";
                var multiResp = await this.Client.PostAsync("/RPC2", new StringContent(multiXml, Encoding.UTF8, "text/xml"));
                multiResp.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 4. f.multicall, t.multicall, p.multicall
            var fMultiXml = $"<?xml version=\"1.0\"?><methodCall><methodName>f.multicall</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string></string></value></param><param><value><string>f.get_path=</string></value></param><param><value><string>f.get_size_bytes=</string></value></param></params></methodCall>";
            var fResp = await this.Client.PostAsync("/RPC2", new StringContent(fMultiXml, Encoding.UTF8, "text/xml"));
            fResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var tMultiXml = $"<?xml version=\"1.0\"?><methodCall><methodName>t.multicall</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string></string></value></param><param><value><string>t.get_url=</string></value></param><param><value><string>t.get_type=</string></value></param></params></methodCall>";
            var tResp = await this.Client.PostAsync("/RPC2", new StringContent(tMultiXml, Encoding.UTF8, "text/xml"));
            tResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var pMultiXml = $"<?xml version=\"1.0\"?><methodCall><methodName>p.multicall</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string></string></value></param><param><value><string>p.get_address=</string></value></param><param><value><string>p.get_port=</string></value></param></params></methodCall>";
            var pResp = await this.Client.PostAsync("/RPC2", new StringContent(pMultiXml, Encoding.UTF8, "text/xml"));
            pResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }

    [Test]
    public async Task UTorrent_WebUi_ActionMatrixAndSettings_Succeeds()
    {
        // Add a torrent
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{RTHash}&dn={RTName}",
            category = "ut-matrix-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 1. Get token
            var tokenResp = await this.Client.GetAsync("/gui/token.html");
            tokenResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. Query torrent list
            var listResp = await this.Client.GetAsync("/gui/?list=1");
            listResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Test lifecycle actions: start, pause, unpause, forcestart, stop, recheck
            var actions = new[] { "start", "pause", "unpause", "forcestart", "stop", "recheck" };
            foreach (var act in actions)
            {
                var actResp = await this.Client.PostAsync($"/gui/?action={act}&hash={RTHash}", new StringContent(string.Empty));
                actResp.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 4. Test queue actions: queueup, queuedown, queuetop, queuebottom
            var queueActions = new[] { "queueup", "queuedown", "queuetop", "queuebottom" };
            foreach (var qAct in queueActions)
            {
                var qResp = await this.Client.PostAsync($"/gui/?action={qAct}&hash={RTHash}", new StringContent(string.Empty));
                qResp.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 5. Test setprops with various properties: label, seed_ratio, seed_time, dlrate, ulrate
            var props = new Dictionary<string, string>
            {
                { "label", "new-category-ut" },
                { "seed_ratio", "2500" },
                { "seed_time", "7200" },
                { "dlrate", "512000" },
                { "ulrate", "256000" },
            };

            foreach (var (propName, propVal) in props)
            {
                var setPropResp = await this.Client.PostAsync($"/gui/?action=setprops&hash={RTHash}&s={propName}&v={propVal}", new StringContent(string.Empty));
                setPropResp.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 6. Test getprops & getfiles
            var getPropsResp = await this.Client.GetAsync($"/gui/?action=getprops&hash={RTHash}");
            getPropsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var getFilesResp = await this.Client.GetAsync($"/gui/?action=getfiles&hash={RTHash}");
            getFilesResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 7. Test getsettings & setsetting
            var getSettingsResp = await this.Client.GetAsync("/gui/?action=getsettings");
            getSettingsResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setSettingResp = await this.Client.PostAsync("/gui/?action=setsetting&s=max_dl_rate&v=1000", new StringContent(string.Empty));
            setSettingResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }
}

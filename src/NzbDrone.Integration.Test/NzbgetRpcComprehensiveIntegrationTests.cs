// Copyright (c) FeedItOut. All rights reserved.

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
public class NzbgetRpcComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string NzbgetHash = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";
    private const string NzbgetName = "NzbgetIntegrationItem";

    [Test]
    public async Task Nzbget_JsonRpc_SystemAndStatusMethods_ReturnValidData()
    {
        // 1. version
        var verReq = new { method = "version", @params = new object[0], id = 1 };
        var verResp = await this.PostJsonAsync("/nzbget/jsonrpc", verReq);
        verResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verDoc = JsonDocument.Parse(await verResp.Content.ReadAsStringAsync());
        verDoc.RootElement.GetProperty("result").GetString().Should().Be("24.0");

        // 2. config & loadconfig
        var cfgReq = new { method = "config", @params = new object[0], id = 2 };
        var cfgResp = await this.PostJsonAsync("/nzbget/jsonrpc", cfgReq);
        cfgResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. status
        var statusReq = new { method = "status", @params = new object[0], id = 3 };
        var statusResp = await this.PostJsonAsync("/nzbget/jsonrpc", statusReq);
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusDoc = JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync());
        var res = statusDoc.RootElement.GetProperty("result");
        (res.TryGetProperty("downloadRate", out _) || res.TryGetProperty("DownloadRate", out _)).Should().BeTrue();

        // 4. listgroups
        var groupsReq = new { method = "listgroups", @params = new object[0], id = 4 };
        var groupsResp = await this.PostJsonAsync("/nzbget/jsonrpc", groupsReq);
        groupsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. history
        var histReq = new { method = "history", @params = new object[0], id = 5 };
        var histResp = await this.PostJsonAsync("/nzbget/jsonrpc", histReq);
        histResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. rate limit
        var rateReq = new { method = "rate", @params = new object[] { 10240 }, id = 6 };
        var rateResp = await this.PostJsonAsync("/nzbget/jsonrpc", rateReq);
        rateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. pause & resume post
        var pausePostReq = new { method = "pausepost", @params = new object[0], id = 7 };
        var pausePostResp = await this.PostJsonAsync("/nzbget/jsonrpc", pausePostReq);
        pausePostResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resumePostReq = new { method = "resumepost", @params = new object[0], id = 8 };
        var resumePostResp = await this.PostJsonAsync("/nzbget/jsonrpc", resumePostReq);
        resumePostResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. log
        var logReq = new { method = "log", @params = new object[0], id = 9 };
        var logResp = await this.PostJsonAsync("/nzbget/jsonrpc", logReq);
        logResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Nzbget_JsonRpc_AppendTorrentAndLifecycle_OperatesCorrectly()
    {
        // 1. Append via magnet URL in params[0]
        var magnetUri = $"magnet:?xt=urn:btih:{NzbgetHash}&dn={NzbgetName}";
        var appendReq = new
        {
            method = "append",
            @params = new object[]
            {
                magnetUri,
                string.Empty,
                "nzbget-cat",
                0,
                false,
                true,
            },
            id = 20,
        };
        var appendResp = await this.PostJsonAsync("/nzbget/jsonrpc", appendReq);
        appendResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var appendDoc = JsonDocument.Parse(await appendResp.Content.ReadAsStringAsync());
        var addedId = appendDoc.RootElement.GetProperty("result").GetInt32();
        addedId.Should().BeGreaterThan(0);

        try
        {
            // 2. listfiles for the added torrent
            var listFilesReq = new { method = "listfiles", @params = new object[] { addedId }, id = 21 };
            var listFilesResp = await this.PostJsonAsync("/nzbget/jsonrpc", listFilesReq);
            listFilesResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. pause and resume global
            var pauseReq = new { method = "pause", @params = new object[0], id = 22 };
            var pauseResp = await this.PostJsonAsync("/nzbget/jsonrpc", pauseReq);
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var resumeReq = new { method = "resume", @params = new object[0], id = 23 };
            var resumeResp = await this.PostJsonAsync("/nzbget/jsonrpc", resumeReq);
            resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{addedId}?deleteData=true");
        }
    }

    [Test]
    public async Task Nzbget_JsonRpc_EditQueue_ExecutesQueueCommands()
    {
        // Add a temporary torrent
        var magnetUri = $"magnet:?xt=urn:btih:e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2&dn=NzbgetQueueItem";
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = magnetUri,
            category = "nzbget-queue-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            var id = 30;

            // 1. grouppause and groupresume
            var pauseReq = new { method = "editqueue", @params = new object[] { "GroupPause", 0, string.Empty, new int[] { torrentId } }, id = id++ };
            var pauseResp = await this.PostJsonAsync("/nzbget/jsonrpc", pauseReq);
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var resumeReq = new { method = "editqueue", @params = new object[] { "GroupResume", 0, string.Empty, new int[] { torrentId } }, id = id++ };
            var resumeResp = await this.PostJsonAsync("/nzbget/jsonrpc", resumeReq);
            resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. Queue movement: GroupMoveTop, GroupMoveUp, GroupMoveDown, GroupMoveBottom, GroupMoveOffset
            var moveCmds = new[] { "GroupMoveTop", "GroupMoveUp", "GroupMoveDown", "GroupMoveBottom", "GroupMoveOffset" };
            foreach (var cmd in moveCmds)
            {
                var moveReq = new { method = "editqueue", @params = new object[] { cmd, 1, string.Empty, new int[] { torrentId } }, id = id++ };
                var moveResp = await this.PostJsonAsync("/nzbget/jsonrpc", moveReq);
                moveResp.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 3. groupsetcategory, groupsetname, groupsetpriority
            var setCatReq = new { method = "editqueue", @params = new object[] { "GroupSetCategory", 0, "new-nzb-cat", new int[] { torrentId } }, id = id++ };
            var setCatResp = await this.PostJsonAsync("/nzbget/jsonrpc", setCatReq);
            setCatResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setNameReq = new { method = "editqueue", @params = new object[] { "GroupSetName", 0, "RenamedNzbgetItem", new int[] { torrentId } }, id = id++ };
            var setNameResp = await this.PostJsonAsync("/nzbget/jsonrpc", setNameReq);
            setNameResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var setPrioReq = new { method = "editqueue", @params = new object[] { "GroupSetPriority", 100, string.Empty, new int[] { torrentId } }, id = id++ };
            var setPrioResp = await this.PostJsonAsync("/nzbget/jsonrpc", setPrioReq);
            setPrioResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteData=true");
        }
    }

    [Test]
    public async Task Nzbget_XmlRpc_ComprehensiveMatrix_ReturnsValidXml()
    {
        // 1. XML-RPC version
        var verXml = "<?xml version=\"1.0\"?><methodCall><methodName>version</methodName></methodCall>";
        var verResp = await this.Client.PostAsync("/nzbget/xmlrpc", new StringContent(verXml, Encoding.UTF8, "text/xml"));
        verResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verResult = await verResp.Content.ReadAsStringAsync();
        verResult.Should().Contain("24.0");

        // 2. XML-RPC status
        var statusXml = "<?xml version=\"1.0\"?><methodCall><methodName>status</methodName></methodCall>";
        var statusResp = await this.Client.PostAsync("/nzbget/xmlrpc", new StringContent(statusXml, Encoding.UTF8, "text/xml"));
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusResult = await statusResp.Content.ReadAsStringAsync();
        statusResult.Should().Contain("DownloadRate");

        // 3. XML-RPC listgroups
        var listGroupsXml = "<?xml version=\"1.0\"?><methodCall><methodName>listgroups</methodName></methodCall>";
        var lgResp = await this.Client.PostAsync("/nzbget/xmlrpc", new StringContent(listGroupsXml, Encoding.UTF8, "text/xml"));
        lgResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. XML-RPC history
        var histXml = "<?xml version=\"1.0\"?><methodCall><methodName>history</methodName></methodCall>";
        var histResp = await this.Client.PostAsync("/nzbget/xmlrpc", new StringContent(histXml, Encoding.UTF8, "text/xml"));
        histResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. XML-RPC config
        var configXml = "<?xml version=\"1.0\"?><methodCall><methodName>config</methodName></methodCall>";
        var cfgResp = await this.Client.PostAsync("/nzbget/xmlrpc", new StringContent(configXml, Encoding.UTF8, "text/xml"));
        cfgResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

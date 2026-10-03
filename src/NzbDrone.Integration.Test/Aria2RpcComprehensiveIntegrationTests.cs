// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using MonoTorrent.BEncoding;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class Aria2RpcComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string Aria2Hash = "a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2";
    private const string Aria2Name = "Aria2IntegrationTestItem";

    [Test]
    public async Task Aria2_JsonRpc_SystemAndSessionMethods_ReturnValidResponses()
    {
        // 1. aria2.getVersion via /jsonrpc
        var getVersionPayload = new
        {
            jsonrpc = "2.0",
            id = "test-ver-1",
            method = "aria2.getVersion",
            @params = System.Array.Empty<object>(),
        };
        var verResp = await this.PostJsonAsync("/jsonrpc", getVersionPayload);
        verResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verDoc = JsonDocument.Parse(await verResp.Content.ReadAsStringAsync());
        verDoc.RootElement.GetProperty("result").GetProperty("version").GetString().Should().Be("1.36.0");
        verDoc.RootElement.GetProperty("result").GetProperty("enabledFeatures").GetArrayLength().Should().BeGreaterThan(0);

        // 2. aria2.getSessionInfo via /rpc
        var getSessionPayload = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "aria2.getSessionInfo",
            @params = System.Array.Empty<object>(),
        };
        var sessionResp = await this.PostJsonAsync("/rpc", getSessionPayload);
        sessionResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionDoc = JsonDocument.Parse(await sessionResp.Content.ReadAsStringAsync());
        sessionDoc.RootElement.GetProperty("result").GetProperty("sessionId").GetString().Should().NotBeNullOrWhiteSpace();

        // 3. aria2.getGlobalStat via /aria2/jsonrpc
        var getStatPayload = new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "aria2.getGlobalStat",
            @params = System.Array.Empty<object>(),
        };
        var statResp = await this.PostJsonAsync("/aria2/jsonrpc", getStatPayload);
        statResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statDoc = JsonDocument.Parse(await statResp.Content.ReadAsStringAsync());
        statDoc.RootElement.GetProperty("result").GetProperty("downloadSpeed").GetString().Should().NotBeNull();
        statDoc.RootElement.GetProperty("result").GetProperty("uploadSpeed").GetString().Should().NotBeNull();

        // 4. aria2.getGlobalOption and aria2.changeGlobalOption
        var getGlobalOptPayload = new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "aria2.getGlobalOption",
            @params = System.Array.Empty<object>(),
        };
        var getOptResp = await this.PostJsonAsync("/jsonrpc", getGlobalOptPayload);
        getOptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var changeGlobalOptPayload = new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "aria2.changeGlobalOption",
            @params = new object[]
            {
                new Dictionary<string, string>
                {
                    { "max-overall-download-limit", "1048576" },
                    { "max-overall-upload-limit", "524288" },
                },
            },
        };
        var changeOptResp = await this.PostJsonAsync("/jsonrpc", changeGlobalOptPayload);
        changeOptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. system.listMethods
        var listMethodsPayload = new
        {
            jsonrpc = "2.0",
            id = 6,
            method = "system.listMethods",
            @params = System.Array.Empty<object>(),
        };
        var listResp = await this.PostJsonAsync("/jsonrpc", listMethodsPayload);
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        var methods = listDoc.RootElement.GetProperty("result");
        methods.GetArrayLength().Should().BeGreaterThan(10);
    }

    [Test]
    public async Task Aria2_JsonRpc_BatchAndMulticallExecution_Succeeds()
    {
        // 1. JSON-RPC Batch request (JSON array of multiple calls)
        var batchPayload = new object[]
        {
            new { jsonrpc = "2.0", id = "batch-1", method = "aria2.getVersion", @params = System.Array.Empty<object>() },
            new { jsonrpc = "2.0", id = "batch-2", method = "aria2.getSessionInfo", @params = System.Array.Empty<object>() },
            new { jsonrpc = "2.0", id = "batch-3", method = "aria2.getGlobalStat", @params = System.Array.Empty<object>() },
        };
        var batchResp = await this.PostJsonAsync("/jsonrpc", batchPayload);
        batchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchDoc = JsonDocument.Parse(await batchResp.Content.ReadAsStringAsync());
        batchDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        batchDoc.RootElement.GetArrayLength().Should().Be(3);

        // 2. system.multicall
        var multicallPayload = new
        {
            jsonrpc = "2.0",
            id = "multi-1",
            method = "system.multicall",
            @params = new object[]
            {
                new object[]
                {
                    new { methodName = "aria2.getVersion", @params = System.Array.Empty<object>() },
                    new { methodName = "aria2.getGlobalStat", @params = System.Array.Empty<object>() },
                },
            },
        };
        var multiResp = await this.PostJsonAsync("/jsonrpc", multicallPayload);
        multiResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var multiDoc = JsonDocument.Parse(await multiResp.Content.ReadAsStringAsync());
        multiDoc.RootElement.GetProperty("result").GetArrayLength().Should().Be(2);
    }

    [Test]
    public async Task Aria2_JsonRpc_TorrentLifecycleAndQueries_OperatesCorrectly()
    {
        // Add a torrent first so we have a valid GID
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{Aria2Hash}&dn={Aria2Name}",
            category = "aria2-test-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();
        var gid = Aria2Hash[..16];

        try
        {
            // 1. aria2.tellActive, aria2.tellWaiting, aria2.tellStopped
            var tellActivePayload = new { jsonrpc = "2.0", id = 10, method = "aria2.tellActive", @params = System.Array.Empty<object>() };
            var actResp = await this.PostJsonAsync("/jsonrpc", tellActivePayload);
            actResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var tellWaitingPayload = new { jsonrpc = "2.0", id = 11, method = "aria2.tellWaiting", @params = new object[] { 0, 100 } };
            var waitResp = await this.PostJsonAsync("/jsonrpc", tellWaitingPayload);
            waitResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var tellStoppedPayload = new { jsonrpc = "2.0", id = 12, method = "aria2.tellStopped", @params = new object[] { 0, 100 } };
            var stopResp = await this.PostJsonAsync("/jsonrpc", tellStoppedPayload);
            stopResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. aria2.tellStatus for existing GID and non-existing GID
            var tellStatusPayload = new { jsonrpc = "2.0", id = 13, method = "aria2.tellStatus", @params = new object[] { gid } };
            var statusResp = await this.PostJsonAsync("/jsonrpc", tellStatusPayload);
            statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var statusDoc = JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync());
            statusDoc.RootElement.GetProperty("result").GetProperty("gid").GetString().Should().Be(gid);

            var tellNonExistentPayload = new { jsonrpc = "2.0", id = 14, method = "aria2.tellStatus", @params = new object[] { "999999" } };
            var nonExistentResp = await this.PostJsonAsync("/jsonrpc", tellNonExistentPayload);
            nonExistentResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. aria2.getUris, aria2.getFiles, aria2.getPeers, aria2.getServers
            var getUrisPayload = new { jsonrpc = "2.0", id = 15, method = "aria2.getUris", @params = new object[] { gid } };
            var urisResp = await this.PostJsonAsync("/jsonrpc", getUrisPayload);
            urisResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var getFilesPayload = new { jsonrpc = "2.0", id = 16, method = "aria2.getFiles", @params = new object[] { gid } };
            var filesResp = await this.PostJsonAsync("/jsonrpc", getFilesPayload);
            filesResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var getPeersPayload = new { jsonrpc = "2.0", id = 17, method = "aria2.getPeers", @params = new object[] { gid } };
            var peersResp = await this.PostJsonAsync("/jsonrpc", getPeersPayload);
            peersResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var getServersPayload = new { jsonrpc = "2.0", id = 18, method = "aria2.getServers", @params = new object[] { gid } };
            var serversResp = await this.PostJsonAsync("/jsonrpc", getServersPayload);
            serversResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. aria2.getOption and aria2.changeOption
            var getOptionPayload = new { jsonrpc = "2.0", id = 19, method = "aria2.getOption", @params = new object[] { gid } };
            var getOptResp = await this.PostJsonAsync("/jsonrpc", getOptionPayload);
            getOptResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var changeOptionPayload = new
            {
                jsonrpc = "2.0",
                id = 20,
                method = "aria2.changeOption",
                @params = new object[]
                {
                    gid,
                    new Dictionary<string, string> { { "max-download-limit", "204800" } },
                },
            };
            var changeOptResp = await this.PostJsonAsync("/jsonrpc", changeOptionPayload);
            changeOptResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. aria2.pause, aria2.forcePause, aria2.unpause, aria2.forceUnpause
            var pausePayload = new { jsonrpc = "2.0", id = 21, method = "aria2.pause", @params = new object[] { gid } };
            var pauseResp = await this.PostJsonAsync("/jsonrpc", pausePayload);
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var unpausePayload = new { jsonrpc = "2.0", id = 22, method = "aria2.unpause", @params = new object[] { gid } };
            var unpauseResp = await this.PostJsonAsync("/jsonrpc", unpausePayload);
            unpauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. aria2.changePosition and aria2.changeUri
            var changePosPayload = new { jsonrpc = "2.0", id = 23, method = "aria2.changePosition", @params = new object[] { gid, 0, "POS_SET" } };
            var changePosResp = await this.PostJsonAsync("/jsonrpc", changePosPayload);
            changePosResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var changeUriPayload = new { jsonrpc = "2.0", id = 24, method = "aria2.changeUri", @params = new object[] { gid, 1, System.Array.Empty<string>(), System.Array.Empty<string>() } };
            var changeUriResp = await this.PostJsonAsync("/jsonrpc", changeUriPayload);
            changeUriResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 7. aria2.purgeDownloadResult and aria2.removeDownloadResult
            var purgePayload = new { jsonrpc = "2.0", id = 25, method = "aria2.purgeDownloadResult", @params = System.Array.Empty<object>() };
            var purgeResp = await this.PostJsonAsync("/jsonrpc", purgePayload);
            purgeResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var removeResultPayload = new { jsonrpc = "2.0", id = 26, method = "aria2.removeDownloadResult", @params = new object[] { gid } };
            var removeResultResp = await this.PostJsonAsync("/jsonrpc", removeResultPayload);
            removeResultResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteData=true");
        }
    }

    [Test]
    public async Task Aria2_XmlRpc_ComprehensiveMatrix_ReturnsValidXml()
    {
        // 1. XML-RPC aria2.getVersion
        var getVerXml = "<?xml version=\"1.0\"?><methodCall><methodName>aria2.getVersion</methodName></methodCall>";
        var verResp = await this.Client.PostAsync("/rpc", new StringContent(getVerXml, Encoding.UTF8, "text/xml"));
        verResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verXml = await verResp.Content.ReadAsStringAsync();
        verXml.Should().Contain("1.36.0");

        // 2. XML-RPC aria2.getSessionInfo
        var getSessionXml = "<?xml version=\"1.0\"?><methodCall><methodName>aria2.getSessionInfo</methodName></methodCall>";
        var sessionResp = await this.Client.PostAsync("/rpc", new StringContent(getSessionXml, Encoding.UTF8, "text/xml"));
        sessionResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionXml = await sessionResp.Content.ReadAsStringAsync();
        sessionXml.Should().Contain("sessionId");

        // 3. XML-RPC aria2.getGlobalStat
        var getStatXml = "<?xml version=\"1.0\"?><methodCall><methodName>aria2.getGlobalStat</methodName></methodCall>";
        var statResp = await this.Client.PostAsync("/rpc", new StringContent(getStatXml, Encoding.UTF8, "text/xml"));
        statResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statXml = await statResp.Content.ReadAsStringAsync();
        statXml.Should().Contain("downloadSpeed");

        // 4. XML-RPC aria2.getGlobalOption
        var getGlobalOptXml = "<?xml version=\"1.0\"?><methodCall><methodName>aria2.getGlobalOption</methodName></methodCall>";
        var optResp = await this.Client.PostAsync("/rpc", new StringContent(getGlobalOptXml, Encoding.UTF8, "text/xml"));
        optResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. XML-RPC aria2.tellActive, aria2.tellWaiting, aria2.tellStopped
        var tellActiveXml = "<?xml version=\"1.0\"?><methodCall><methodName>aria2.tellActive</methodName></methodCall>";
        var actResp = await this.Client.PostAsync("/rpc", new StringContent(tellActiveXml, Encoding.UTF8, "text/xml"));
        actResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var tellWaitXml = "<?xml version=\"1.0\"?><methodCall><methodName>aria2.tellWaiting</methodName><params><param><value><string>0</string></value></param><param><value><string>50</string></value></param></params></methodCall>";
        var waitResp = await this.Client.PostAsync("/rpc", new StringContent(tellWaitXml, Encoding.UTF8, "text/xml"));
        waitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var tellStopXml = "<?xml version=\"1.0\"?><methodCall><methodName>aria2.tellStopped</methodName><params><param><value><string>0</string></value></param><param><value><string>50</string></value></param></params></methodCall>";
        var stopResp = await this.Client.PostAsync("/rpc", new StringContent(tellStopXml, Encoding.UTF8, "text/xml"));
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. XML-RPC system.multicall
        var multiXml = "<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data><value><struct><member><name>methodName</name><value><string>aria2.getVersion</string></value></member><member><name>params</name><value><array><data></data></array></value></member></struct></value></data></array></value></param></params></methodCall>";
        var multiResp = await this.Client.PostAsync("/rpc", new StringContent(multiXml, Encoding.UTF8, "text/xml"));
        multiResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var multiXmlResult = await multiResp.Content.ReadAsStringAsync();
        multiXmlResult.Should().Contain("1.36.0");

        // 7. XML-RPC system.listMethods
        var listXml = "<?xml version=\"1.0\"?><methodCall><methodName>system.listMethods</methodName></methodCall>";
        var listResp = await this.Client.PostAsync("/rpc", new StringContent(listXml, Encoding.UTF8, "text/xml"));
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listXmlResult = await listResp.Content.ReadAsStringAsync();
        listXmlResult.Should().Contain("aria2.getVersion");

        // 8. Malformed XML returns fault
        var badXml = "<not-valid-xml";
        var badResp = await this.Client.PostAsync("/rpc", new StringContent(badXml, Encoding.UTF8, "text/xml"));
        badResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var badXmlResult = await badResp.Content.ReadAsStringAsync();
        badXmlResult.Should().Contain("fault");
    }

    [Test]
    public async Task Aria2_GetQueryRpc_SupportsBase64AndStringParams()
    {
        // 1. GET /jsonrpc?method=aria2.getVersion&id=999
        var getVerResp = await this.Client.GetAsync("/jsonrpc?method=aria2.getVersion&id=999");
        getVerResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getVerDoc = JsonDocument.Parse(await getVerResp.Content.ReadAsStringAsync());
        getVerDoc.RootElement.GetProperty("result").GetProperty("version").GetString().Should().Be("1.36.0");

        // 2. GET /aria2/rpc?method=aria2.getSessionInfo&id=abc
        var getSessionResp = await this.Client.GetAsync("/aria2/rpc?method=aria2.getSessionInfo&id=abc");
        getSessionResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. GET /jsonrpc with base64-encoded params: ["0", "10"]
        var jsonParams = JsonSerializer.Serialize(new object[] { 0, 10 });
        var b64Params = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonParams));
        var b64Resp = await this.Client.GetAsync($"/jsonrpc?method=aria2.tellWaiting&id=1001&params={b64Params}");
        b64Resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Aria2_AddUriAndRemove_ViaJsonAndXmlRpc_Succeeds()
    {
        // 1. JSON-RPC aria2.addUri with token: prefix in params
        var magnetUri = "magnet:?xt=urn:btih:b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2&dn=Aria2AddUriTest";
        var addUriPayload = new
        {
            jsonrpc = "2.0",
            id = "add-uri-1",
            method = "aria2.addUri",
            @params = new object[]
            {
                "token:" + this.ApiKey,
                new string[] { magnetUri },
                new Dictionary<string, string> { { "dir", "/downloads/aria2-test" } },
            },
        };
        var addUriResp = await this.PostJsonAsync("/jsonrpc", addUriPayload);
        addUriResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addUriResp.Content.ReadAsStringAsync());
        var addedGid = addDoc.RootElement.GetProperty("result").GetString();
        addedGid.Should().NotBeNullOrWhiteSpace();

        // 2. JSON-RPC aria2.remove
        var removePayload = new
        {
            jsonrpc = "2.0",
            id = "rem-1",
            method = "aria2.remove",
            @params = new object[]
            {
                "token:" + this.ApiKey,
                addedGid,
            },
        };
        var removeResp = await this.PostJsonAsync("/jsonrpc", removePayload);
        removeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. XML-RPC aria2.addUri
        var magnetUri2 = "magnet:?xt=urn:btih:b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3b3&amp;dn=Aria2XmlAddUriTest";
        var addUriXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.addUri</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><array><data><value><string>{magnetUri2}</string></value></data></array></value></param></params></methodCall>";
        var xmlAddResp = await this.Client.PostAsync("/rpc", new StringContent(addUriXml, Encoding.UTF8, "text/xml"));
        xmlAddResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var xmlAddResult = await xmlAddResp.Content.ReadAsStringAsync();
        xmlAddResult.Should().Contain("b3b3b3b3b3b3b3b3");

        // 4. XML-RPC aria2.remove
        var xmlRem = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.remove</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><string>b3b3b3b3b3b3b3b3</string></value></param></params></methodCall>";
        var xmlRemResp = await this.Client.PostAsync("/rpc", new StringContent(xmlRem, Encoding.UTF8, "text/xml"));
        xmlRemResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Aria2_AddTorrentBase64_ViaJsonAndXmlRpc_Succeeds()
    {
        var pieces = new byte[20];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)(i + 1);
        }

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString("aria2_sample.mkv") },
            { "piece length", new BEncodedNumber(16384) },
            { "pieces", new BEncodedString(pieces) },
            { "length", new BEncodedNumber(16384) },
        };

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.aria2test.com/announce") },
            { "info", infoDict },
        };

        var torrentB64 = Convert.ToBase64String(rootDict.Encode());

        // 1. JSON-RPC aria2.addTorrent
        var addTorrentPayload = new
        {
            jsonrpc = "2.0",
            id = "add-torrent-1",
            method = "aria2.addTorrent",
            @params = new object[]
            {
                "token:" + this.ApiKey,
                torrentB64,
                System.Array.Empty<string>(),
                new Dictionary<string, string> { { "dir", "/downloads/aria2-torrent" } },
            },
        };
        var addResp = await this.PostJsonAsync("/jsonrpc", addTorrentPayload);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var addedGid = addDoc.RootElement.GetProperty("result").GetString();
        addedGid.Should().NotBeNullOrWhiteSpace();

        // 2. Remove the added torrent
        var removePayload = new
        {
            jsonrpc = "2.0",
            id = "rem-torrent-1",
            method = "aria2.remove",
            @params = new object[] { "token:" + this.ApiKey, addedGid },
        };
        var remResp = await this.PostJsonAsync("/jsonrpc", removePayload);
        remResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. XML-RPC aria2.addTorrent
        var addXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.addTorrent</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><string>{torrentB64}</string></value></param></params></methodCall>";
        var xmlResp = await this.Client.PostAsync("/rpc", new StringContent(addXml, Encoding.UTF8, "text/xml"));
        xmlResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var xmlResult = await xmlResp.Content.ReadAsStringAsync();
        xmlResult.Should().Contain("<methodResponse>");
    }

    [Test]
    public async Task Aria2_XmlRpc_ExtendedMethods_ExecuteSuccessfully()
    {
        // 1. XML-RPC aria2.getGlobalStat
        var statXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.getGlobalStat</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param></params></methodCall>";
        var statResp = await this.Client.PostAsync("/rpc", new StringContent(statXml, Encoding.UTF8, "text/xml"));
        statResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statBody = await statResp.Content.ReadAsStringAsync();
        statBody.Should().Contain("downloadSpeed");

        // 2. XML-RPC aria2.getGlobalOption
        var optXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.getGlobalOption</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param></params></methodCall>";
        var optResp = await this.Client.PostAsync("/rpc", new StringContent(optXml, Encoding.UTF8, "text/xml"));
        optResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. XML-RPC aria2.tellActive
        var activeXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.tellActive</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param></params></methodCall>";
        var activeResp = await this.Client.PostAsync("/rpc", new StringContent(activeXml, Encoding.UTF8, "text/xml"));
        activeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. XML-RPC aria2.tellWaiting
        var waitXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.tellWaiting</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><i4>0</i4></param><param><value><i4>10</i4></param></params></methodCall>";
        var waitResp = await this.Client.PostAsync("/rpc", new StringContent(waitXml, Encoding.UTF8, "text/xml"));
        waitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. XML-RPC aria2.tellStopped
        var stopXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.tellStopped</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><i4>0</i4></param><param><value><i4>10</i4></param></params></methodCall>";
        var stopResp = await this.Client.PostAsync("/rpc", new StringContent(stopXml, Encoding.UTF8, "text/xml"));
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. XML-RPC aria2.getServers
        var srvXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.getServers</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><string>0000000000000000</string></value></param></params></methodCall>";
        var srvResp = await this.Client.PostAsync("/rpc", new StringContent(srvXml, Encoding.UTF8, "text/xml"));
        srvResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. XML-RPC aria2.purgeDownloadResult
        var purgeXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.purgeDownloadResult</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param></params></methodCall>";
        var purgeResp = await this.Client.PostAsync("/rpc", new StringContent(purgeXml, Encoding.UTF8, "text/xml"));
        purgeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. XML-RPC aria2.tellStatus and aria2.getOption and aria2.getFiles
        var statusXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.tellStatus</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><string>a2a2a2a2a2a2a2a2</string></value></param></params></methodCall>";
        var statusResp = await this.Client.PostAsync("/rpc", new StringContent(statusXml, Encoding.UTF8, "text/xml"));
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getOptXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.getOption</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><string>a2a2a2a2a2a2a2a2</string></value></param></params></methodCall>";
        var getOptResp = await this.Client.PostAsync("/rpc", new StringContent(getOptXml, Encoding.UTF8, "text/xml"));
        getOptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getFilesXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.getFiles</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><string>a2a2a2a2a2a2a2a2</string></value></param></params></methodCall>";
        var getFilesResp = await this.Client.PostAsync("/rpc", new StringContent(getFilesXml, Encoding.UTF8, "text/xml"));
        getFilesResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getUrisXml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.getUris</methodName><params><param><value><string>token:{this.ApiKey}</string></value></param><param><value><string>a2a2a2a2a2a2a2a2</string></value></param></params></methodCall>";
        var getUrisResp = await this.Client.PostAsync("/rpc", new StringContent(getUrisXml, Encoding.UTF8, "text/xml"));
        getUrisResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

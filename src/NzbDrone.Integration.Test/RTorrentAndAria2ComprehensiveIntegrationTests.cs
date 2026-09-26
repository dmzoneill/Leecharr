// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class RTorrentAndAria2ComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string RTHash = "c333333333333333333333333333333333333333";
    private const string RTName = "RTorrentAria2Movie";

    [Test]
    public async Task RTorrent_ComprehensiveXmlRpcOperations_Succeed()
    {
        // 1. Add torrent via Leecharr REST API
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{RTHash}&dn={RTName}",
            category = "movies",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 2. Call get_down_rate and get_up_rate
            var downRateXml = "<?xml version=\"1.0\"?><methodCall><methodName>get_down_rate</methodName></methodCall>";
            var downRateResp = await this.Client.PostAsync("/RPC2", new StringContent(downRateXml, Encoding.UTF8, "text/xml"));
            downRateResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var upRateXml = "<?xml version=\"1.0\"?><methodCall><methodName>get_up_rate</methodName></methodCall>";
            var upRateResp = await this.Client.PostAsync("/RPC2", new StringContent(upRateXml, Encoding.UTF8, "text/xml"));
            upRateResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Call d.multicall2 with main view
            var multicall2Xml = $@"<?xml version=""1.0""?>
<methodCall>
  <methodName>d.multicall2</methodName>
  <params>
    <param><value><string></string></value></param>
    <param><value><string>main</string></value></param>
    <param><value><string>d.hash=</string></value></param>
    <param><value><string>d.name=</string></value></param>
    <param><value><string>d.directory=</string></value></param>
  </params>
</methodCall>";
            var multiResp = await this.Client.PostAsync("/RPC2", new StringContent(multicall2Xml, Encoding.UTF8, "text/xml"));
            multiResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var multiXml = await multiResp.Content.ReadAsStringAsync();
            multiXml.Should().Contain(RTHash.ToUpperInvariant());
            multiXml.Should().Contain(RTName);

            // 4. Call d.start, d.stop, d.pause, d.resume
            var startXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.start</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var startResp = await this.Client.PostAsync("/RPC2", new StringContent(startXml, Encoding.UTF8, "text/xml"));
            startResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var pauseXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.pause</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var pauseResp = await this.Client.PostAsync("/RPC2", new StringContent(pauseXml, Encoding.UTF8, "text/xml"));
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var resumeXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.resume</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var resumeResp = await this.Client.PostAsync("/RPC2", new StringContent(resumeXml, Encoding.UTF8, "text/xml"));
            resumeResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var stopXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.stop</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var stopResp = await this.Client.PostAsync("/RPC2", new StringContent(stopXml, Encoding.UTF8, "text/xml"));
            stopResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }

    [Test]
    public async Task Aria2_ComprehensiveJsonRpcOperations_Succeed()
    {
        // 1. aria2.addUri
        var addUriReq = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "aria2.addUri",
            @params = new object[]
            {
                new[] { $"magnet:?xt=urn:btih:{RTHash}&dn={RTName}" },
                new { dir = "/downloads", pause = "true" },
            },
        };
        var addUriResp = await this.PostJsonAsync("/jsonrpc", addUriReq);
        addUriResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addUriResp.Content.ReadAsStringAsync());
        var gid = addDoc.RootElement.GetProperty("result").GetString();
        gid.Should().NotBeNullOrWhiteSpace();

        try
        {
            // 2. aria2.tellWaiting & aria2.tellStopped
            var tellWaitingReq = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "aria2.tellWaiting",
                @params = new object[] { 0, 10 },
            };
            var tellWaitingResp = await this.PostJsonAsync("/jsonrpc", tellWaitingReq);
            tellWaitingResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var tellStoppedReq = new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "aria2.tellStopped",
                @params = new object[] { 0, 10 },
            };
            var tellStoppedResp = await this.PostJsonAsync("/jsonrpc", tellStoppedReq);
            tellStoppedResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. aria2.getOption & aria2.changeOption
            var getOptReq = new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "aria2.getOption",
                @params = new object[] { gid! },
            };
            var getOptResp = await this.PostJsonAsync("/jsonrpc", getOptReq);
            getOptResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var changeOptReq = new
            {
                jsonrpc = "2.0",
                id = 5,
                method = "aria2.changeOption",
                @params = new object[] { gid!, new { dir = "/downloads/updated" } },
            };
            var changeOptResp = await this.PostJsonAsync("/jsonrpc", changeOptReq);
            changeOptResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. aria2.getGlobalOption & aria2.changeGlobalOption
            var getGlobalOptReq = new
            {
                jsonrpc = "2.0",
                id = 6,
                method = "aria2.getGlobalOption",
                @params = new object[0],
            };
            var getGlobalOptResp = await this.PostJsonAsync("/jsonrpc", getGlobalOptReq);
            getGlobalOptResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var changeGlobalOptReq = new
            {
                jsonrpc = "2.0",
                id = 7,
                method = "aria2.changeGlobalOption",
                @params = new object[] { new { dir = "/downloads" } },
            };
            var changeGlobalOptResp = await this.PostJsonAsync("/jsonrpc", changeGlobalOptReq);
            changeGlobalOptResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. aria2.getGlobalStat
            var globalStatReq = new
            {
                jsonrpc = "2.0",
                id = 8,
                method = "aria2.getGlobalStat",
                @params = new object[0],
            };
            var globalStatResp = await this.PostJsonAsync("/jsonrpc", globalStatReq);
            globalStatResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. system.multicall
            var multicallReq = new
            {
                jsonrpc = "2.0",
                id = 9,
                method = "system.multicall",
                @params = new object[]
                {
                    new object[]
                    {
                        new { methodName = "aria2.getVersion", @params = new object[0] },
                        new { methodName = "aria2.getGlobalStat", @params = new object[0] },
                    },
                },
            };
            var multicallResp = await this.PostJsonAsync("/jsonrpc", multicallReq);
            multicallResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var multiDoc = JsonDocument.Parse(await multicallResp.Content.ReadAsStringAsync());
            multiDoc.RootElement.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Array);
            multiDoc.RootElement.GetProperty("result").GetArrayLength().Should().Be(2);

            // 7. aria2.pause and unpause
            var pauseReq = new
            {
                jsonrpc = "2.0",
                id = 10,
                method = "aria2.pause",
                @params = new object[] { gid! },
            };
            var pauseResp = await this.PostJsonAsync("/jsonrpc", pauseReq);
            pauseResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var unpauseReq = new
            {
                jsonrpc = "2.0",
                id = 11,
                method = "aria2.unpause",
                @params = new object[] { gid! },
            };
            var unpauseResp = await this.PostJsonAsync("/jsonrpc", unpauseReq);
            unpauseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // 8. aria2.remove
            var removeReq = new
            {
                jsonrpc = "2.0",
                id = 12,
                method = "aria2.remove",
                @params = new object[] { gid! },
            };
            var removeResp = await this.PostJsonAsync("/jsonrpc", removeReq);
            removeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}

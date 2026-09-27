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
public class RTorrentMulticallMatrixIntegrationTests : IntegrationTestBase
{
    private const string RTHash = "b888888888888888888888888888888888888888";
    private const string RTName = "RTorrentMulticallMovie";

    [Test]
    public async Task RTorrent_SystemMulticall_BatchesSubcallsAccurately()
    {
        var multicallXml = string.Join("\n", new[]
        {
            "<?xml version=\"1.0\"?>",
            "<methodCall>",
            "  <methodName>system.multicall</methodName>",
            "  <params>",
            "    <param>",
            "      <value>",
            "        <array>",
            "          <data>",
            "            <value>",
            "              <struct>",
            "                <member>",
            "                  <name>methodName</name>",
            "                  <value><string>system.api_version</string></value>",
            "                </member>",
            "                <member>",
            "                  <name>params</name>",
            "                  <value><array><data></data></array></value>",
            "                </member>",
            "              </struct>",
            "            </value>",
            "            <value>",
            "              <struct>",
            "                <member>",
            "                  <name>methodName</name>",
            "                  <value><string>system.client_version</string></value>",
            "                </member>",
            "                <member>",
            "                  <name>params</name>",
            "                  <value><array><data></data></array></value>",
            "                </member>",
            "              </struct>",
            "            </value>",
            "            <value>",
            "              <struct>",
            "                <member>",
            "                  <name>methodName</name>",
            "                  <value><string>get_directory</string></value>",
            "                </member>",
            "                <member>",
            "                  <name>params</name>",
            "                  <value><array><data></data></array></value>",
            "                </member>",
            "              </struct>",
            "            </value>",
            "          </data>",
            "        </array>",
            "      </value>",
            "    </param>",
            "  </params>",
            "</methodCall>",
        });

        var response = await this.Client.PostAsync("/RPC2", new StringContent(multicallXml, Encoding.UTF8, "text/xml"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var xml = await response.Content.ReadAsStringAsync();
        xml.Should().Contain("methodResponse");
        xml.Should().Contain("value");
    }

    [Test]
    public async Task RTorrent_FileTrackerAndPeerMulticalls_QueryFieldsAccurately()
    {
        // 1. Add a torrent to query files, trackers, and peers
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{RTHash}&dn={RTName}",
            category = "rt-multi-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 2. f.multicall with all 7 file fields
            var fMultiXml = string.Join("\n", new[]
            {
                "<?xml version=\"1.0\"?>",
                "<methodCall>",
                "  <methodName>f.multicall</methodName>",
                "  <params>",
                $"    <param><value><string>{RTHash}</string></value></param>",
                "    <param><value><string></string></value></param>",
                "    <param><value><string>f.get_path=</string></value></param>",
                "    <param><value><string>f.get_size_bytes=</string></value></param>",
                "    <param><value><string>f.get_completed_chunks=</string></value></param>",
                "    <param><value><string>f.get_size_chunks=</string></value></param>",
                "    <param><value><string>f.get_range_first=</string></value></param>",
                "    <param><value><string>f.get_range_second=</string></value></param>",
                "    <param><value><string>f.get_priority=</string></value></param>",
                "  </params>",
                "</methodCall>",
            });

            var fResp = await this.Client.PostAsync("/RPC2", new StringContent(fMultiXml, Encoding.UTF8, "text/xml"));
            fResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. t.multicall with all 8 tracker fields
            var tMultiXml = string.Join("\n", new[]
            {
                "<?xml version=\"1.0\"?>",
                "<methodCall>",
                "  <methodName>t.multicall</methodName>",
                "  <params>",
                $"    <param><value><string>{RTHash}</string></value></param>",
                "    <param><value><string></string></value></param>",
                "    <param><value><string>t.get_url=</string></value></param>",
                "    <param><value><string>t.get_type=</string></value></param>",
                "    <param><value><string>t.is_enabled=</string></value></param>",
                "    <param><value><string>t.is_open=</string></value></param>",
                "    <param><value><string>t.get_group=</string></value></param>",
                "    <param><value><string>t.get_scrape_complete=</string></value></param>",
                "    <param><value><string>t.get_scrape_incomplete=</string></value></param>",
                "    <param><value><string>t.get_scrape_downloaded=</string></value></param>",
                "  </params>",
                "</methodCall>",
            });

            var tResp = await this.Client.PostAsync("/RPC2", new StringContent(tMultiXml, Encoding.UTF8, "text/xml"));
            tResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. p.multicall with all 7 peer fields
            var pMultiXml = string.Join("\n", new[]
            {
                "<?xml version=\"1.0\"?>",
                "<methodCall>",
                "  <methodName>p.multicall</methodName>",
                "  <params>",
                $"    <param><value><string>{RTHash}</string></value></param>",
                "    <param><value><string></string></value></param>",
                "    <param><value><string>p.id=</string></value></param>",
                "    <param><value><string>p.address=</string></value></param>",
                "    <param><value><string>p.port=</string></value></param>",
                "    <param><value><string>p.client_version=</string></value></param>",
                "    <param><value><string>p.completed_percent=</string></value></param>",
                "    <param><value><string>p.down_rate=</string></value></param>",
                "    <param><value><string>p.up_rate=</string></value></param>",
                "  </params>",
                "</methodCall>",
            });

            var pResp = await this.Client.PostAsync("/RPC2", new StringContent(pMultiXml, Encoding.UTF8, "text/xml"));
            pResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }
}

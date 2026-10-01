// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class RTorrentAdvancedXmlRpcMatrixIntegrationTests : IntegrationTestBase
{
    private const string RTHash = "b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1";
    private const string RTName = "RTorrentAdvancedMovie";

    private async Task<string> PostXmlRpcAsync(string xml)
    {
        var response = await this.Client.PostAsync("/RPC2", new StringContent(xml, Encoding.UTF8, "text/xml"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [Test]
    public async Task RTorrent_LoadAndCustomFieldsAndSystemMethods_ExecuteSuccessfully()
    {
        // 1. system.listmethods & system.client_version & system.getcapabilities
        var listMethodsXml = "<?xml version=\"1.0\"?><methodCall><methodName>system.listmethods</methodName></methodCall>";
        var listResp = await this.PostXmlRpcAsync(listMethodsXml);
        listResp.Should().Contain("d.multicall2");
        listResp.Should().Contain("load.start");

        var verXml = "<?xml version=\"1.0\"?><methodCall><methodName>system.client_version</methodName></methodCall>";
        var verResp = await this.PostXmlRpcAsync(verXml);
        verResp.Should().Contain("0.9.8");

        var capXml = "<?xml version=\"1.0\"?><methodCall><methodName>system.getcapabilities</methodName></methodCall>";
        var capResp = await this.PostXmlRpcAsync(capXml);
        capResp.Should().Contain("system.multicall");

        // 2. load.start with magnet and arguments
        var magnetUri = $"magnet:?xt=urn:btih:{RTHash}&amp;dn={RTName}";
        var loadXml = $"<?xml version=\"1.0\"?><methodCall><methodName>load.start</methodName><params><param><value><string></string></value></param><param><value><string>{magnetUri}</string></value></param><param><value><string>d.set_custom1=advcategory</string></value></param><param><value><string>d.priority.set=2</string></value></param></params></methodCall>";
        var loadResp = await this.PostXmlRpcAsync(loadXml);
        loadResp.Should().Contain("<i4>0</i4>");

        try
        {
            // 3. Custom fields 1 through 5: set and get
            var setC1Xml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.custom1.set</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string>CustomVal1</string></value></param></params></methodCall>";
            var c1Resp = await this.PostXmlRpcAsync(setC1Xml);
            c1Resp.Should().Contain("CustomVal1");

            var setC2Xml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.custom2.set</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string>CustomVal2</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(setC2Xml);

            var setC3Xml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.custom3.set</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string>CustomVal3</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(setC3Xml);

            var setC4Xml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.custom4.set</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string>CustomVal4</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(setC4Xml);

            var setC5Xml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.custom5.set</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string>CustomVal5</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(setC5Xml);

            var getC2Xml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_custom2</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var getC2Resp = await this.PostXmlRpcAsync(getC2Xml);
            getC2Resp.Should().Contain("CustomVal2");

            var getC5Xml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_custom5</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var getC5Resp = await this.PostXmlRpcAsync(getC5Xml);
            getC5Resp.Should().Contain("CustomVal5");

            // 4. d.views.has testing
            var viewMainXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.views.has</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string>main</string></value></param></params></methodCall>";
            var viewMainResp = await this.PostXmlRpcAsync(viewMainXml);
            viewMainResp.Should().Contain("<i4>1</i4>");

            var viewStoppedXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.views.has</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string>stopped</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(viewStoppedXml);

            var viewHashingXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.views.has</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><string>hashing</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(viewHashingXml);

            // 5. Rate limits on torrent
            var setDlRateXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.down.rate.set_kb</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><i4>2048</i4></param></params></methodCall>";
            await this.PostXmlRpcAsync(setDlRateXml);

            var setUlRateXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.up.rate.set_kb</methodName><params><param><value><string>{RTHash}</string></value></param><param><value><i4>1024</i4></param></params></methodCall>";
            await this.PostXmlRpcAsync(setUlRateXml);

            // 6. Direct d.* field getters
            var getHashXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_hash</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var hashResp = await this.PostXmlRpcAsync(getHashXml);
            hashResp.Should().Contain(RTHash.ToUpperInvariant());

            var getNameXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_name</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var nameResp = await this.PostXmlRpcAsync(getNameXml);
            nameResp.Should().Contain(RTName);

            var getBasePathXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_base_path</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(getBasePathXml);

            var getBytesDoneXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_bytes_done</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(getBytesDoneXml);

            var getSizeBytesXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_size_bytes</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(getSizeBytesXml);

            var getRatioXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_ratio</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(getRatioXml);

            var getChunkSizeXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_chunk_size</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(getChunkSizeXml);

            var getConnCurrentXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.get_connection_current</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            var connResp = await this.PostXmlRpcAsync(getConnCurrentXml);
            connResp.Should().Contain("leech");

            // 7. Tracker announce and check hash
            var annXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.tracker_announce</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(annXml);

            var checkXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.check_hash</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(checkXml);

            // 8. system.multicall with nested operations
            var sysMultiXml = "<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data><value><struct><member><name>methodName</name><value><string>system.client_version</string></value></member><member><name>params</name><value><array><data></data></array></value></member></struct></value><value><struct><member><name>methodName</name><value><string>get_directory</string></value></member><member><name>params</name><value><array><data></data></array></value></member></struct></value></data></array></value></param></params></methodCall>";
            var sysMultiResp = await this.PostXmlRpcAsync(sysMultiXml);
            sysMultiResp.Should().Contain("0.9.8");
        }
        finally
        {
            // 8. d.delete_tied to cleanly remove torrent
            var delXml = $"<?xml version=\"1.0\"?><methodCall><methodName>d.delete_tied</methodName><params><param><value><string>{RTHash}</string></value></param></params></methodCall>";
            await this.PostXmlRpcAsync(delXml);
        }
    }
}

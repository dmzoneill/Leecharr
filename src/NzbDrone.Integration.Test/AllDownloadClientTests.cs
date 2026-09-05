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
public class AllDownloadClientTests : IntegrationTestBase
{
    [Test]
    public async Task RTorrent_XmlRpc_SystemClientVersion_ReturnsVersion()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>system.client_version</methodName></methodCall>";
        var content = new StringContent(xml, Encoding.UTF8, "text/xml");
        var response = await this.Client.PostAsync("/RPC2", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var resXml = await response.Content.ReadAsStringAsync();
        resXml.Should().Contain("0.9.8");
    }

    [Test]
    public async Task RTorrent_XmlRpc_ListMethods_ReturnsMethods()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>system.listMethods</methodName></methodCall>";
        var content = new StringContent(xml, Encoding.UTF8, "text/xml");
        var response = await this.Client.PostAsync("/RPC2", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var resXml = await response.Content.ReadAsStringAsync();
        resXml.Should().Contain("d.multicall2");
    }

    [Test]
    public async Task Aria2_JsonRpc_GetVersion_ReturnsVersion()
    {
        var rpcBody = new
        {
            jsonrpc = "2.0",
            method = "aria2.getVersion",
            id = 1,
        };

        var response = await this.PostJsonAsync("/jsonrpc", rpcBody);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("result").GetProperty("version").GetString().Should().Be("1.36.0");
    }

    [Test]
    public async Task Flood_Authenticate_And_GetTorrents_ReturnsSuccess()
    {
        var authResponse = await this.PostJsonAsync("/api/auth/authenticate", new { });
        authResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var torrentsResponse = await this.Client.GetAsync("/api/torrents");
        torrentsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await torrentsResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("torrents", out _).Should().BeTrue();
    }

    [Test]
    public async Task UTorrent_GetToken_And_List_ReturnsSuccess()
    {
        var tokenResponse = await this.Client.GetAsync("/gui/token.html");
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenHtml = await tokenResponse.Content.ReadAsStringAsync();
        tokenHtml.Should().Contain("LEECHARR_UTORRENT_AUTH_TOKEN");

        var listResponse = await this.Client.GetAsync("/gui/?list=1");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(listJson);
        doc.RootElement.TryGetProperty("torrents", out _).Should().BeTrue();
    }

    [Test]
    public async Task Hadouken_GetVersion_ReturnsVersion()
    {
        var rpcBody = new
        {
            method = "core.getVersion",
            id = 1,
        };

        var response = await this.PostJsonAsync("/api/hadouken", rpcBody);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("result").GetString().Should().Be("5.3.0");
    }

    [Test]
    public async Task Synology_Auth_And_Task_ReturnsSuccess()
    {
        var authResponse = await this.Client.GetAsync("/webapi/auth.cgi?api=SYNO.API.Auth&method=login");
        authResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var taskResponse = await this.Client.GetAsync("/webapi/DownloadStation/task.cgi?api=SYNO.DownloadStation.Task&method=list");
        taskResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await taskResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task Freebox_Login_And_Downloads_ReturnsSuccess()
    {
        var loginResponse = await this.Client.GetAsync("/api/v4/login/session");
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var dlResponse = await this.Client.GetAsync("/api/v4/downloads/");
        dlResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await dlResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task Sabnzbd_Version_And_Queue_ReturnsSuccess()
    {
        var versionResponse = await this.Client.GetAsync("/api?mode=version");
        versionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var vJson = await versionResponse.Content.ReadAsStringAsync();
        using var vDoc = JsonDocument.Parse(vJson);
        vDoc.RootElement.GetProperty("version").GetString().Should().Be("4.3.2");

        var queueResponse = await this.Client.GetAsync("/api?mode=queue");
        queueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var qJson = await queueResponse.Content.ReadAsStringAsync();
        using var qDoc = JsonDocument.Parse(qJson);
        qDoc.RootElement.TryGetProperty("queue", out _).Should().BeTrue();
    }

    [Test]
    public async Task Nzbget_Version_And_Status_ReturnsSuccess()
    {
        var versionBody = new { method = "version", id = 1 };
        var versionResponse = await this.PostJsonAsync("/nzbget/jsonrpc", versionBody);
        versionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var vJson = await versionResponse.Content.ReadAsStringAsync();
        using var vDoc = JsonDocument.Parse(vJson);
        vDoc.RootElement.GetProperty("result").GetString().Should().Be("24.0");

        var statusBody = new { method = "status", id = 2 };
        var statusResponse = await this.PostJsonAsync("/nzbget/jsonrpc", statusBody);
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task NzbVortex_Nonce_And_Queue_ReturnsSuccess()
    {
        var nonceResponse = await this.Client.GetAsync("/nzbvortex/api/v1/auth/nonce");
        nonceResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await this.Client.GetAsync("/nzbvortex/api/v1/auth/login");
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var queueResponse = await this.Client.GetAsync("/nzbvortex/api/v1/queue");
        queueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var qJson = await queueResponse.Content.ReadAsStringAsync();
        using var qDoc = JsonDocument.Parse(qJson);
        qDoc.RootElement.GetProperty("error").GetInt32().Should().Be(0);
    }

    [Test]
    public async Task NzbVortex_RootPath_Nonce_Login_And_Queue_ReturnsSuccess()
    {
        var nonceResponse = await this.Client.GetAsync("/api/v1/auth/nonce");
        nonceResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getLoginResponse = await this.Client.GetAsync("/api/v1/auth/login");
        getLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getLoginJson = await getLoginResponse.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getLoginJson);
        getDoc.RootElement.GetProperty("loginResult").GetInt32().Should().Be(0);
        getDoc.RootElement.GetProperty("auth").GetBoolean().Should().BeTrue();

        var postLoginResponse = await this.Client.PostAsync("/api/v1/auth/login", new StringContent(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded"));
        postLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var postLoginJson = await postLoginResponse.Content.ReadAsStringAsync();
        using var postDoc = JsonDocument.Parse(postLoginJson);
        postDoc.RootElement.GetProperty("loginResult").GetInt32().Should().Be(0);
        postDoc.RootElement.GetProperty("auth").GetBoolean().Should().BeTrue();

        var queueResponse = await this.Client.GetAsync("/api/v1/queue");
        queueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var qJson = await queueResponse.Content.ReadAsStringAsync();
        using var qDoc = JsonDocument.Parse(qJson);
        qDoc.RootElement.GetProperty("error").GetInt32().Should().Be(0);
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Leecharr.Api.V1.Aria2;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class Aria2RpcControllerTest
{
    private const string FullInfoHash = "0123456789abcdef0123456789abcdef01234567";
    private const string ExpectedGid = "0123456789abcdef";

    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private Aria2RpcController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new Aria2RpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            this.safeHttpClientService);
    }

    [Test]
    public async Task AddTorrent_JsonRpc_Returns16CharGidMatchingInfoHash()
    {
        var torrentBytes = new byte[] { 0x64, 0x31, 0x3a, 0x61, 0x64, 0x65 };
        var b64Torrent = Convert.ToBase64String(torrentBytes);
        var parsed = new ParsedTorrent { Name = "TestTorrent", InfoHash = FullInfoHash };
        var addedTorrent = new Torrent { Id = 1, Name = "TestTorrent", InfoHash = FullInfoHash };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, Arg.Any<byte[]>())
            .Returns(Task.FromResult(addedTorrent));

        this.SetJsonRequestBody($$"""
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "aria2.addTorrent",
              "params": ["{{b64Torrent}}"]
            }
            """);

        var actionResult = await this.controller.HandleRpc();
        var gid = GetJsonRpcResultString(actionResult);

        gid.Should().NotBeNull();
        gid.Length.Should().Be(16);
        gid.Should().Be(ExpectedGid);
    }

    [Test]
    public async Task AddUri_JsonRpc_Magnet_Returns16CharGidMatchingInfoHash()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{FullInfoHash}&dn=TestTorrent";
        var addedTorrent = new Torrent { Id = 2, Name = "TestTorrent", InfoHash = FullInfoHash };

        this.torrentService.AddFromMagnetAsync(magnetUri, null, null, false)
            .Returns(Task.FromResult(addedTorrent));

        this.SetJsonRequestBody($$"""
            {
              "jsonrpc": "2.0",
              "id": 2,
              "method": "aria2.addUri",
              "params": [["{{magnetUri}}"]]
            }
            """);

        var actionResult = await this.controller.HandleRpc();
        var gid = GetJsonRpcResultString(actionResult);

        gid.Should().NotBeNull();
        gid.Length.Should().Be(16);
        gid.Should().Be(ExpectedGid);
    }

    [Test]
    public async Task AddUri_JsonRpc_HttpUrl_Returns16CharGidMatchingInfoHash()
    {
        var httpUri = "http://example.com/test.torrent";
        var torrentBytes = new byte[] { 0x64, 0x31, 0x3a, 0x61, 0x64, 0x65 };
        var parsed = new ParsedTorrent { Name = "HttpTorrent", InfoHash = FullInfoHash };
        var addedTorrent = new Torrent { Id = 3, Name = "HttpTorrent", InfoHash = FullInfoHash };

        this.safeHttpClientService.DownloadBytesAsync(httpUri, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(torrentBytes));
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, Arg.Any<byte[]>())
            .Returns(Task.FromResult(addedTorrent));

        this.SetJsonRequestBody($$"""
            {
              "jsonrpc": "2.0",
              "id": 3,
              "method": "aria2.addUri",
              "params": [["{{httpUri}}"]]
            }
            """);

        var actionResult = await this.controller.HandleRpc();
        var gid = GetJsonRpcResultString(actionResult);

        gid.Should().NotBeNull();
        gid.Length.Should().Be(16);
        gid.Should().Be(ExpectedGid);
    }

    [Test]
    public async Task AddTorrent_XmlRpc_Returns16CharGidMatchingInfoHash()
    {
        var torrentBytes = new byte[] { 0x64, 0x31, 0x3a, 0x61, 0x64, 0x65 };
        var b64Torrent = Convert.ToBase64String(torrentBytes);
        var parsed = new ParsedTorrent { Name = "XmlTorrent", InfoHash = FullInfoHash };
        var addedTorrent = new Torrent { Id = 4, Name = "XmlTorrent", InfoHash = FullInfoHash };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, Arg.Any<byte[]>())
            .Returns(Task.FromResult(addedTorrent));

        this.SetXmlRpcRequest("aria2.addTorrent", b64Torrent);

        var actionResult = await this.controller.HandleRpc();
        var gid = GetXmlRpcResultString(actionResult);

        gid.Should().NotBeNull();
        gid.Length.Should().Be(16);
        gid.Should().Be(ExpectedGid);
    }

    [Test]
    public async Task AddUri_XmlRpc_Magnet_Returns16CharGidMatchingInfoHash()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{FullInfoHash}&dn=XmlTorrent";
        var addedTorrent = new Torrent { Id = 5, Name = "XmlTorrent", InfoHash = FullInfoHash };

        this.torrentService.AddFromMagnetAsync(magnetUri, null, null, false)
            .Returns(Task.FromResult(addedTorrent));

        this.SetXmlRpcRequest("aria2.addUri", magnetUri);

        var actionResult = await this.controller.HandleRpc();
        var gid = GetXmlRpcResultString(actionResult);

        gid.Should().NotBeNull();
        gid.Length.Should().Be(16);
        gid.Should().Be(ExpectedGid);
    }

    [Test]
    public async Task AddUri_XmlRpc_HttpUrl_Returns16CharGidMatchingInfoHash()
    {
        var httpUri = "http://example.com/test.torrent";
        var torrentBytes = new byte[] { 0x64, 0x31, 0x3a, 0x61, 0x64, 0x65 };
        var parsed = new ParsedTorrent { Name = "XmlHttpTorrent", InfoHash = FullInfoHash };
        var addedTorrent = new Torrent { Id = 6, Name = "XmlHttpTorrent", InfoHash = FullInfoHash };

        this.safeHttpClientService.DownloadBytesAsync(httpUri, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(torrentBytes));
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, Arg.Any<byte[]>())
            .Returns(Task.FromResult(addedTorrent));

        this.SetXmlRpcRequest("aria2.addUri", httpUri);

        var actionResult = await this.controller.HandleRpc();
        var gid = GetXmlRpcResultString(actionResult);

        gid.Should().NotBeNull();
        gid.Length.Should().Be(16);
        gid.Should().Be(ExpectedGid);
    }

    [Test]
    public async Task ReturnedGid_MatchesGidFromTellActiveAndTellStatus()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{FullInfoHash}&dn=ActiveTorrent";
        var torrent = new Torrent
        {
            Id = 7,
            Name = "ActiveTorrent",
            InfoHash = FullInfoHash,
            Status = TorrentStatus.Downloading,
            TotalSize = 1000,
            Downloaded = 500,
            Uploaded = 200,
            DownloadSpeed = 100,
            UploadSpeed = 50,
            SavePath = "/downloads",
        };

        this.torrentService.AddFromMagnetAsync(magnetUri, null, null, false)
            .Returns(Task.FromResult(torrent));
        this.torrentService.GetAll().Returns(new List<Torrent> { torrent });

        // 1. Add download via JSON-RPC addUri
        this.SetJsonRequestBody($$"""
            {
              "jsonrpc": "2.0",
              "id": 10,
              "method": "aria2.addUri",
              "params": [["{{magnetUri}}"]]
            }
            """);

        var addResult = await this.controller.HandleRpc();
        var returnedGid = GetJsonRpcResultString(addResult);
        returnedGid.Should().Be(ExpectedGid);

        // 2. Query aria2.tellActive via JSON-RPC
        this.SetJsonRequestBody("""
            {
              "jsonrpc": "2.0",
              "id": 11,
              "method": "aria2.tellActive",
              "params": []
            }
            """);

        var tellActiveResult = await this.controller.HandleRpc();
        tellActiveResult.Should().BeOfType<OkObjectResult>();
        var tellActiveOk = (OkObjectResult)tellActiveResult;
        var tellActiveJson = JsonSerializer.Serialize(tellActiveOk.Value);
        using var activeDoc = JsonDocument.Parse(tellActiveJson);
        var activeArray = activeDoc.RootElement.GetProperty("result");
        activeArray.GetArrayLength().Should().Be(1);
        var activeGid = activeArray[0].GetProperty("gid").GetString();
        activeGid.Should().Be(returnedGid);

        // 3. Query aria2.tellStatus with the returned GID via JSON-RPC
        this.SetJsonRequestBody($$"""
            {
              "jsonrpc": "2.0",
              "id": 12,
              "method": "aria2.tellStatus",
              "params": ["{{returnedGid}}"]
            }
            """);

        var tellStatusResult = await this.controller.HandleRpc();
        tellStatusResult.Should().BeOfType<OkObjectResult>();
        var tellStatusOk = (OkObjectResult)tellStatusResult;
        var tellStatusJson = JsonSerializer.Serialize(tellStatusOk.Value);
        using var statusDoc = JsonDocument.Parse(tellStatusJson);
        var statusObj = statusDoc.RootElement.GetProperty("result");
        var statusGid = statusObj.GetProperty("gid").GetString();
        statusGid.Should().Be(returnedGid);
    }

    [Test]
    public async Task AddTorrent_WhenAddedIsNull_Returns16CharGid()
    {
        var torrentBytes = new byte[] { 0x64, 0x31, 0x3a, 0x61, 0x64, 0x65 };
        var b64Torrent = Convert.ToBase64String(torrentBytes);
        var parsed = new ParsedTorrent { Name = "NullTorrent", InfoHash = FullInfoHash };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, Arg.Any<byte[]>())
            .Returns(Task.FromResult<Torrent>(null!));

        this.SetJsonRequestBody($$"""
            {
              "jsonrpc": "2.0",
              "id": 20,
              "method": "aria2.addTorrent",
              "params": ["{{b64Torrent}}"]
            }
            """);

        var actionResult = await this.controller.HandleRpc();
        var gid = GetJsonRpcResultString(actionResult);

        gid.Should().NotBeNull();
        gid.Length.Should().Be(16);
    }

    [Test]
    public async Task AddUri_WhenAddedIsNull_Returns16CharGid()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{FullInfoHash}&dn=NullTorrent";

        this.torrentService.AddFromMagnetAsync(magnetUri, null, null, false)
            .Returns(Task.FromResult<Torrent>(null!));

        this.SetJsonRequestBody($$"""
            {
              "jsonrpc": "2.0",
              "id": 21,
              "method": "aria2.addUri",
              "params": [["{{magnetUri}}"]]
            }
            """);

        var actionResult = await this.controller.HandleRpc();
        var gid = GetJsonRpcResultString(actionResult);

        gid.Should().NotBeNull();
        gid.Length.Should().Be(16);
    }

    private void SetJsonRequestBody(string json)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    private void SetXmlRequestBody(string xml)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(xml);
        context.Request.Method = "POST";
        context.Request.ContentType = "text/xml";
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    private void SetXmlRpcRequest(string methodName, params string[] stringParams)
    {
        var paramsElem = new XElement("params");
        foreach (var p in stringParams)
        {
            paramsElem.Add(new XElement("param", new XElement("value", new XElement("string", p))));
        }

        var methodCall = new XElement(
            "methodCall",
            new XElement("methodName", methodName),
            paramsElem);
        var doc = new XDocument(methodCall);

        this.SetXmlRequestBody(doc.ToString());
    }

    private static string GetJsonRpcResultString(IActionResult actionResult)
    {
        actionResult.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("result").GetString();
    }

    private static string GetXmlRpcResultString(IActionResult actionResult)
    {
        actionResult.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)actionResult;
        var doc = XDocument.Parse(contentResult.Content);
        return doc.Root?.Element("params")?.Element("param")?.Element("value")?.Element("string")?.Value;
    }
}

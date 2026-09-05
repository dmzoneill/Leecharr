// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Nzbget;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class NzbgetRpcControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IDiskProvider diskProvider = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private NzbgetRpcController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new NzbgetRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            this.diskProvider,
            this.safeHttpClientService);
    }

    [Test]
    public async Task HandleRpc_Status_ReturnsFreeDiskSpaceMBFromDiskProvider()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        this.torrentService.GetAll().Returns(new List<Torrent>());
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(524288000000L);

        var request = new NzbgetRequest
        {
            Method = "status",
            Id = 1,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var resObj = doc.RootElement.GetProperty("result");
        resObj.GetProperty("FreeDiskSpaceMB").GetInt32().Should().Be((int)(524288000000L / (1024 * 1024)));
    }

    [Test]
    public async Task HandleRpc_Version_ReturnsVersion()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var request = new NzbgetRequest
        {
            Method = "version",
            Id = 2,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("result").GetString().Should().Be("24.0");
    }

    [Test]
    public async Task HandleRpc_Append_WithMagnet_CallsAddFromMagnetAsync()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Test";
        var expectedTorrent = new Torrent { Id = 42, Name = "Test" };
        this.torrentService.AddFromMagnetAsync(magnetUri, "tv", null, false).Returns(Task.FromResult(expectedTorrent));

        using var doc = JsonDocument.Parse($"[\"{magnetUri}\", \"\", \"tv\", 0, false, false]");
        var request = new NzbgetRequest
        {
            Method = "append",
            Params = doc.RootElement,
            Id = 10,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("result").GetInt32().Should().Be(42);
        await this.torrentService.Received(1).AddFromMagnetAsync(magnetUri, "tv", null, false);
    }

    [Test]
    public async Task HandleRpc_Append_WithHttpUrl_DownloadsAndAddsTorrent()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var httpUrl = "https://example.com/file.torrent";
        var bytes = new byte[] { 1, 2, 3, 4 };
        var parsedTorrent = new ParsedTorrent { InfoHash = "0123456789abcdef0123456789abcdef01234567", Name = "HttpTorrent" };
        var expectedTorrent = new Torrent { Id = 99, Name = "HttpTorrent" };

        this.safeHttpClientService.DownloadBytesAsync(httpUrl, maxSizeBytes: 10 * 1024 * 1024).Returns(Task.FromResult(bytes));
        this.torrentFileParser.Parse(bytes).Returns(parsedTorrent);
        this.torrentService.AddFromParsedTorrentAsync(parsedTorrent, "movies", null, false, bytes).Returns(Task.FromResult(expectedTorrent));

        using var doc = JsonDocument.Parse($"[\"{httpUrl}\", \"\", \"movies\", 0, false, false]");
        var request = new NzbgetRequest
        {
            Method = "append",
            Params = doc.RootElement,
            Id = 11,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("result").GetInt32().Should().Be(99);
        await this.safeHttpClientService.Received(1).DownloadBytesAsync(httpUrl, maxSizeBytes: 10 * 1024 * 1024);
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsedTorrent, "movies", null, false, bytes);
    }

    [Test]
    public async Task HandleRpc_Append_WithBase64Content_ParsesAndAddsTorrent()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var rawBytes = new byte[] { 10, 20, 30 };
        var base64 = Convert.ToBase64String(rawBytes);
        var parsedTorrent = new ParsedTorrent { InfoHash = "0123456789abcdef0123456789abcdef01234567", Name = "Base64Torrent" };
        var expectedTorrent = new Torrent { Id = 77, Name = "Base64Torrent" };

        this.torrentFileParser.Parse(Arg.Is<byte[]>(b => b.Length == 3)).Returns(parsedTorrent);
        this.torrentService.AddFromParsedTorrentAsync(parsedTorrent, "anime", null, true, Arg.Is<byte[]>(b => b.Length == 3)).Returns(Task.FromResult(expectedTorrent));

        using var doc = JsonDocument.Parse($"[\"file.nzb\", \"{base64}\", \"anime\", 0, false, true]");
        var request = new NzbgetRequest
        {
            Method = "append",
            Params = doc.RootElement,
            Id = 12,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("result").GetInt32().Should().Be(77);
    }

    [Test]
    public async Task HandleRpc_Append_WhenMagnetFails_ReturnsErrorResponse()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var magnetUri = "magnet:?invalid";
        this.torrentService.AddFromMagnetAsync(magnetUri, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .ThrowsAsync(new FormatException("Invalid magnet link"));

        using var doc = JsonDocument.Parse($"[\"{magnetUri}\", \"\", \"tv\"]");
        var request = new NzbgetRequest
        {
            Method = "append",
            Params = doc.RootElement,
            Id = 13,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.TryGetProperty("error", out var errorElem).Should().BeTrue();
        errorElem.GetProperty("message").GetString().Should().Contain("Invalid magnet link");
    }

    [Test]
    public async Task HandleRpc_Append_WhenHttpDownloadFails_ReturnsErrorResponse()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var httpUrl = "https://example.com/notfound.torrent";
        this.safeHttpClientService.DownloadBytesAsync(httpUrl, Arg.Any<long>(), Arg.Any<System.Threading.CancellationToken>())
            .ThrowsAsync(new Exception("HTTP 404 Not Found"));

        using var doc = JsonDocument.Parse($"[\"{httpUrl}\", \"\", \"tv\"]");
        var request = new NzbgetRequest
        {
            Method = "append",
            Params = doc.RootElement,
            Id = 14,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.TryGetProperty("error", out var errorElem).Should().BeTrue();
        errorElem.GetProperty("message").GetString().Should().Contain("HTTP 404 Not Found");
    }

    [Test]
    public async Task HandleXmlRpc_Version_ReturnsXmlRpcVersion()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>version</methodName></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.ContentType.Should().Be("text/xml; charset=utf-8");
        contentResult.Content.Should().Contain("<value><string>24.0</string></value>");
    }

    [Test]
    public async Task HandleXmlRpc_Status_ReturnsXmlRpcStatus()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>status</methodName></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };
        this.configService.DownloadDir.Returns("/downloads");
        this.torrentService.GetAll().Returns(new List<Torrent>());
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(524288000000L);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.ContentType.Should().Be("text/xml; charset=utf-8");
        contentResult.Content.Should().Contain("<name>FreeDiskSpaceMB</name>");
    }

    [Test]
    public async Task HandleXmlRpc_Append_WithMagnet_CallsAddFromMagnetAsync()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Test";
        var expectedTorrent = new Torrent { Id = 42, Name = "Test" };
        this.torrentService.AddFromMagnetAsync(magnetUri, "tv", null, false).Returns(Task.FromResult(expectedTorrent));

        var xml = $"<?xml version=\"1.0\"?><methodCall><methodName>append</methodName><params><param><value><string>{magnetUri}</string></value></param><param><value><string></string></value></param><param><value><string>tv</string></value></param></params></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<value><int>42</int></value>");
        await this.torrentService.Received(1).AddFromMagnetAsync(magnetUri, "tv", null, false);
    }

    [Test]
    public async Task HandleXmlRpc_Append_WithBase64_CallsAddFromParsedTorrentAsync()
    {
        var rawBytes = new byte[] { 10, 20, 30 };
        var base64 = Convert.ToBase64String(rawBytes);
        var parsedTorrent = new ParsedTorrent { InfoHash = "0123456789abcdef0123456789abcdef01234567", Name = "Base64Torrent" };
        var expectedTorrent = new Torrent { Id = 77, Name = "Base64Torrent" };

        this.torrentFileParser.Parse(Arg.Is<byte[]>(b => b.Length == 3)).Returns(parsedTorrent);
        this.torrentService.AddFromParsedTorrentAsync(parsedTorrent, "anime", null, true, Arg.Is<byte[]>(b => b.Length == 3)).Returns(Task.FromResult(expectedTorrent));

        var xml = $"<?xml version=\"1.0\"?><methodCall><methodName>append</methodName><params><param><value><string>file.nzb</string></value></param><param><value><string>{base64}</string></value></param><param><value><string>anime</string></value></param><param><value><int>0</int></value></param><param><value><boolean>0</boolean></value></param><param><value><boolean>1</boolean></value></param></params></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<value><int>77</int></value>");
    }

    [Test]
    public async Task HandleXmlRpc_EditQueue_Pause_CallsPauseAsync()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>editqueue</methodName><params><param><value><string>grouppause</string></value></param><param><value><int>0</int></value></param><param><value><string></string></value></param><param><value><array><data><value><int>101</int></value></data></array></value></param></params></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<value><boolean>1</boolean></value>");
        await this.torrentService.Received(1).PauseAsync(101);
    }
}

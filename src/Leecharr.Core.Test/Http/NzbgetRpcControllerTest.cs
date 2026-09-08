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
    private ITorrentFileService torrentFileService = null!;
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
        this.torrentFileService = Substitute.For<ITorrentFileService>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new NzbgetRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            this.diskProvider,
            this.safeHttpClientService,
            this.torrentFileService);
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

        this.safeHttpClientService.DownloadBytesAsync(httpUrl, maxSizeBytes: Arg.Any<long>()).Returns(Task.FromResult(bytes));
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
        await this.safeHttpClientService.Received(1).DownloadBytesAsync(httpUrl, maxSizeBytes: Arg.Any<long>());
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

        var xml = $"<?xml version=\"1.0\"?><methodCall><methodName>append</methodName><params><param><value><string>{System.Security.SecurityElement.Escape(magnetUri)}</string></value></param><param><value><string></string></value></param><param><value><string>tv</string></value></param></params></methodCall>";
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
    public async Task HandleRpc_EditQueue_GroupSetPriority_UpdatesTorrentPriority()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent { Id = 101, Priority = 0 };
        this.torrentService.Get(101).Returns(torrent);

        using var doc = JsonDocument.Parse("[\"groupsetpriority\", 50, \"\", [101]]");
        var request = new NzbgetRequest
        {
            Method = "editqueue",
            Params = doc.RootElement,
            Id = 20,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        torrent.Priority.Should().Be(50);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleRpc_EditQueue_HistoryReturn_CallsResumeAsync()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("[\"historyreturn\", 0, \"\", [102]]");
        var request = new NzbgetRequest
        {
            Method = "editqueue",
            Params = doc.RootElement,
            Id = 21,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        await this.torrentService.Received(1).ResumeAsync(102);
    }

    [Test]
    public async Task HandleRpc_EditQueue_HistoryRedownload_CallsResumeAsync()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("[\"historyredownload\", 0, \"\", [103]]");
        var request = new NzbgetRequest
        {
            Method = "editqueue",
            Params = doc.RootElement,
            Id = 22,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("result").GetBoolean().Should().BeTrue();

        await this.torrentService.Received(1).ResumeAsync(103);
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

    [Test]
    public async Task HandleRpc_Log_ReturnsEmptyArrayResult()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var request = new NzbgetRequest
        {
            Method = "log",
            Id = 42,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("version").GetString().Should().Be("1.1");
        doc.RootElement.GetProperty("id").GetInt32().Should().Be(42);
        var resElem = doc.RootElement.GetProperty("result");
        resElem.ValueKind.Should().Be(JsonValueKind.Array);
        resElem.GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task HandleRpc_LoadLog_ReturnsEmptyArrayResult()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var request = new NzbgetRequest
        {
            Method = "loadlog",
            Id = 43,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("version").GetString().Should().Be("1.1");
        doc.RootElement.GetProperty("id").GetInt32().Should().Be(43);
        var resElem = doc.RootElement.GetProperty("result");
        resElem.ValueKind.Should().Be(JsonValueKind.Array);
        resElem.GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task HandleXmlRpc_Log_ReturnsEmptyArrayResult()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>log</methodName></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<value><array><data");
        contentResult.Content.Should().Contain("</array></value>");
    }

    [Test]
    public async Task HandleXmlRpc_LoadLog_ReturnsEmptyArrayResult()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>loadlog</methodName></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<value><array><data");
        contentResult.Content.Should().Contain("</array></value>");
    }

    [Test]
    public async Task HandleXmlRpc_EditQueue_GroupSetPriority_UpdatesTorrentPriority()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>editqueue</methodName><params><param><value><string>groupsetpriority</string></value></param><param><value><int>75</int></value></param><param><value><string></string></value></param><param><value><array><data><value><int>201</int></value></data></array></value></param></params></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent { Id = 201, Priority = 0 };
        this.torrentService.Get(201).Returns(torrent);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<value><boolean>1</boolean></value>");

        torrent.Priority.Should().Be(75);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleXmlRpc_EditQueue_HistoryReturn_CallsResumeAsync()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>editqueue</methodName><params><param><value><string>historyreturn</string></value></param><param><value><int>0</int></value></param><param><value><string></string></value></param><param><value><array><data><value><int>202</int></value></data></array></value></param></params></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<value><boolean>1</boolean></value>");

        await this.torrentService.Received(1).ResumeAsync(202);
    }

    [Test]
    public async Task HandleXmlRpc_EditQueue_HistoryRedownload_CallsResumeAsync()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>editqueue</methodName><params><param><value><string>historyredownload</string></value></param><param><value><int>0</int></value></param><param><value><string></string></value></param><param><value><array><data><value><int>203</int></value></data></array></value></param></params></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<value><boolean>1</boolean></value>");

        await this.torrentService.Received(1).ResumeAsync(203);
    }

    [Test]
    public async Task HandleRpc_ListFiles_WithArrayParam_ReturnsMappedFileList()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var files = new List<TorrentFile>
        {
            new TorrentFile
            {
                Id = 1,
                Path = "movie.mkv",
                Size = 1073741824L,
                BytesCompleted = 1073741824L,
                Progress = 1.0,
            },
            new TorrentFile
            {
                Id = 2,
                Path = "sample.mkv",
                Size = 52428800L,
                BytesCompleted = 26214400L,
                Progress = 0.5,
            },
        };

        this.torrentFileService.GetFiles(101).Returns(files);

        using var doc = JsonDocument.Parse("[101]");
        var request = new NzbgetRequest
        {
            Method = "listfiles",
            Params = doc.RootElement,
            Id = 50,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        resDoc.RootElement.GetProperty("id").GetInt32().Should().Be(50);
        var resultArr = resDoc.RootElement.GetProperty("result");
        resultArr.ValueKind.Should().Be(JsonValueKind.Array);
        resultArr.GetArrayLength().Should().Be(2);

        var firstFile = resultArr[0];
        firstFile.GetProperty("ID").GetInt32().Should().Be(1);
        firstFile.GetProperty("NZBID").GetInt32().Should().Be(101);
        firstFile.GetProperty("FileName").GetString().Should().Be("movie.mkv");
        firstFile.GetProperty("Progress").GetInt32().Should().Be(1000);
        firstFile.GetProperty("Status").GetString().Should().Be("FINISHED");

        var secondFile = resultArr[1];
        secondFile.GetProperty("ID").GetInt32().Should().Be(2);
        secondFile.GetProperty("NZBID").GetInt32().Should().Be(101);
        secondFile.GetProperty("FileName").GetString().Should().Be("sample.mkv");
        secondFile.GetProperty("Progress").GetInt32().Should().Be(500);
    }

    [Test]
    public async Task HandleRpc_ListFiles_WithObjectParam_ReturnsMappedFileList()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var files = new List<TorrentFile>
        {
            new TorrentFile
            {
                Id = 10,
                Path = "episode.mkv",
                Size = 500000000L,
                BytesCompleted = 500000000L,
                Progress = 1.0,
            },
        };

        this.torrentFileService.GetFiles(102).Returns(files);

        using var doc = JsonDocument.Parse("{\"NZBID\": 102}");
        var request = new NzbgetRequest
        {
            Method = "listfiles",
            Params = doc.RootElement,
            Id = 51,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        var resultArr = resDoc.RootElement.GetProperty("result");
        resultArr.GetArrayLength().Should().Be(1);
        resultArr[0].GetProperty("FileName").GetString().Should().Be("episode.mkv");
    }

    [Test]
    public async Task HandleRpc_ListFiles_WhenNoFilesFound_ReturnsEmptyArray()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.torrentFileService.GetFiles(999).Returns(new List<TorrentFile>());

        using var doc = JsonDocument.Parse("[999]");
        var request = new NzbgetRequest
        {
            Method = "listfiles",
            Params = doc.RootElement,
            Id = 52,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var resDoc = JsonDocument.Parse(json);
        var resultArr = resDoc.RootElement.GetProperty("result");
        resultArr.ValueKind.Should().Be(JsonValueKind.Array);
        resultArr.GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task HandleXmlRpc_ListFiles_ReturnsMappedFileListXml()
    {
        var files = new List<TorrentFile>
        {
            new TorrentFile
            {
                Id = 5,
                Path = "track.flac",
                Size = 30000000L,
                BytesCompleted = 30000000L,
                Progress = 1.0,
            },
        };

        this.torrentFileService.GetFiles(301).Returns(files);

        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>listfiles</methodName><params><param><value><int>301</int></value></param></params></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<name>FileName</name><value><string>track.flac</string></value>");
        contentResult.Content.Should().Contain("<name>NZBID</name><value><int>301</int></value>");
    }

    [Test]
    public async Task HandleRpc_EditQueue_GroupMoveTop_ProcessesInReverseOrderToPreserveQueueOrder()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("[\"GroupMoveTop\", 0, \"\", [10, 20, 30]]");
        var request = new NzbgetRequest
        {
            Method = "editqueue",
            Params = doc.RootElement,
            Id = 60,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(30, "top");
            await this.torrentService.MoveQueueAsync(20, "top");
            await this.torrentService.MoveQueueAsync(10, "top");
        });
    }

    [Test]
    public async Task HandleRpc_EditQueue_GroupMoveBottom_ProcessesInForwardOrder()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse("[\"GroupMoveBottom\", 0, \"\", [10, 20, 30]]");
        var request = new NzbgetRequest
        {
            Method = "editqueue",
            Params = doc.RootElement,
            Id = 61,
        };

        var result = await this.controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(10, "bottom");
            await this.torrentService.MoveQueueAsync(20, "bottom");
            await this.torrentService.MoveQueueAsync(30, "bottom");
        });
    }

    [Test]
    public async Task HandleXmlRpc_EditQueue_GroupMoveTop_ProcessesInReverseOrder()
    {
        var xml = "<?xml version=\"1.0\"?><methodCall><methodName>editqueue</methodName><params><param><value><string>GroupMoveTop</string></value></param><param><value><int>0</int></value></param><param><value><string></string></value></param><param><value><array><data><value><int>10</int></value><value><int>20</int></value><value><int>30</int></value></data></array></value></param></params></methodCall>";
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(30, "top");
            await this.torrentService.MoveQueueAsync(20, "top");
            await this.torrentService.MoveQueueAsync(10, "top");
        });
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Synology;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class SynologyDownloadStationControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private SynologyDownloadStationController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new SynologyDownloadStationController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            this.safeHttpClientService);
    }

    [Test]
    public async Task HandleTask_Create_FallsBackToUrlInForm_WhenUriMissing()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        var magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "url", magnet },
            { "destination", "/volume1/downloads" },
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "create",
            id: null!,
            uri: null!,
            url: null!,
            destination: null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync(magnet, null, "/volume1/downloads", false);
    }

    [Test]
    public async Task HandleTask_Create_PrefersUriInForm_WhenBothUriAndUrlPresent()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        var magnetUri = "magnet:?xt=urn:btih:1111111111111111111111111111111111111111";
        var magnetUrl = "magnet:?xt=urn:btih:2222222222222222222222222222222222222222";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "uri", magnetUri },
            { "url", magnetUrl },
            { "destination", "/downloads" },
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "create",
            id: null!,
            uri: null!,
            url: null!,
            destination: null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync(magnetUri, null, "/downloads", false);
    }

    [Test]
    public async Task HandleTask_List_ReturnsAllMappedTasksWithStatusCodes()
    {
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrents = new List<Torrent>
        {
            new() { Id = 1, InfoHash = "hash111111111111111111111111111111111111", Name = "T1", Status = TorrentStatus.Downloading, TotalSize = 1000, Downloaded = 500 },
            new() { Id = 2, InfoHash = "hash222222222222222222222222222222222222", Name = "T2", Status = TorrentStatus.Paused, TotalSize = 2000, Downloaded = 1000 },
            new() { Id = 3, InfoHash = "hash333333333333333333333333333333333333", Name = "T3", Status = TorrentStatus.Stopped, Progress = 0.5, TotalSize = 3000, Downloaded = 1500 },
            new() { Id = 4, InfoHash = "hash444444444444444444444444444444444444", Name = "T4", Status = TorrentStatus.Stopped, Progress = 1.0, TotalSize = 4000, Downloaded = 4000 },
            new() { Id = 5, InfoHash = "hash555555555555555555555555555555555555", Name = "T5", Status = TorrentStatus.Completed, Progress = 1.0, TotalSize = 5000, Downloaded = 5000 },
            new() { Id = 6, InfoHash = "hash666666666666666666666666666666666666", Name = "T6", Status = TorrentStatus.Seeding, Progress = 1.0, TotalSize = 6000, Downloaded = 6000 },
            new() { Id = 7, InfoHash = "hash777777777777777777777777777777777777", Name = "T7", Status = TorrentStatus.Checking, TotalSize = 7000 },
            new() { Id = 8, InfoHash = "hash888888888888888888888888888888888888", Name = "T8", Status = TorrentStatus.Error, TotalSize = 8000 },
            new() { Id = 9, InfoHash = "hash999999999999999999999999999999999999", Name = "T9", Status = TorrentStatus.Queued, TotalSize = 9000 },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "list",
            id: null!,
            uri: null!,
            url: null!,
            destination: null!);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        var tasks = doc.RootElement.GetProperty("data").GetProperty("task");
        tasks.GetArrayLength().Should().Be(9);

        // Downloading: 2
        tasks[0].GetProperty("status").GetInt32().Should().Be(2);
        tasks[0].GetProperty("status_text").GetString().Should().Be("downloading");

        // Paused: 3
        tasks[1].GetProperty("status").GetInt32().Should().Be(3);
        tasks[1].GetProperty("status_text").GetString().Should().Be("paused");

        // Stopped (incomplete): 3
        tasks[2].GetProperty("status").GetInt32().Should().Be(3);
        tasks[2].GetProperty("status_text").GetString().Should().Be("paused");

        // Stopped (complete): 5
        tasks[3].GetProperty("status").GetInt32().Should().Be(5);
        tasks[3].GetProperty("status_text").GetString().Should().Be("finished");

        // Completed: 5
        tasks[4].GetProperty("status").GetInt32().Should().Be(5);
        tasks[4].GetProperty("status_text").GetString().Should().Be("finished");

        // Seeding: 7
        tasks[5].GetProperty("status").GetInt32().Should().Be(7);
        tasks[5].GetProperty("status_text").GetString().Should().Be("seeding");

        // Checking: 1
        tasks[6].GetProperty("status").GetInt32().Should().Be(1);
        tasks[6].GetProperty("status_text").GetString().Should().Be("checking");

        // Error: 9
        tasks[7].GetProperty("status").GetInt32().Should().Be(9);
        tasks[7].GetProperty("status_text").GetString().Should().Be("error");

        // Queued / other: 1
        tasks[8].GetProperty("status").GetInt32().Should().Be(1);
        tasks[8].GetProperty("status_text").GetString().Should().Be("waiting");
    }

    [Test]
    public async Task HandleTask_GetInfo_WithQueryIds_ReturnsFilteredTasks()
    {
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var hashA = "aaaa111122223333444455556666777788889999";
        var hashB = "bbbb111122223333444455556666777788889999";
        var torrents = new List<Torrent>
        {
            new() { Id = 10, InfoHash = hashA, Name = "Torrent.A", Status = TorrentStatus.Downloading },
            new() { Id = 20, InfoHash = hashB, Name = "Torrent.B", Status = TorrentStatus.Paused },
            new() { Id = 30, InfoHash = "cccc111122223333444455556666777788889999", Name = "Torrent.C" },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "getinfo",
            id: $"{hashA},20",
            uri: null!,
            url: null!,
            destination: null!);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var tasks = doc.RootElement.GetProperty("data").GetProperty("task");
        tasks.GetArrayLength().Should().Be(2);
    }

    [Test]
    public async Task HandleTask_Create_WithHttpUrl_DownloadsAndAddsTorrent()
    {
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var url = "http://example.com/movie.torrent";
        var bytes = new byte[] { 1, 2, 3 };
        var parsed = new ParsedTorrentInfo { InfoHash = "abc", Name = "Movie" };

        this.safeHttpClientService.DownloadBytesAsync(url, maxSizeBytes: Arg.Any<long>()).Returns(bytes);
        this.torrentFileParser.Parse(bytes).Returns(parsed);

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "create",
            id: null!,
            uri: url,
            url: null!,
            destination: "/downloads/movies");

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsed, null, "/downloads/movies", false, bytes);
    }

    [Test]
    public async Task HandleTask_Create_WithUploadedFile_ParsesAndAddsTorrent()
    {
        var fileBytes = new byte[] { 10, 20, 30 };
        var stream = new MemoryStream(fileBytes);
        var formFile = new FormFile(stream, 0, fileBytes.Length, "file", "test.torrent");
        var formFiles = new FormFileCollection { formFile };

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "multipart/form-data";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "method", "create" },
            { "destination", "/downloads" },
        }, formFiles);
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var parsed = new ParsedTorrentInfo { InfoHash = "filehash123", Name = "Uploaded.Torrent" };
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: null!,
            id: null!,
            uri: null!,
            url: null!,
            destination: null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsed, null, "/downloads", false, Arg.Any<byte[]>());
    }

    [Test]
    public async Task HandleTask_Delete_WithoutFiles_InvokesDeleteAsyncWithFalse()
    {
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var hash = "delete1111111111111111111111111111111111";
        var torrent = new Torrent { Id = 100, InfoHash = hash };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "delete",
            id: hash,
            uri: null!,
            url: null!,
            destination: null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).DeleteAsync(100, false);
    }

    [Test]
    public async Task HandleTask_Delete_WithDeleteDataTrue_InvokesDeleteAsyncWithTrue()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?force_complete=true");
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var hash = "delete2222222222222222222222222222222222";
        var torrent = new Torrent { Id = 101, InfoHash = hash };
        this.torrentService.GetByInfoHash(hash).Returns(torrent);

        var result = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "delete",
            id: hash,
            uri: null!,
            url: null!,
            destination: null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).DeleteAsync(101, true);
    }

    [Test]
    public async Task HandleTask_PauseAndResume_CallsTorrentService()
    {
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var hashA = "pause11111111111111111111111111111111111";
        var hashB = "pause22222222222222222222222222222222222";
        var torrentA = new Torrent { Id = 201, InfoHash = hashA };
        var torrentB = new Torrent { Id = 202, InfoHash = hashB };
        this.torrentService.GetByInfoHash(hashA).Returns(torrentA);
        this.torrentService.GetByInfoHash(hashB).Returns(torrentB);

        var pauseResult = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "pause",
            id: $"{hashA},{hashB}",
            uri: null!,
            url: null!,
            destination: null!);
        pauseResult.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).PauseAsync(201);
        await this.torrentService.Received(1).PauseAsync(202);

        var resumeResult = await this.controller.TaskHandler(
            api: "SYNO.DownloadStation.Task",
            method: "resume",
            id: $"{hashA},{hashB}",
            uri: null!,
            url: null!,
            destination: null!);
        resumeResult.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).ResumeAsync(201);
        await this.torrentService.Received(1).ResumeAsync(202);
    }

    [Test]
    public async Task HandleTask_SpecialApis_ReturnExpectedData()
    {
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // SYNO.DSM.Info
        var dsmResult = await this.controller.TaskHandler("SYNO.DSM.Info", "getinfo", null!, null!, null!, null!);
        dsmResult.Should().BeOfType<OkObjectResult>();
        var dsmJson = JsonSerializer.Serialize(((OkObjectResult)dsmResult).Value);
        using var dsmDoc = JsonDocument.Parse(dsmJson);
        dsmDoc.RootElement.GetProperty("data").GetProperty("version").GetString().Should().Be("7.2-64570");

        // SYNO.FileStation.List
        var fileStationResult = await this.controller.TaskHandler("SYNO.FileStation.List", "list", null!, null!, null!, null!);
        fileStationResult.Should().BeOfType<OkObjectResult>();
        var fsJson = JsonSerializer.Serialize(((OkObjectResult)fileStationResult).Value);
        using var fsDoc = JsonDocument.Parse(fsJson);
        fsDoc.RootElement.GetProperty("data").GetProperty("shares").GetArrayLength().Should().Be(1);

        // Unknown method
        var unknownResult = await this.controller.TaskHandler("SYNO.DownloadStation.Task", "unknownmethod", null!, null!, null!, null!);
        unknownResult.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public void Query_ReturnsSupportedApiList()
    {
        var result = this.controller.Query("SYNO.API.Info", "query", "all");
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").TryGetProperty("SYNO.DownloadStation.Task", out _).Should().BeTrue();
    }

    [Test]
    public void Auth_WhenAuthDisabled_ReturnsSessionToken()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var result = this.controller.Auth("SYNO.API.Auth", "login");
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").GetProperty("sid").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void Auth_WhenAuthEnabled_ValidPasswordSucceeds_InvalidPasswordFails()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("syno_master_key");

        // Invalid password
        var failResult = this.controller.Auth("SYNO.API.Auth", "login", passwd: "wrong");
        failResult.Should().BeOfType<OkObjectResult>();
        var failJson = JsonSerializer.Serialize(((OkObjectResult)failResult).Value);
        using (var doc = JsonDocument.Parse(failJson))
        {
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            doc.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(400);
        }

        // Valid password
        var successResult = this.controller.Auth("SYNO.API.Auth", "login", passwd: "syno_master_key");
        successResult.Should().BeOfType<OkObjectResult>();
        var successJson = JsonSerializer.Serialize(((OkObjectResult)successResult).Value);
        using (var doc = JsonDocument.Parse(successJson))
        {
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("data").GetProperty("sid").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Test]
    public void Info_And_Statistic_WhenAuthenticated_ReturnExpectedData()
    {
        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrents = new List<Torrent>
        {
            new() { Id = 1, DownloadSpeed = 50000, UploadSpeed = 20000 },
        };
        this.torrentService.GetAll().Returns(torrents);

        var infoResult = this.controller.Info("getinfo");
        infoResult.Should().BeOfType<OkObjectResult>();
        var infoJson = JsonSerializer.Serialize(((OkObjectResult)infoResult).Value);
        using (var doc = JsonDocument.Parse(infoJson))
        {
            doc.RootElement.GetProperty("data").GetProperty("version").GetInt32().Should().Be(3890);
        }

        var statResult = this.controller.Statistic("getstatistic");
        statResult.Should().BeOfType<OkObjectResult>();
        var statJson = JsonSerializer.Serialize(((OkObjectResult)statResult).Value);
        using (var doc = JsonDocument.Parse(statJson))
        {
            doc.RootElement.GetProperty("data").GetProperty("speed_download").GetInt64().Should().Be(50000);
        }
    }

    [Test]
    public async Task WhenAuthEnabled_UnauthenticatedRequestsReturn401_AuthenticatedSucceeds()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("master_key");

        // Unauthenticated Info call
        var unauthContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = unauthContext };
        var unauthInfo = this.controller.Info("getinfo");
        unauthInfo.Should().BeOfType<UnauthorizedResult>();

        // Unauthenticated Statistic call
        var unauthStat = this.controller.Statistic("getstatistic");
        unauthStat.Should().BeOfType<UnauthorizedResult>();

        // Unauthenticated TaskHandler call
        var unauthTask = await this.controller.TaskHandler("SYNO.DownloadStation.Task", "list", null!, null!, null!, null!);
        unauthTask.Should().BeOfType<UnauthorizedResult>();

        // Log in to get session token
        var authRes = (OkObjectResult)this.controller.Auth("SYNO.API.Auth", "login", passwd: "master_key");
        var authJson = JsonSerializer.Serialize(authRes.Value);
        using var doc = JsonDocument.Parse(authJson);
        var sid = doc.RootElement.GetProperty("data").GetProperty("sid").GetString();

        // Authenticated with _sid query parameter
        var authContext = new DefaultHttpContext();
        authContext.Request.QueryString = new QueryString($"?_sid={sid}");
        this.controller.ControllerContext = new ControllerContext { HttpContext = authContext };

        var authInfo = this.controller.Info("getinfo");
        authInfo.Should().BeOfType<OkObjectResult>();

        var authTask = await this.controller.TaskHandler("SYNO.DownloadStation.Task", "list", null!, null!, null!, null!);
        authTask.Should().BeOfType<OkObjectResult>();
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Freebox;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Freebox;

[TestFixture]
public class FreeboxDownloadControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private FreeboxDownloadController controller = null!;
    private DefaultHttpContext httpContext = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);
        this.configFileProvider.ApiKey.Returns("master-api-key");
        this.configService.DownloadDir.Returns("/downloads");

        this.controller = new FreeboxDownloadController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.configFileProvider,
            this.safeHttpClientService);

        this.httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = this.httpContext };
    }

    [Test]
    public void LoginAuthorize_ReturnsOkWithChallengeToken()
    {
        var result = this.controller.LoginAuthorize();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("logged_in").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("challenge").GetString().Should().Be("freebox-challenge-token");
    }

    [Test]
    public void LoginSession_WhenAuthenticationDisabled_ReturnsSessionToken()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var result = this.controller.LoginSession();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("logged_in").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("session_token").GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("result").GetProperty("permissions").GetProperty("downloader").GetBoolean().Should().BeTrue();
    }

    [Test]
    public void LoginSession_WhenAuthenticationEnabledAndNoCredentials_ReturnsAuthRequired()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = this.controller.LoginSession();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error_code").GetString().Should().Be("auth_required");
    }

    [Test]
    public void LoginSession_WhenAuthenticationEnabledAndValidApiKeyInHeader_ReturnsSessionToken()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.httpContext.Request.Headers["X-Fbx-App-Auth"] = "master-api-key";

        var result = this.controller.LoginSession();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("logged_in").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("session_token").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [TestCase("password", "master-api-key")]
    [TestCase("app_token", "master-api-key")]
    public void LoginSession_WhenAuthenticationEnabledAndValidQueryParam_ReturnsSessionToken(string paramKey, string paramValue)
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.httpContext.Request.QueryString = new QueryString($"?{paramKey}={paramValue}");

        var result = this.controller.LoginSession();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("logged_in").GetBoolean().Should().BeTrue();
    }

    [TestCase("password", "master-api-key")]
    [TestCase("app_token", "master-api-key")]
    [TestCase("api_key", "master-api-key")]
    public void LoginSession_WhenAuthenticationEnabledAndValidFormParam_ReturnsSessionToken(string formKey, string formValue)
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        this.httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            { formKey, formValue },
        });

        var result = this.controller.LoginSession();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("logged_in").GetBoolean().Should().BeTrue();
    }

    [Test]
    public void GetDownloadConfig_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = this.controller.GetDownloadConfig();

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void GetDownloadConfig_WhenAuthenticated_ReturnsBase64DownloadDirAndSettings()
    {
        this.configService.DownloadDir.Returns("/media/downloads");

        var result = this.controller.GetDownloadConfig();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var resultObj = doc.RootElement.GetProperty("result");
        resultObj.GetProperty("max_downloading_tasks").GetInt32().Should().Be(10);
        resultObj.GetProperty("use_watch_dir").GetBoolean().Should().BeFalse();

        var b64Dir = resultObj.GetProperty("download_dir").GetString();
        var decodedDir = Encoding.UTF8.GetString(Convert.FromBase64String(b64Dir!));
        decodedDir.Should().Be("/media/downloads");
    }

    [Test]
    public void GetDownloadConfig_WhenDownloadDirNull_FallsBackToDefaultDownloads()
    {
        this.configService.DownloadDir.Returns((string)null!);

        var result = this.controller.GetDownloadConfig();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var b64Dir = doc.RootElement.GetProperty("result").GetProperty("download_dir").GetString();
        var decodedDir = Encoding.UTF8.GetString(Convert.FromBase64String(b64Dir!));
        decodedDir.Should().Be("/downloads");
    }

    [Test]
    public void GetDownloads_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = this.controller.GetDownloads();

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestCase(TorrentStatus.Downloading, "downloading", 0.5, 5000L)]
    [TestCase(TorrentStatus.Seeding, "seeding", 1.0, 10000L)]
    [TestCase(TorrentStatus.Completed, "done", 1.0, 10000L)]
    [TestCase(TorrentStatus.Checking, "checking", 0.2, 2000L)]
    [TestCase(TorrentStatus.Paused, "stopped", 0.1, 1000L)]
    [TestCase(TorrentStatus.Stopped, "stopped", 0.4, 4000L)]
    [TestCase(TorrentStatus.Error, "error", 0.0, 0L)]
    [TestCase(TorrentStatus.Queued, "queued", 0.0, 0L)]
    public void GetDownloads_MapsAllTorrentStatusesAndProgressCalculations(
        TorrentStatus status,
        string expectedStatus,
        double progress,
        long expectedRxPct)
    {
        var torrents = new List<Torrent>
        {
            new()
            {
                Id = 101,
                Name = "Linux_ISO_Ubuntu",
                SavePath = "/downloads/isos",
                TotalSize = 2_000_000_000,
                Downloaded = 1_000_000_000,
                Uploaded = 500_000_000,
                Progress = progress,
                Ratio = 0.5,
                DownloadSpeed = 10_000_000,
                UploadSpeed = 2_000_000,
                Status = status,
                QueuePosition = 2,
                TargetRatio = 2.0,
                DateAdded = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                Eta = 120,
            },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = this.controller.GetDownloads();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var items = doc.RootElement.GetProperty("result");
        items.GetArrayLength().Should().Be(1);

        var item = items[0];
        item.GetProperty("id").GetInt32().Should().Be(101);
        item.GetProperty("name").GetString().Should().Be("Linux_ISO_Ubuntu");
        item.GetProperty("status").GetString().Should().Be(expectedStatus);
        item.GetProperty("rx_pct").GetInt64().Should().Be(expectedRxPct);
        item.GetProperty("tx_pct").GetInt64().Should().Be(5000L);
        item.GetProperty("rx_bytes").GetInt64().Should().Be(1_000_000_000);
        item.GetProperty("tx_bytes").GetInt64().Should().Be(500_000_000);
        item.GetProperty("rx_rate").GetInt64().Should().Be(10_000_000);
        item.GetProperty("tx_rate").GetInt64().Should().Be(2_000_000);
        item.GetProperty("type").GetString().Should().Be("bt");
        item.GetProperty("queue_pos").GetInt32().Should().Be(2);
        item.GetProperty("stop_ratio").GetInt32().Should().Be(200);
        item.GetProperty("eta").GetInt32().Should().Be(120);
        item.GetProperty("error").GetString().Should().Be("none");

        var b64Path = item.GetProperty("download_dir").GetString();
        Encoding.UTF8.GetString(Convert.FromBase64String(b64Path!)).Should().Be("/downloads/isos");
    }

    [Test]
    public void GetDownloads_WhenTorrentStoppedButCompleted_ReturnsDoneStatus()
    {
        var torrents = new List<Torrent>
        {
            new()
            {
                Id = 102,
                Name = "Finished_Torrent",
                TotalSize = 1000,
                Downloaded = 1000,
                Progress = 1.0,
                Status = TorrentStatus.Stopped,
            },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = this.controller.GetDownloads();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("result")[0].GetProperty("status").GetString().Should().Be("done");
    }

    [Test]
    public async Task AddDownload_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = await this.controller.AddDownload("magnet:?xt=urn:btih:dummy", null!);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task AddDownload_WithMagnetUri_DecodesBase64DownloadDirAndAddsTorrent()
    {
        var magnetUri = "magnet:?xt=urn:btih:fedcba9876543210fedcba9876543210fedcba98&dn=ArchLinux";
        var b64Dir = Convert.ToBase64String(Encoding.UTF8.GetBytes("/media/torrents"));
        var added = new Torrent { Id = 55, Name = "ArchLinux" };

        this.torrentService.AddFromMagnetAsync(magnetUri, null, "/media/torrents", false).Returns(added);

        var result = await this.controller.AddDownload(magnetUri, b64Dir);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("id").GetInt32().Should().Be(55);
        await this.torrentService.Received(1).AddFromMagnetAsync(magnetUri, null, "/media/torrents", false);
    }

    [Test]
    public async Task AddDownload_WithPlainTextDownloadDir_PreservesPlainTextDirectory()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu";
        var plainDir = "/var/downloads";
        var added = new Torrent { Id = 56, Name = "Ubuntu" };

        this.torrentService.AddFromMagnetAsync(magnetUri, null, plainDir, false).Returns(added);

        var result = await this.controller.AddDownload(magnetUri, plainDir);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync(magnetUri, null, plainDir, false);
    }

    [Test]
    public async Task AddDownload_WithHttpUrl_DownloadsBytesAndParsesTorrent()
    {
        var torrentUrl = "https://tracker.example.com/test.torrent";
        var dummyBytes = new byte[] { 1, 2, 3, 4, 5 };
        var parsed = new ParsedTorrentInfo { Name = "HttpTorrent" };
        var added = new Torrent { Id = 77, Name = "HttpTorrent" };

        this.configService.MaxTorrentFileSizeBytes.Returns(10 * 1024 * 1024L);
        this.safeHttpClientService.DownloadBytesAsync(torrentUrl, maxSizeBytes: 10 * 1024 * 1024L).Returns(dummyBytes);
        this.torrentFileParser.Parse(dummyBytes).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, dummyBytes).Returns(added);

        var result = await this.controller.AddDownload(torrentUrl, null!);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("result").GetProperty("id").GetInt32().Should().Be(77);
        await this.safeHttpClientService.Received(1).DownloadBytesAsync(torrentUrl, maxSizeBytes: 10 * 1024 * 1024L);
        this.torrentFileParser.Received(1).Parse(dummyBytes);
    }

    [Test]
    public async Task AddDownload_WithFileUpload_ReadsFileBytesAndParsesTorrent()
    {
        var fileContent = new byte[] { 10, 20, 30, 40 };
        var formFile = Substitute.For<IFormFile>();
        formFile.Length.Returns(fileContent.Length);
        formFile.FileName.Returns("uploaded.torrent");
        formFile.CopyToAsync(Arg.Any<Stream>()).Returns(callInfo =>
        {
            var stream = callInfo.Arg<Stream>();
            return stream.WriteAsync(fileContent, 0, fileContent.Length);
        });

        var fileCollection = new FormFileCollection { formFile };
        this.httpContext.Request.ContentType = "multipart/form-data";
        this.httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>(), fileCollection);

        var parsed = new ParsedTorrentInfo { Name = "UploadedTorrent" };
        var added = new Torrent { Id = 88, Name = "UploadedTorrent" };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, Arg.Any<byte[]>()).Returns(added);

        var result = await this.controller.AddDownload(null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("result").GetProperty("id").GetInt32().Should().Be(88);
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsed, null, null, false, Arg.Any<byte[]>());
    }

    [Test]
    public async Task DeleteDownload_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = await this.controller.DeleteDownload(42);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task DeleteDownload_WhenAuthenticated_CallsDeleteAsyncWithEraseFalse()
    {
        var result = await this.controller.DeleteDownload(42);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).DeleteAsync(42, false);
    }

    [Test]
    public async Task EraseDownload_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = await this.controller.EraseDownload(42);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task EraseDownload_WhenAuthenticated_CallsDeleteAsyncWithEraseTrue()
    {
        var result = await this.controller.EraseDownload(42);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).DeleteAsync(42, true);
    }

    [Test]
    public async Task UpdateDownload_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = await this.controller.UpdateDownload(42);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task UpdateDownload_ViaForm_HandlesStoppedStatusQueuePosAndRatio()
    {
        var torrent = new Torrent { Id = 42, TargetRatio = 1.0 };
        this.torrentService.Get(42).Returns(torrent);

        this.httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        this.httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "status", "stopped" },
            { "queue_pos", "top" },
            { "stop_ratio", "250" },
        });

        var result = await this.controller.UpdateDownload(42);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).PauseAsync(42);
        await this.torrentService.Received(1).MoveQueueAsync(42, "top");
        torrent.TargetRatio.Should().Be(2.5);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task UpdateDownload_ViaForm_HandlesDownloadingStatus()
    {
        this.httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        this.httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "status", "downloading" },
        });

        var result = await this.controller.UpdateDownload(42);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).ResumeAsync(42);
    }

    [Test]
    public async Task UpdateDownload_ViaJsonBody_HandlesStoppedStatusQueuePosAndRatio()
    {
        var torrent = new Torrent { Id = 42, TargetRatio = 1.0 };
        this.torrentService.Get(42).Returns(torrent);

        var jsonBody = JsonSerializer.Serialize(new
        {
            status = "stopped",
            queue_pos = "bottom",
            stop_ratio = 180.0,
        });
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        this.httpContext.Request.Body = new MemoryStream(bodyBytes);
        this.httpContext.Request.ContentType = "application/json";

        var result = await this.controller.UpdateDownload(42);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).PauseAsync(42);
        await this.torrentService.Received(1).MoveQueueAsync(42, "bottom");
        torrent.TargetRatio.Should().Be(1.8);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task UpdateDownload_ViaJsonBody_HandlesDownloadingStatus()
    {
        var jsonBody = JsonSerializer.Serialize(new
        {
            status = "downloading",
        });
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        this.httpContext.Request.Body = new MemoryStream(bodyBytes);
        this.httpContext.Request.ContentType = "application/json";

        var result = await this.controller.UpdateDownload(42);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).ResumeAsync(42);
    }

    [TestCase("X-Fbx-App-Auth")]
    [TestCase("session_token")]
    [TestCase("session")]
    [TestCase("token")]
    public async Task ProtectedEndpoints_AcceptAuthenticationViaQueryStringToken(string queryParam)
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-token-xyz");

        this.httpContext.Request.QueryString = new QueryString($"?{queryParam}=secret-token-xyz");

        var configResult = this.controller.GetDownloadConfig();
        configResult.Should().BeOfType<OkObjectResult>();

        var getResult = this.controller.GetDownloads();
        getResult.Should().BeOfType<OkObjectResult>();

        var deleteResult = await this.controller.DeleteDownload(10);
        deleteResult.Should().BeOfType<OkObjectResult>();
    }
}

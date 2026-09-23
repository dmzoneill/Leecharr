// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.NzbVortex;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.NzbVortex;

[TestFixture]
public class NzbVortexApiControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private NzbVortexApiController controller = null!;
    private DefaultHttpContext httpContext = null!;

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
        this.configService.DownloadDir.Returns("/downloads");

        this.controller = new NzbVortexApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            this.safeHttpClientService);

        this.httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = this.httpContext };
    }

    [Test]
    public void GetNonce_ReturnsNonceObject()
    {
        var result = this.controller.GetNonce();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("authNonce").GetString().Should().Be("leecharr-vortex-nonce");
        doc.RootElement.GetProperty("nonce").GetString().Should().Be("leecharr-vortex-nonce");
        doc.RootElement.GetProperty("error").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("result").GetInt32().Should().Be(0);
    }

    [Test]
    public void Login_WhenAuthenticationDisabled_ReturnsSuccessSessionToken()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var result = this.controller.Login();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("loginResult").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("auth").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("session").GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("error").GetInt32().Should().Be(0);
    }

    [Test]
    public void Login_WhenAuthenticationEnabledAndNotAuthenticated_ReturnsAuthFailure()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = this.controller.Login();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("loginResult").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("auth").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetInt32().Should().Be(401);
    }

    [Test]
    public void GetAppVersion_WhenUnauthenticated_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = this.controller.GetAppVersion();

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void GetAppVersion_WhenAuthenticated_ReturnsVersionString()
    {
        var result = this.controller.GetAppVersion();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("appVersion").GetString().Should().Be("3.4.2");
        doc.RootElement.GetProperty("error").GetInt32().Should().Be(0);
    }

    [Test]
    public void GetApiLevel_ReturnsExpectedLevelSeven()
    {
        var result = this.controller.GetApiLevel();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("apiLevel").GetInt32().Should().Be(7);
        doc.RootElement.GetProperty("error").GetInt32().Should().Be(0);
    }

    [Test]
    public void GetGroups_MapsCategoriesToGroupObjects()
    {
        var categories = new List<Category>
        {
            new() { Id = 1, Name = "Movies", SavePath = "/downloads/movies" },
            new() { Id = 2, Name = "TV", SavePath = null },
        };
        this.categoryService.GetAll().Returns(categories);

        var result = this.controller.GetGroups();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var groups = doc.RootElement.GetProperty("groups");
        groups.GetArrayLength().Should().Be(2);

        groups[0].GetProperty("groupName").GetString().Should().Be("Movies");
        groups[0].GetProperty("destinationPath").GetString().Should().Be("/downloads/movies");
        groups[0].GetProperty("isDefault").GetBoolean().Should().BeFalse();

        groups[1].GetProperty("groupName").GetString().Should().Be("TV");
        groups[1].GetProperty("destinationPath").GetString().Should().Be("/downloads");
    }

    [Test]
    public void GetNzbs_WhenUnauthenticated_ReturnsUnauthorized()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var result = this.controller.GetNzbs();

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestCase(TorrentStatus.Downloading, 1, false)]
    [TestCase(TorrentStatus.Seeding, 20, false)]
    [TestCase(TorrentStatus.Stopped, 20, false, 1.0, 1000L, 1000L)]
    [TestCase(TorrentStatus.Stopped, 0, true, 0.4, 400L, 1000L)]
    [TestCase(TorrentStatus.Paused, 0, true)]
    [TestCase(TorrentStatus.Error, 21, false)]
    public void GetNzbs_MapsStatusesStatesAndQueueFields(
        TorrentStatus status,
        int expectedState,
        bool expectedPaused,
        double progress = 0.5,
        long downloaded = 500,
        long totalSize = 1000)
    {
        var torrents = new List<Torrent>
        {
            new()
            {
                Id = 15,
                Name = "Ubuntu-Daily.iso",
                Category = "Linux",
                TotalSize = totalSize,
                Downloaded = downloaded,
                DownloadSpeed = 1024 * 1024,
                Status = status,
                SavePath = "/downloads/isos",
                Ratio = 1.25,
                Progress = progress,
            },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = this.controller.GetNzbs();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var nzbs = doc.RootElement.GetProperty("nzbs");
        nzbs.GetArrayLength().Should().Be(1);

        var nzb = nzbs[0];
        nzb.GetProperty("id").GetInt32().Should().Be(15);
        nzb.GetProperty("uiTitle").GetString().Should().Be("Ubuntu-Daily.iso");
        nzb.GetProperty("nzbFilename").GetString().Should().Be("Ubuntu-Daily.iso");
        nzb.GetProperty("groupName").GetString().Should().Be("Linux");
        nzb.GetProperty("totalDownloadSize").GetInt64().Should().Be(totalSize);
        nzb.GetProperty("downloadedSize").GetInt64().Should().Be(downloaded);
        nzb.GetProperty("isPaused").GetBoolean().Should().Be(expectedPaused);
        nzb.GetProperty("state").GetInt32().Should().Be(expectedState);
        nzb.GetProperty("statusText").GetString().Should().Be(status.ToString());
        nzb.GetProperty("destinationPath").GetString().Should().Be("/downloads/isos");
        nzb.GetProperty("downloadRatio").GetDouble().Should().Be(1.25);
        nzb.GetProperty("progress").GetDouble().Should().Be(progress);
    }

    [Test]
    public async Task AddNzb_WithFileUpload_ReadsFileBytesAndAddsTorrentWithCategory()
    {
        var fileContent = new byte[] { 1, 2, 3, 4 };
        var formFile = Substitute.For<IFormFile>();
        formFile.Length.Returns(fileContent.Length);
        formFile.FileName.Returns("test.nzb");
        formFile.CopyToAsync(Arg.Any<Stream>()).Returns(callInfo =>
        {
            var stream = callInfo.Arg<Stream>();
            return stream.WriteAsync(fileContent, 0, fileContent.Length);
        });

        var fileCollection = new FormFileCollection { formFile };
        this.httpContext.Request.ContentType = "multipart/form-data";
        this.httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>(), fileCollection);

        var parsed = new ParsedTorrent { Name = "ParsedTorrent" };
        var added = new Torrent { Id = 44, Name = "ParsedTorrent" };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, "movies", null, false, Arg.Any<byte[]>()).Returns(added);

        var result = await this.controller.AddNzb(queryGroup: "movies");

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("id").GetString().Should().Be("44");
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(parsed, "movies", null, false, Arg.Any<byte[]>());
    }

    [Test]
    public async Task AddNzb_WithMagnetUrlQuery_AddsMagnetTorrent()
    {
        var magnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
        this.httpContext.Request.QueryString = new QueryString($"?url={Uri.EscapeDataString(magnetUri)}&groupname=tv");

        var added = new Torrent { Id = 55, Name = "MagnetShow" };
        this.torrentService.AddFromMagnetAsync(magnetUri, "tv", null, false).Returns(added);

        var result = await this.controller.AddNzb(queryGroup: "tv");

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("id").GetString().Should().Be("55");
        await this.torrentService.Received(1).AddFromMagnetAsync(magnetUri, "tv", null, false);
    }

    [Test]
    public async Task AddNzb_WithHttpUrl_DownloadsBytesAndAddsParsedTorrent()
    {
        var torrentUrl = "https://tracker.example.com/item.torrent";
        this.httpContext.Request.QueryString = new QueryString($"?url={Uri.EscapeDataString(torrentUrl)}");

        var dummyBytes = new byte[] { 9, 8, 7, 6 };
        var parsed = new ParsedTorrent { Name = "HttpItem" };
        var added = new Torrent { Id = 66, Name = "HttpItem" };

        this.configService.MaxTorrentFileSizeBytes.Returns(50 * 1024 * 1024L);
        this.safeHttpClientService.DownloadBytesAsync(torrentUrl, maxSizeBytes: 50 * 1024 * 1024L).Returns(dummyBytes);
        this.torrentFileParser.Parse(dummyBytes).Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, "anime", null, false, dummyBytes).Returns(added);

        var result = await this.controller.AddNzb(queryGroup: "anime");

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("id").GetString().Should().Be("66");
        await this.safeHttpClientService.Received(1).DownloadBytesAsync(torrentUrl, maxSizeBytes: 50 * 1024 * 1024L);
        this.torrentFileParser.Received(1).Parse(dummyBytes);
    }

    [Test]
    public async Task PauseNzb_CallsTorrentServicePause()
    {
        var result = await this.controller.PauseNzb(12);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("error").GetInt32().Should().Be(0);
        await this.torrentService.Received(1).PauseAsync(12);
    }

    [Test]
    public async Task ResumeNzb_CallsTorrentServiceResume()
    {
        var result = await this.controller.ResumeNzb(12);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("error").GetInt32().Should().Be(0);
        await this.torrentService.Received(1).ResumeAsync(12);
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task CancelNzb_InvokesDeleteWithCorrectFileDeletionFlag(bool deleteFiles, bool expectedDeleteFiles)
    {
        var result = await this.controller.CancelNzb(33, deleteFiles: deleteFiles);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).DeleteAsync(33, expectedDeleteFiles);
    }

    [Test]
    public async Task CancelNzb_WhenPathContainsCancelDelete_DeletesWithFilesEvenIfParamFalse()
    {
        this.httpContext.Request.Path = "/nzbvortex/api/v1/nzb/33/cancelDelete";

        var result = await this.controller.CancelNzb(33, deleteFiles: false);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).DeleteAsync(33, true);
    }

    [Test]
    public void GetFiles_ReturnsEnrichedFilesList()
    {
        var torrent = new Torrent { Id = 5, Name = "Torrent5", Progress = 0.75 };
        var files = new List<TorrentFile>
        {
            new() { Id = 101, TorrentId = 5, Path = "video.mkv", Size = 1_000_000, BytesCompleted = 750_000, Priority = 1 },
            new() { Id = 102, TorrentId = 5, Path = "sample.mkv", Size = 50_000, BytesCompleted = 0, Priority = 0 },
        };

        this.torrentService.Get(5).Returns(torrent);
        this.torrentFileService.GetFiles(5).Returns(files);

        var result = this.controller.GetFiles(5);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var resultFiles = doc.RootElement.GetProperty("files");
        resultFiles.GetArrayLength().Should().Be(2);

        resultFiles[0].GetProperty("id").GetInt32().Should().Be(101);
        resultFiles[0].GetProperty("fileName").GetString().Should().Be("video.mkv");
        resultFiles[0].GetProperty("isIgnored").GetBoolean().Should().BeFalse();

        resultFiles[1].GetProperty("id").GetInt32().Should().Be(102);
        resultFiles[1].GetProperty("isIgnored").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task IgnoreFile_SetsPriorityToZero()
    {
        var result = await this.controller.IgnoreFile(102);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentFileService.Received(1).SetPriorityAsync(102, 0);
    }

    [Test]
    public async Task UnignoreFile_SetsPriorityToOne()
    {
        var result = await this.controller.UnignoreFile(102);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentFileService.Received(1).SetPriorityAsync(102, 1);
    }

    [TestCase("GetNonce")]
    [TestCase("Login")]
    public void OnActionExecuting_AllowsAnonymousActions(string actionName)
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var actionDescriptor = new ActionDescriptor
        {
            RouteValues = new Dictionary<string, string> { { "action", actionName } },
        };
        var actionContext = new ActionContext(this.httpContext, new RouteData(), actionDescriptor);
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), this.controller);

        this.controller.OnActionExecuting(context);

        context.Result.Should().BeNull();
    }

    [Test]
    public void OnActionExecuting_RejectsProtectedActionWhenUnauthenticated()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var actionDescriptor = new ActionDescriptor
        {
            RouteValues = new Dictionary<string, string> { { "action", "GetNzbs" } },
        };
        var actionContext = new ActionContext(this.httpContext, new RouteData(), actionDescriptor);
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), this.controller);

        this.controller.OnActionExecuting(context);

        context.Result.Should().NotBeNull();
        context.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)context.Result!;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Aria2;
using Leecharr.Api.V1.Deluge;
using Leecharr.Api.V1.Flood;
using Leecharr.Api.V1.Freebox;
using Leecharr.Api.V1.Hadouken;
using Leecharr.Api.V1.Nzbget;
using Leecharr.Api.V1.NzbVortex;
using Leecharr.Api.V1.RTorrent;
using Leecharr.Api.V1.Sabnzbd;
using Leecharr.Api.V1.Synology;
using Leecharr.Api.V1.Torrents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class RpcSsrfProtectionTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IUserService userService = null!;
    private IMediaEnrichmentService mediaEnrichmentService = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private IBroadcastSignalRMessage signalRBroadcaster = null!;
    private ISafeHttpClientService safeHttpClientService = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.userService = Substitute.For<IUserService>();
        this.mediaEnrichmentService = Substitute.For<IMediaEnrichmentService>();
        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);
        this.safeHttpClientService = new SafeHttpClientService();
    }

    [TearDown]
    public void TearDown()
    {
        (this.safeHttpClientService as IDisposable)?.Dispose();
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task TorrentController_AddTorrentJson_WithSsrfUrl_ThrowsSecurityException(string ssrfUrl)
    {
        var controller = new TorrentController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.mediaEnrichmentService,
            this.trackerEntryRepository,
            this.signalRBroadcaster,
            safeHttpClientService: this.safeHttpClientService);

        var request = new AddTorrentJsonRequest { DownloadUrl = ssrfUrl };

        var act = () => controller.AddTorrentJson(request);

        await act.Should().ThrowAsync<SecurityException>().WithMessage("*SSRF blocked*");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task DelugeJsonRpcController_CoreAddTorrentUrl_WithSsrfUrl_ReturnsDelugeErrorWithSsrfBlocked(string ssrfUrl)
    {
        var controller = new DelugeJsonRpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse($"{{\"method\":\"core.add_torrent_url\",\"params\":[\"{ssrfUrl}\",{{}}],\"id\":1}}");

        var result = await controller.HandleRpc(doc.RootElement);

        result.Should().BeOfType<JsonResult>();
        var jsonResult = (JsonResult)result;
        var json = JsonSerializer.Serialize(jsonResult.Value);
        json.Should().Contain("SSRF blocked");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task RTorrentController_LoadStart_WithSsrfUrl_ReturnsXmlRpcFaultWithSsrfBlocked(string ssrfUrl)
    {
        var controller = new RTorrentController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.torrentFileService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        var xml = $"<?xml version=\"1.0\"?><methodCall><methodName>load.start</methodName><params><param><value><string></string></value></param><param><value><string>{ssrfUrl}</string></value></param></params></methodCall>";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var content = (ContentResult)result;
        content.Content.Should().Contain("faultString").And.Contain("SSRF blocked");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task Aria2RpcController_AddUriJsonRpc_WithSsrfUrl_ReturnsJsonRpcErrorWithSsrfBlocked(string ssrfUrl)
    {
        var controller = new Aria2RpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        var json = $"{{\"jsonrpc\":\"2.0\",\"method\":\"aria2.addUri\",\"params\":[[\"{ssrfUrl}\"]],\"id\":1}}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleRpc();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var resJson = JsonSerializer.Serialize(okResult.Value);
        resJson.Should().Contain("SSRF blocked");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task Aria2RpcController_AddUriXmlRpc_WithSsrfUrl_ReturnsXmlRpcFaultWithSsrfBlocked(string ssrfUrl)
    {
        var controller = new Aria2RpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        var xml = $"<?xml version=\"1.0\"?><methodCall><methodName>aria2.adduri</methodName><params><param><value><string>{ssrfUrl}</string></value></param></params></methodCall>";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleRpc();

        result.Should().BeOfType<ContentResult>();
        var content = (ContentResult)result;
        content.Content.Should().Contain("faultString").And.Contain("SSRF blocked");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task FloodApiController_AddUrls_WithSsrfUrl_ThrowsSecurityException(string ssrfUrl)
    {
        var controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            this.userService,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var request = new FloodAddUrlsRequest { Urls = new List<string> { ssrfUrl } };

        var act = () => controller.AddUrls(request);

        await act.Should().ThrowAsync<SecurityException>().WithMessage("*SSRF blocked*");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task SabnzbdApiController_AddUrl_WithSsrfUrl_ThrowsSecurityException(string ssrfUrl)
    {
        var controller = new SabnzbdApiController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var act = () => controller.HandleApi("addurl", ssrfUrl, null!, null!, null!, null!);

        await act.Should().ThrowAsync<SecurityException>().WithMessage("*SSRF blocked*");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task SynologyDownloadStationController_CreateTask_WithSsrfUrl_ThrowsSecurityException(string ssrfUrl)
    {
        var controller = new SynologyDownloadStationController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var act = () => controller.TaskHandler(api: "SYNO.DownloadStation.Task", method: "create", id: null!, uri: ssrfUrl, url: null!, destination: null!);

        await act.Should().ThrowAsync<SecurityException>().WithMessage("*SSRF blocked*");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task FreeboxDownloadController_AddDownload_WithSsrfUrl_ThrowsSecurityException(string ssrfUrl)
    {
        var controller = new FreeboxDownloadController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var act = () => controller.AddDownload(download_url: ssrfUrl, download_dir: null!);

        await act.Should().ThrowAsync<SecurityException>().WithMessage("*SSRF blocked*");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task HadoukenRpcController_WebUiAdd_WithSsrfUrl_ReturnsHadoukenErrorWithSsrfBlocked(string ssrfUrl)
    {
        var controller = new HadoukenRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.torrentFileService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse($"{{\"method\":\"webui.addtorrent\",\"params\":[\"url\",\"{ssrfUrl}\"],\"id\":1}}");

        var result = await controller.HandleRpc(new HadoukenRpcRequest
        {
            Method = "webui.addtorrent",
            Params = doc.RootElement.GetProperty("params"),
            Id = 1,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        json.Should().Contain("SSRF blocked");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task HadoukenRpcController_TorrentsAdd_WithSsrfUrl_ReturnsHadoukenErrorWithSsrfBlocked(string ssrfUrl)
    {
        var controller = new HadoukenRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.torrentFileService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse($"{{\"method\":\"torrents.adduri\",\"params\":[\"{ssrfUrl}\"],\"id\":1}}");

        var result = await controller.HandleRpc(new HadoukenRpcRequest
        {
            Method = "torrents.adduri",
            Params = doc.RootElement.GetProperty("params"),
            Id = 1,
        });

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        json.Should().Contain("SSRF blocked");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task NzbVortexApiController_AddNzb_WithSsrfUrl_ThrowsSecurityException(string ssrfUrl)
    {
        var controller = new NzbVortexApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            { "url", ssrfUrl }
        });
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var act = () => controller.AddNzb();

        await act.Should().ThrowAsync<SecurityException>().WithMessage("*SSRF blocked*");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task RssSyncService_SyncRssFeedsAsync_WithSsrfUrl_CatchesSecurityExceptionAndDoesNotAddTorrent(string ssrfUrl)
    {
        var indexerRepo = Substitute.For<IIndexerRepository>();
        var ruleRepo = Substitute.For<IRssRuleRepository>();
        var torznab = Substitute.For<ITorznabClient>();
        var torrentSvc = Substitute.For<ITorrentService>();

        var service = new RssSyncService(
            indexerRepo,
            ruleRepo,
            torznab,
            torrentSvc,
            safeHttpClientService: this.safeHttpClientService);

        var indexer = new IndexerDefinition { Id = 1, Name = "Alpha", EnableRss = true };
        indexerRepo.GetRssEnabled().Returns(new List<IndexerDefinition> { indexer });

        var rule = new RssRule { Id = 1, Name = "All", IsEnabled = true };
        ruleRepo.GetEnabled().Returns(new List<RssRule> { rule });

        var release = new TorznabSearchResult
        {
            Guid = "ssrf-release-1",
            Title = "Ssrf.Release",
            DownloadUrl = ssrfUrl,
        };
        torznab.FetchRssAsync(indexer).Returns(Task.FromResult(new List<TorznabSearchResult> { release }));

        var grabbed = await service.SyncRssFeedsAsync();

        grabbed.Should().Be(0);
        await torrentSvc.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }

    [TestCase("http://127.0.0.1/evil.torrent")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    public async Task NzbgetRpcController_Append_WithSsrfUrl_ReturnsErrorWithSsrfBlocked(string ssrfUrl)
    {
        var controller = new NzbgetRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            safeHttpClientService: this.safeHttpClientService);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var doc = JsonDocument.Parse($"[\"{ssrfUrl}\", \"\", \"tv\"]");
        var request = new NzbgetRequest
        {
            Method = "append",
            Params = doc.RootElement,
            Id = 1,
        };

        var result = await controller.HandleRpc(request);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        json.Should().Contain("SSRF blocked");
        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(Arg.Any<ParsedTorrent>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<byte[]>());
    }
}

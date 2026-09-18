// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.UTorrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class UTorrentWebUiControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private ITrackerEntryRepository trackerEntryRepository = null!;
    private IDownloadEngine downloadEngine = null!;
    private UTorrentWebUiController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configFileProvider.AuthenticationEnabled.Returns(false);
        this.trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();

        this.controller = new UTorrentWebUiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            configFileProvider: this.configFileProvider,
            trackerEntryRepository: this.trackerEntryRepository,
            downloadEngine: this.downloadEngine);
    }

    [TestCase("removedata")]
    [TestCase("remove")]
    [TestCase("add-url")]
    [TestCase("start")]
    [TestCase("stop")]
    [TestCase("pause")]
    [TestCase("recheck")]
    [TestCase("setprops")]
    public async Task HandleWebUi_RejectsMutatingActionsViaGet(string action)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.HandleWebUi(null!, action, "hash123", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)result;
        objectResult.StatusCode.Should().Be(StatusCodes.Status405MethodNotAllowed);
    }

    [TestCase("getprops")]
    [TestCase("getsettings")]
    [TestCase("getfiles")]
    public async Task HandleWebUi_AllowsQueryActionsViaGet(string action)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.HandleWebUi(null!, action, "hash123", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task HandleWebUi_AllowsMutatingActionsViaPost()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent { Id = 42, InfoHash = "abc12345" };
        this.torrentService.GetByInfoHash("abc12345").Returns(torrent);

        var result = await this.controller.HandleWebUi(null!, "removedata", "abc12345", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).DeleteAsync(42, true);
    }

    [Test]
    public async Task HandleWebUi_AddUrl_FallsBackToPathInForm_WhenDownloadDirMissing()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(new System.Collections.Generic.Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            { "path", "/downloads/custom" },
            { "label", "tv-shows" },
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
        var result = await this.controller.HandleWebUi(null!, "add-url", null!, magnet, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).AddFromMagnetAsync(magnet, "tv-shows", "/downloads/custom", false);
    }

    [Test]
    public async Task HandleWebUi_QueueTop_WithMultiHash_MovesInReverseOrder()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrentA = new Torrent { Id = 10, InfoHash = "hashA", QueuePosition = 1 };
        var torrentB = new Torrent { Id = 20, InfoHash = "hashB", QueuePosition = 2 };
        var torrentC = new Torrent { Id = 30, InfoHash = "hashC", QueuePosition = 3 };
        this.torrentService.GetByInfoHash("hashA").Returns(torrentA);
        this.torrentService.GetByInfoHash("hashB").Returns(torrentB);
        this.torrentService.GetByInfoHash("hashC").Returns(torrentC);

        var result = await this.controller.HandleWebUi(null!, "queuetop", "hashA,hashB,hashC", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(30, "top");
            await this.torrentService.MoveQueueAsync(20, "top");
            await this.torrentService.MoveQueueAsync(10, "top");
        });
    }

    [Test]
    public async Task HandleWebUi_QueueBottom_WithMultiHash_MovesInForwardOrder()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrentA = new Torrent { Id = 10, InfoHash = "hashA", QueuePosition = 1 };
        var torrentB = new Torrent { Id = 20, InfoHash = "hashB", QueuePosition = 2 };
        var torrentC = new Torrent { Id = 30, InfoHash = "hashC", QueuePosition = 3 };
        this.torrentService.GetByInfoHash("hashA").Returns(torrentA);
        this.torrentService.GetByInfoHash("hashB").Returns(torrentB);
        this.torrentService.GetByInfoHash("hashC").Returns(torrentC);

        var result = await this.controller.HandleWebUi(null!, "queuebottom", "hashA|hashB|hashC", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(10, "bottom");
            await this.torrentService.MoveQueueAsync(20, "bottom");
            await this.torrentService.MoveQueueAsync(30, "bottom");
        });
    }

    [Test]
    public async Task HandleWebUi_QueueUp_WithMultiHash_MovesInAscendingQueuePositionOrder()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrentA = new Torrent { Id = 10, InfoHash = "hashA", QueuePosition = 4 };
        var torrentB = new Torrent { Id = 20, InfoHash = "hashB", QueuePosition = 2 };
        var torrentC = new Torrent { Id = 30, InfoHash = "hashC", QueuePosition = 3 };
        this.torrentService.GetByInfoHash("hashA").Returns(torrentA);
        this.torrentService.GetByInfoHash("hashB").Returns(torrentB);
        this.torrentService.GetByInfoHash("hashC").Returns(torrentC);

        var result = await this.controller.HandleWebUi(null!, "queueup", "hashA,hashB,hashC", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(20, "up");
            await this.torrentService.MoveQueueAsync(30, "up");
            await this.torrentService.MoveQueueAsync(10, "up");
        });
    }

    [Test]
    public async Task HandleWebUi_QueueDown_WithMultiHash_MovesInDescendingQueuePositionOrder()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrentA = new Torrent { Id = 10, InfoHash = "hashA", QueuePosition = 2 };
        var torrentB = new Torrent { Id = 20, InfoHash = "hashB", QueuePosition = 3 };
        var torrentC = new Torrent { Id = 30, InfoHash = "hashC", QueuePosition = 4 };
        this.torrentService.GetByInfoHash("hashA").Returns(torrentA);
        this.torrentService.GetByInfoHash("hashB").Returns(torrentB);
        this.torrentService.GetByInfoHash("hashC").Returns(torrentC);

        var result = await this.controller.HandleWebUi(null!, "queuedown", "hashA,hashB,hashC", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        Received.InOrder(async () =>
        {
            await this.torrentService.MoveQueueAsync(30, "down");
            await this.torrentService.MoveQueueAsync(20, "down");
            await this.torrentService.MoveQueueAsync(10, "down");
        });
    }

    [Test]
    public async Task HandleWebUi_GetFiles_ReturnsSixElementTuplesWithPieceOffsetsAndCounts()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent { Id = 42, InfoHash = "abc12345", Progress = 0.5 };
        this.torrentService.GetByInfoHash("abc12345").Returns(torrent);

        var files = new List<TorrentFile>
        {
            new()
            {
                Id = 1,
                TorrentId = 42,
                Path = "sample.mkv",
                Size = 1048576,
                BytesCompleted = 524288,
                Priority = 3,
                PieceOffset = 10,
                PieceCount = 20,
            },
        };
        this.torrentFileService.GetFiles(42).Returns(files);

        var result = await this.controller.HandleWebUi(null!, "getfiles", "abc12345", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
        var filesElem = doc.RootElement.GetProperty("files");
        filesElem[0].GetString().Should().Be("ABC12345");
        var firstFile = filesElem[1][0];
        firstFile.GetArrayLength().Should().Be(6);
        firstFile[0].GetString().Should().Be("sample.mkv");
        firstFile[1].GetInt64().Should().Be(1048576);
        firstFile[2].GetInt64().Should().Be(524288);
        firstFile[3].GetInt32().Should().Be(2);
        firstFile[4].GetInt32().Should().Be(10);
        firstFile[5].GetInt32().Should().Be(20);
    }

    [Test]
    public async Task HandleWebUi_SetProps_DlRateAndUlRate_PreventIntegerDivisionTruncation()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent { Id = 1, InfoHash = "abc12345", DownloadLimit = 0, UploadLimit = 0 };
        this.torrentService.GetByInfoHash("abc12345").Returns(torrent);

        // Sub-1024 B/s rates should round up to 1 KB/s, not truncate to 0 (unlimited)
        await this.controller.HandleWebUi(null!, "setprops", "abc12345", "dlrate", "500", null!, null!, null!, null!);
        torrent.DownloadLimit.Should().Be(1);

        await this.controller.HandleWebUi(null!, "setprops", "abc12345", "ulrate", "100", null!, null!, null!, null!);
        torrent.UploadLimit.Should().Be(1);

        // 0 resets rate limits to 0
        await this.controller.HandleWebUi(null!, "setprops", "abc12345", "dlrate", "0", null!, null!, null!, null!);
        torrent.DownloadLimit.Should().Be(0);

        // Higher rates round up properly
        await this.controller.HandleWebUi(null!, "setprops", "abc12345", "dlrate", "1500", null!, null!, null!, null!);
        torrent.DownloadLimit.Should().Be(2);

        await this.controller.HandleWebUi(null!, "setprops", "abc12345", "max_ul_rate", "2048", null!, null!, null!, null!);
        torrent.UploadLimit.Should().Be(2);
    }

    [Test]
    public async Task HandleWebUi_GetProps_ReturnsTargetSeedTimeAndFormattedTrackers()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc12345",
            TargetSeedTimeMinutes = 120,
            UploadLimit = 50,
            DownloadLimit = 100,
            TargetRatio = 1.5,
        };
        this.torrentService.GetByInfoHash("abc12345").Returns(torrent);

        var trackers = new List<TrackerEntry>
        {
            new() { TorrentId = 1, Url = "http://tracker1.com/announce", Tier = 0 },
            new() { TorrentId = 1, Url = "http://tracker2.com/announce", Tier = 0 },
            new() { TorrentId = 1, Url = "http://tracker3.com/announce", Tier = 1 },
        };
        this.trackerEntryRepository.GetByTorrentId(1).Returns(trackers);

        var result = await this.controller.HandleWebUi(null!, "getprops", "abc12345", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
        var prop = doc.RootElement.GetProperty("props")[0];
        prop.GetProperty("seed_time").GetInt32().Should().Be(120 * 60);
        prop.GetProperty("trackers").GetString().Should().Be("http://tracker1.com/announce\r\nhttp://tracker2.com/announce\r\n\r\nhttp://tracker3.com/announce");
    }

    [Test]
    public async Task HandleWebUi_GetProps_FallsBackToTorrentTrackerUrl_WhenRepositoryEmpty()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "abc12345",
            TrackerUrl = "http://fallback.com/announce",
            TargetSeedTimeMinutes = 0,
        };
        this.torrentService.GetByInfoHash("abc12345").Returns(torrent);
        this.trackerEntryRepository.GetByTorrentId(1).Returns(new List<TrackerEntry>());

        var result = await this.controller.HandleWebUi(null!, "getprops", "abc12345", null!, null!, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(okResult.Value));
        var prop = doc.RootElement.GetProperty("props")[0];
        prop.GetProperty("trackers").GetString().Should().Be("http://fallback.com/announce");
        prop.GetProperty("seed_time").GetInt32().Should().Be(0);
    }

    [Test]
    public async Task HandleWebUi_SetProps_Trackers_UpdatesRepositoryAndEngine()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var torrent = new Torrent { Id = 1, InfoHash = "abc12345", TrackerUrl = "http://old.com/announce" };
        this.torrentService.GetByInfoHash("abc12345").Returns(torrent);
        this.trackerEntryRepository.GetByTorrentId(1).Returns(new List<TrackerEntry>
        {
            new() { TorrentId = 1, Url = "http://old.com/announce", Tier = 0 },
        });

        var trackerPayload = "http://t1.com/announce\r\nhttp://t2.com/announce\r\n\r\nhttp://t3.com/announce";
        var result = await this.controller.HandleWebUi(null!, "setprops", "abc12345", "trackers", trackerPayload, null!, null!, null!, null!);

        result.Should().BeOfType<OkObjectResult>();
        this.trackerEntryRepository.Received(1).DeleteByTorrentId(1);
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.TorrentId == 1 && t.Url == "http://t1.com/announce" && t.Tier == 0));
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.TorrentId == 1 && t.Url == "http://t2.com/announce" && t.Tier == 0));
        this.trackerEntryRepository.Received(1).Insert(Arg.Is<TrackerEntry>(t => t.TorrentId == 1 && t.Url == "http://t3.com/announce" && t.Tier == 1));

        await this.downloadEngine.Received(1).RemoveTrackersAsync(1, Arg.Is<IEnumerable<string>>(urls => urls.Contains("http://old.com/announce")));
        await this.downloadEngine.Received(1).AddTrackersAsync(1, Arg.Is<IEnumerable<string>>(urls => urls.Count() == 3));

        torrent.TrackerUrl.Should().Be("http://t1.com/announce");
        await this.torrentService.Received().UpdateAsync(torrent);
    }
}

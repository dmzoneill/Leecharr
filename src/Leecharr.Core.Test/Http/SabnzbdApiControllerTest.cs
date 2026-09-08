// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Sabnzbd;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class SabnzbdApiControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IDiskProvider diskProvider = null!;
    private SabnzbdApiController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.diskProvider = Substitute.For<IDiskProvider>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new SabnzbdApiController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider,
            diskProvider: this.diskProvider);
    }

    [Test]
    public async Task HandleApi_Queue_ReturnsFreeAndTotalDiskSpaceFromDiskProvider()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        this.configService.IncompleteDownloadDir.Returns("/incomplete");
        this.torrentService.GetAll().Returns(new List<Torrent>());

        // 1 TB = 1073741824000 bytes -> 1000.00 GB
        this.diskProvider.GetAvailableSpace(Arg.Any<string>()).Returns(1073741824000L);
        // 2 TB = 2147483648000 bytes -> 2000.00 GB
        this.diskProvider.GetTotalSize(Arg.Any<string>()).Returns(2147483648000L);

        var result = await this.controller.HandleApi(
            mode: "queue",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var queue = doc.RootElement.GetProperty("queue");
        queue.GetProperty("diskspace1").GetString().Should().Be("1000.00");
        queue.GetProperty("diskspace2").GetString().Should().Be("1000.00");
        queue.GetProperty("diskspacetotal1").GetString().Should().Be("2000.00");
        queue.GetProperty("diskspacetotal2").GetString().Should().Be("2000.00");
    }

    [Test]
    public async Task HandleApi_Version_ReturnsVersion()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleApi(
            mode: "version",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("version").GetString().Should().Be("4.3.2");
    }

    [Test]
    public async Task HandleApi_History_ReturnsCompletedTorrentsWithAbsoluteCompletenameAndUnixTimestamp()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");

        var completedDate = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var expectedEpoch = new DateTimeOffset(completedDate).ToUnixTimeSeconds();

        var torrents = new List<Torrent>
        {
            new Torrent
            {
                Id = 1,
                InfoHash = "hash1",
                Name = "Show.S01E01.mkv",
                SavePath = "/downloads/tv",
                Status = TorrentStatus.Seeding,
                Progress = 1.0,
                TotalSize = 104857600,
                DateAdded = completedDate.AddHours(-1),
                DateCompleted = completedDate,
            },
            new Torrent
            {
                Id = 2,
                InfoHash = "hash2",
                Name = "Movie.2025.mkv",
                SavePath = "/downloads/movies",
                Status = TorrentStatus.Completed,
                Progress = 1.0,
                TotalSize = 209715200,
                DateAdded = completedDate.AddHours(-2),
                DateCompleted = completedDate,
            },
            new Torrent
            {
                Id = 3,
                InfoHash = "hash3",
                Name = "Partial.mkv",
                SavePath = "/downloads",
                Status = TorrentStatus.Stopped,
                Progress = 0.5,
                TotalSize = 52428800,
                DateAdded = completedDate.AddHours(-1),
            },
        };

        this.torrentService.GetAll().Returns(torrents);

        var result = await this.controller.HandleApi(
            mode: "history",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var history = doc.RootElement.GetProperty("history");
        history.GetProperty("noofslots").GetInt32().Should().Be(2);

        var slots = history.GetProperty("slots");
        slots.GetArrayLength().Should().Be(2);

        var slot0 = slots[0];
        slot0.GetProperty("nzo_id").GetString().Should().Be("hash1");
        slot0.GetProperty("completename").GetString().Should().Be(Path.Combine("/downloads/tv", "Show.S01E01.mkv"));
        slot0.GetProperty("completed").GetInt64().Should().Be(expectedEpoch);
        slot0.GetProperty("status").GetString().Should().Be("Completed");

        var slot1 = slots[1];
        slot1.GetProperty("nzo_id").GetString().Should().Be("hash2");
        slot1.GetProperty("completename").GetString().Should().Be(Path.Combine("/downloads/movies", "Movie.2025.mkv"));
        slot1.GetProperty("completed").GetInt64().Should().Be(expectedEpoch);
    }

    [Test]
    public async Task HandleApi_AddLocalFile_WithExistingFilePath_ParsesAndAddsTorrent()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tempFile = Path.GetTempFileName();
        try
        {
            var dummyBytes = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(tempFile, dummyBytes);

            var parsedTorrent = new ParsedTorrent { Name = "Test Torrent" };
            this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsedTorrent);

            var addedTorrent = new Torrent { Id = 10, InfoHash = "addedhash" };
            this.torrentService.AddFromParsedTorrentAsync(parsedTorrent, "tv", null, false, Arg.Any<byte[]>())
                .Returns(Task.FromResult(addedTorrent));

            var result = await this.controller.HandleApi(
                mode: "addlocalfile",
                name: tempFile,
                value: null,
                cat: "tv",
                priority: "1",
                output: null);

            result.Should().BeOfType<OkObjectResult>();
            var okResult = (OkObjectResult)result;
            var json = JsonSerializer.Serialize(okResult.Value);
            using var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("status").GetBoolean().Should().BeTrue();
            var nzoIds = doc.RootElement.GetProperty("nzo_ids");
            nzoIds.GetArrayLength().Should().Be(1);
            nzoIds[0].GetString().Should().Be("addedhash");

            await this.torrentService.Received(1).UpdateAsync(Arg.Is<Torrent>(t => t.Priority == 1));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task HandleApi_AddLocalFile_WithNonExistentFile_ReturnsBadRequest()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleApi(
            mode: "addlocalfile",
            name: "/non/existent/path/test.torrent",
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task HandleApi_Queue_SetPriority_NumericZero_DoesNotMoveQueueAndSetsPriorityZero()
    {
        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "value2", "0" }
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent { Id = 42, InfoHash = "testhash", Priority = 1 };
        this.torrentService.GetByInfoHash("testhash").Returns(torrent);

        var result = await this.controller.HandleApi(
            mode: "queue",
            name: "priority",
            value: "testhash",
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        torrent.Priority.Should().Be(0);
        await this.torrentService.Received(1).UpdateAsync(torrent);
        await this.torrentService.DidNotReceive().MoveQueueAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [TestCase("low", -1)]
    [TestCase("-1", -1)]
    [TestCase("normal", 0)]
    [TestCase("default", 0)]
    [TestCase("0", 0)]
    [TestCase("high", 1)]
    [TestCase("1", 1)]
    [TestCase("force", 2)]
    [TestCase("forced", 2)]
    [TestCase("2", 2)]
    [TestCase("paused", -2)]
    [TestCase("stop", -2)]
    [TestCase("stopped", -2)]
    [TestCase("-2", -2)]
    public async Task HandleApi_Queue_SetPriority_VariousPriorityStringsAndNumbers_SetsExpectedPriority(string priorityInput, int expectedPriority)
    {
        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "value2", priorityInput }
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent { Id = 101, InfoHash = "hashtest", Priority = 99 };
        this.torrentService.GetByInfoHash("hashtest").Returns(torrent);

        var result = await this.controller.HandleApi(
            mode: "queue",
            name: "priority",
            value: "hashtest",
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        torrent.Priority.Should().Be(expectedPriority);
        await this.torrentService.Received(1).UpdateAsync(torrent);
        await this.torrentService.DidNotReceive().MoveQueueAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [TestCase("move_top", "top")]
    [TestCase("move_bottom", "bottom")]
    [TestCase("move_up", "up")]
    [TestCase("move_down", "down")]
    public async Task HandleApi_Queue_MoveCommands_CallsMoveQueueAsync(string moveCommand, string expectedDirection)
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent { Id = 77, InfoHash = "movehash" };
        this.torrentService.GetByInfoHash("movehash").Returns(torrent);

        var result = await this.controller.HandleApi(
            mode: "queue",
            name: moveCommand,
            value: "movehash",
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).MoveQueueAsync(77, expectedDirection);
    }

    [Test]
    public async Task HandleApi_Queue_Slots_ReturnsCorrectPriorityStrings()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        this.configService.IncompleteDownloadDir.Returns("/incomplete");

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, InfoHash = "h1", Name = "T1", Status = TorrentStatus.Downloading, Priority = 1 },
            new Torrent { Id = 2, InfoHash = "h2", Name = "T2", Status = TorrentStatus.Downloading, Priority = -1 },
            new Torrent { Id = 3, InfoHash = "h3", Name = "T3", Status = TorrentStatus.Downloading, Priority = 0 },
            new Torrent { Id = 4, InfoHash = "h4", Name = "T4", Status = TorrentStatus.Downloading, Priority = 2 },
            new Torrent { Id = 5, InfoHash = "h5", Name = "T5", Status = TorrentStatus.Downloading, Priority = -2 },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = await this.controller.HandleApi(
            mode: "queue",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var slots = doc.RootElement.GetProperty("queue").GetProperty("slots");
        slots[0].GetProperty("priority").GetString().Should().Be("High");
        slots[1].GetProperty("priority").GetString().Should().Be("Low");
        slots[2].GetProperty("priority").GetString().Should().Be("Normal");
        slots[3].GetProperty("priority").GetString().Should().Be("Force");
        slots[4].GetProperty("priority").GetString().Should().Be("Paused");
    }

    [Test]
    public async Task HandleApi_DirectPriorityMode_SetsPriority()
    {
        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "value2", "high" }
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var torrent = new Torrent { Id = 88, InfoHash = "directhash", Priority = 0 };
        this.torrentService.GetByInfoHash("directhash").Returns(torrent);

        var result = await this.controller.HandleApi(
            mode: "priority",
            name: null,
            value: "directhash",
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        torrent.Priority.Should().Be(1);
        await this.torrentService.Received(1).UpdateAsync(torrent);
    }

    [Test]
    public async Task HandleApi_Queue_WithStartAndLimit_ReturnsPagedSlotsWhilePreservingTotalCount()
    {
        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "start", "1" },
            { "limit", "2" }
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        this.configService.IncompleteDownloadDir.Returns("/incomplete");

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, InfoHash = "h1", Name = "T1", Status = TorrentStatus.Downloading },
            new Torrent { Id = 2, InfoHash = "h2", Name = "T2", Status = TorrentStatus.Downloading },
            new Torrent { Id = 3, InfoHash = "h3", Name = "T3", Status = TorrentStatus.Downloading },
            new Torrent { Id = 4, InfoHash = "h4", Name = "T4", Status = TorrentStatus.Downloading },
            new Torrent { Id = 5, InfoHash = "h5", Name = "T5", Status = TorrentStatus.Downloading },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = await this.controller.HandleApi(
            mode: "queue",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var queue = doc.RootElement.GetProperty("queue");
        queue.GetProperty("noofslots_total").GetInt32().Should().Be(5);
        queue.GetProperty("noofslots").GetInt32().Should().Be(5);

        var slots = queue.GetProperty("slots");
        slots.GetArrayLength().Should().Be(2);
        slots[0].GetProperty("nzo_id").GetString().Should().Be("h2");
        slots[1].GetProperty("nzo_id").GetString().Should().Be("h3");
    }

    [Test]
    public async Task HandleApi_Queue_WithStartBeyondCount_ReturnsEmptySlotsWhilePreservingTotalCount()
    {
        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "start", "10" },
            { "limit", "5" }
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        this.configService.IncompleteDownloadDir.Returns("/incomplete");

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, InfoHash = "h1", Name = "T1", Status = TorrentStatus.Downloading },
            new Torrent { Id = 2, InfoHash = "h2", Name = "T2", Status = TorrentStatus.Downloading },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = await this.controller.HandleApi(
            mode: "queue",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var queue = doc.RootElement.GetProperty("queue");
        queue.GetProperty("noofslots_total").GetInt32().Should().Be(2);
        queue.GetProperty("slots").GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task HandleApi_History_WithStartAndLimit_ReturnsPagedSlotsWhilePreservingTotalCount()
    {
        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "start", "2" },
            { "limit", "2" }
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        var completedDate = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, InfoHash = "h1", Name = "T1", Status = TorrentStatus.Completed, Progress = 1.0, TotalSize = 1000, DateCompleted = completedDate },
            new Torrent { Id = 2, InfoHash = "h2", Name = "T2", Status = TorrentStatus.Completed, Progress = 1.0, TotalSize = 2000, DateCompleted = completedDate },
            new Torrent { Id = 3, InfoHash = "h3", Name = "T3", Status = TorrentStatus.Completed, Progress = 1.0, TotalSize = 3000, DateCompleted = completedDate },
            new Torrent { Id = 4, InfoHash = "h4", Name = "T4", Status = TorrentStatus.Completed, Progress = 1.0, TotalSize = 4000, DateCompleted = completedDate },
            new Torrent { Id = 5, InfoHash = "h5", Name = "T5", Status = TorrentStatus.Completed, Progress = 1.0, TotalSize = 5000, DateCompleted = completedDate },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = await this.controller.HandleApi(
            mode: "history",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var history = doc.RootElement.GetProperty("history");
        history.GetProperty("noofslots").GetInt32().Should().Be(5);

        var slots = history.GetProperty("slots");
        slots.GetArrayLength().Should().Be(2);
        slots[0].GetProperty("nzo_id").GetString().Should().Be("h3");
        slots[1].GetProperty("nzo_id").GetString().Should().Be("h4");
    }

    [Test]
    public async Task HandleApi_History_WithFormParametersStartAndLimit_ReturnsPagedSlots()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            { "start", "1" },
            { "limit", "2" }
        });
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        this.configService.DownloadDir.Returns("/downloads");
        var completedDate = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, InfoHash = "h1", Name = "T1", Status = TorrentStatus.Completed, Progress = 1.0, TotalSize = 1000, DateCompleted = completedDate },
            new Torrent { Id = 2, InfoHash = "h2", Name = "T2", Status = TorrentStatus.Completed, Progress = 1.0, TotalSize = 2000, DateCompleted = completedDate },
            new Torrent { Id = 3, InfoHash = "h3", Name = "T3", Status = TorrentStatus.Completed, Progress = 1.0, TotalSize = 3000, DateCompleted = completedDate },
        };
        this.torrentService.GetAll().Returns(torrents);

        var result = await this.controller.HandleApi(
            mode: "history",
            name: null,
            value: null,
            cat: null,
            priority: null,
            output: null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);

        var history = doc.RootElement.GetProperty("history");
        history.GetProperty("noofslots").GetInt32().Should().Be(3);

        var slots = history.GetProperty("slots");
        slots.GetArrayLength().Should().Be(2);
        slots[0].GetProperty("nzo_id").GetString().Should().Be("h2");
        slots[1].GetProperty("nzo_id").GetString().Should().Be("h3");
    }
}

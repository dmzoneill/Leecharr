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
}

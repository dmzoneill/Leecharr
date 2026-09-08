// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Flood;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class FloodApiControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private FloodApiController controller = null!;

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

        this.controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            configFileProvider: this.configFileProvider);
    }

    [Test]
    public async Task AddFiles_WithMultipartFormData_SuccessfullyAddsTorrents()
    {
        var dummyTorrentBytes = new byte[] { 0x64, 0x38, 0x3a, 0x61, 0x6e, 0x6e, 0x6f, 0x75, 0x6e, 0x63, 0x65, 0x65 };
        var parsedTorrent = new ParsedTorrent
        {
            InfoHash = "1234567890abcdef1234567890abcdef12345678",
            Name = "Sample Torrent",
            TotalSize = 1024,
        };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsedTorrent);

        var formFile = new FormFile(
            new MemoryStream(dummyTorrentBytes),
            0,
            dummyTorrentBytes.Length,
            "torrents",
            "test.torrent")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/x-bittorrent"
        };

        var formCollection = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                { "destination", "/downloads/completed" },
                { "tags", "linux,iso" },
                { "start", "true" }
            },
            new FormFileCollection { formFile });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW";
        httpContext.Request.Form = formCollection;

        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.AddFiles();

        result.Should().BeOfType<OkObjectResult>();
        this.torrentFileParser.Received(1).Parse(Arg.Any<byte[]>());
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(
            parsedTorrent,
            "linux",
            "/downloads/completed",
            false,
            Arg.Any<byte[]>());
    }

    [Test]
    public async Task AddFiles_WithJsonBase64Body_SuccessfullyAddsTorrents()
    {
        var dummyTorrentBytes = new byte[] { 0x64, 0x38, 0x3a, 0x61, 0x6e, 0x6e, 0x6f, 0x75, 0x6e, 0x63, 0x65, 0x65 };
        var b64String = Convert.ToBase64String(dummyTorrentBytes);
        var parsedTorrent = new ParsedTorrent
        {
            InfoHash = "abcdefabcdefabcdefabcdefabcdefabcdefabcd",
            Name = "JSON Torrent",
            TotalSize = 2048,
        };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsedTorrent);

        var jsonPayload = JsonSerializer.Serialize(new
        {
            files = new[] { b64String },
            destination = "/downloads/movies",
            tags = new[] { "movies" },
            start = false
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonPayload));

        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await this.controller.AddFiles();

        result.Should().BeOfType<OkObjectResult>();
        this.torrentFileParser.Received(1).Parse(Arg.Any<byte[]>());
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(
            parsedTorrent,
            "movies",
            "/downloads/movies",
            true,
            Arg.Any<byte[]>());
    }

    [Test]
    public void GetPeers_WithActiveSwarm_MapsChokingAndEncryptionFlags()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Flood Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        var mockTask = Substitute.For<IDownloadTask>();
        mockTask.GetPeers().Returns(new List<PeerInfo>
        {
            new PeerInfo
            {
                Ip = "10.0.0.1",
                Client = "Transmission/3.00",
                DownloadSpeed = 50000,
                UploadSpeed = 20000,
                Progress = 0.5,
                Flags = "uE",
                IsEncrypted = true,
                IsIncoming = true,
                IsUtp = true,
                IsChoked = false,
                IsInterested = true,
                ClientIsChoked = true,
                ClientIsInterested = false,
            },
        });
        this.torrentService.GetDownloadTask(42).Returns(mockTask);

        var result = this.controller.GetTorrentPeers("aabbccddeeff00112233445566778899aabbccdd");
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement.EnumerateArray().ToList();
        array.Count.Should().Be(1);
        var peer = array[0];
        peer.GetProperty("address").GetString().Should().Be("10.0.0.1");
        peer.GetProperty("isEncrypted").GetBoolean().Should().BeTrue();
        peer.GetProperty("isIncoming").GetBoolean().Should().BeTrue();
        peer.GetProperty("isUtp").GetBoolean().Should().BeTrue();
        peer.GetProperty("peerIsChoked").GetBoolean().Should().BeFalse();
        peer.GetProperty("peerIsInterested").GetBoolean().Should().BeTrue();
        peer.GetProperty("clientIsChoked").GetBoolean().Should().BeTrue();
        peer.GetProperty("clientIsInterested").GetBoolean().Should().BeFalse();
    }

    [TestCase(0, 0)]
    [TestCase(-1, 0)]
    [TestCase(1, 1)]
    [TestCase(2, 1)]
    [TestCase(3, 1)]
    [TestCase(4, 2)]
    [TestCase(7, 2)]
    public void ToFloodPriority_MapsInternalToFloodPriorityCorrectly(int internalPrio, int expectedFloodPrio)
    {
        FloodApiController.ToFloodPriority(internalPrio).Should().Be(expectedFloodPrio);
    }

    [TestCase(0, 0)]
    [TestCase(1, 3)]
    [TestCase(2, 4)]
    [TestCase(99, 3)]
    public void FromFloodPriority_MapsFloodToInternalPriorityCorrectly(int floodPrio, int expectedInternalPrio)
    {
        FloodApiController.FromFloodPriority(floodPrio).Should().Be(expectedInternalPrio);
    }

    [Test]
    public void GetContents_MapsFilePrioritiesToFloodScale()
    {
        var torrent = new Torrent
        {
            Id = 50,
            Name = "File Priority Torrent",
            InfoHash = "1122334455667788990011223344556677889900",
        };
        this.torrentService.GetByInfoHash("1122334455667788990011223344556677889900").Returns(torrent);

        var files = new List<TorrentFile>
        {
            new() { Id = 1, Path = "file1.mkv", Size = 1000, Progress = 0.5, Priority = 0 }, // Should map to 0
            new() { Id = 2, Path = "file2.mkv", Size = 2000, Progress = 1.0, Priority = 3 }, // Should map to 1
            new() { Id = 3, Path = "file3.mkv", Size = 3000, Progress = 0.0, Priority = 4 }, // Should map to 2
        };
        this.torrentFileService.GetFiles(50).Returns(files);

        var result = this.controller.GetContents("1122334455667788990011223344556677889900");
        result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var array = doc.RootElement.EnumerateArray().ToList();
        array.Count.Should().Be(3);

        array[0].GetProperty("priority").GetInt32().Should().Be(0);
        array[1].GetProperty("priority").GetInt32().Should().Be(1);
        array[2].GetProperty("priority").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task SetContentsPriority_MapsFloodPriorityToInternalScale()
    {
        var torrent = new Torrent
        {
            Id = 51,
            Name = "Priority Set Torrent",
            InfoHash = "aabb112233445566778899aabb11223344556677",
        };
        this.torrentService.GetByInfoHash("aabb112233445566778899aabb11223344556677").Returns(torrent);

        var files = new List<TorrentFile>
        {
            new() { Id = 10, Path = "file1.mkv", Size = 1000, Priority = 3 },
            new() { Id = 11, Path = "file2.mkv", Size = 2000, Priority = 3 },
        };
        this.torrentFileService.GetFiles(51).Returns(files);

        var request = new FloodSetPriorityRequest
        {
            Hashes = new List<string> { "aabb112233445566778899aabb11223344556677" },
            Indices = new List<int> { 0, 1 },
            Priority = 2, // Flood High -> should map to 4
        };

        var result = await this.controller.SetContentsPriority(request: request);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentFileService.Received(1).SetPriorityAsync(10, 4);
        await this.torrentFileService.Received(1).SetPriorityAsync(11, 4);
    }

    [Test]
    public async Task SetLocation_CallsTorrentServiceSetLocationAsync()
    {
        var torrent = new Torrent
        {
            Id = 60,
            Name = "Location Torrent",
            InfoHash = "cccc112233445566778899cccc11223344556677",
        };
        this.torrentService.GetByInfoHash("cccc112233445566778899cccc11223344556677").Returns(torrent);

        var request = new FloodSetLocationRequest
        {
            Hashes = new List<string> { "cccc112233445566778899cccc11223344556677" },
            Destination = "/new/destination/path",
            MoveFiles = true,
        };

        var result = await this.controller.SetLocation(request);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).SetLocationAsync(60, "/new/destination/path", true);
    }

    [Test]
    public async Task Reannounce_CallsTorrentServiceForceAnnounceAsync()
    {
        var torrent = new Torrent
        {
            Id = 70,
            Name = "Announce Torrent",
            InfoHash = "dddd112233445566778899dddd11223344556677",
        };
        this.torrentService.GetByInfoHash("dddd112233445566778899dddd11223344556677").Returns(torrent);

        var request = new FloodReannounceRequest
        {
            Hashes = new List<string> { "dddd112233445566778899dddd11223344556677" },
        };

        var result = await this.controller.Reannounce(request);

        result.Should().BeOfType<OkObjectResult>();
        await this.torrentService.Received(1).ForceAnnounceAsync(70);
    }
}

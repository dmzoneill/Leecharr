// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
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
}

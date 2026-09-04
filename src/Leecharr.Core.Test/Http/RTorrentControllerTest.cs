// Copyright (c) PlaceholderCompany. All rights reserved.

using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.RTorrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class RTorrentControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private ITorrentFileService torrentFileService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private RTorrentController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();

        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new RTorrentController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.torrentFileService,
            this.configFileProvider);
    }

    private void SetRequestBody(string xml)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(xml);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    [Test]
    public async Task HandleXmlRpc_DirectorySet_WithInfoHash_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
              <methodName>d.directory.set</methodName>
              <params>
                <param><value><string>aabbccddeeff00112233445566778899aabbccdd</string></value></param>
                <param><value><string>/downloads/new_dir</string></value></param>
              </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/new_dir", moveFiles: true);
    }

    [Test]
    public async Task HandleXmlRpc_DirectoryBaseSet_WithInfoHash_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.GetByInfoHash("aabbccddeeff00112233445566778899aabbccdd").Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
              <methodName>d.directory_base.set</methodName>
              <params>
                <param><value><string>aabbccddeeff00112233445566778899aabbccdd</string></value></param>
                <param><value><string>/downloads/base_dir</string></value></param>
              </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/base_dir", moveFiles: true);
    }

    [Test]
    public async Task HandleXmlRpc_DirectorySet_WithTorrentId_InvokesSetLocationAsyncWithMoveTrue()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Test.Torrent",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            SavePath = "/downloads",
        };
        this.torrentService.Get(42).Returns(torrent);

        var xml = """
            <?xml version="1.0"?>
            <methodCall>
              <methodName>d.directory.set</methodName>
              <params>
                <param><value><i4>42</i4></value></param>
                <param><value><string>/downloads/id_target</string></value></param>
              </params>
            </methodCall>
            """;
        this.SetRequestBody(xml);

        var result = await this.controller.HandleXmlRpc();

        result.Should().BeOfType<ContentResult>();
        var contentResult = (ContentResult)result;
        contentResult.Content.Should().Contain("<i4>0</i4>");
        await this.torrentService.Received(1).SetLocationAsync(42, "/downloads/id_target", moveFiles: true);
    }
}

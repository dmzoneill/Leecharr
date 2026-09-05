// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Hadouken;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class HadoukenRpcControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private IConfigFileProvider configFileProvider = null!;
    private IConfigService configService = null!;
    private HadoukenRpcController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        this.controller = new HadoukenRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.torrentFileService,
            this.configFileProvider);

        var httpContext = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [TestCase("torrents.get_files")]
    [TestCase("webui.getfiles")]
    public async Task GetFiles_EnrichesFileProgress(string method)
    {
        var infoHash = "abc123456789abcdef0123456789abcdef012345";
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = infoHash,
            Status = TorrentStatus.Downloading,
            Progress = 0.5,
            TotalSize = 1000,
        };

        var files = new List<TorrentFile>
        {
            new() { Id = 10, TorrentId = 1, Path = "file1.mkv", Size = 1000, Progress = 0.0 },
        };

        this.torrentService.GetByInfoHash(infoHash).Returns(torrent);
        this.torrentFileService.GetFiles(1).Returns(files);

        var downloadTask = Substitute.For<IDownloadTask>();
        downloadTask.PieceBitfield.Returns(new bool[] { true, true });
        this.torrentService.GetDownloadTask(1).Returns(downloadTask);

        var requestJson = $"{{\"method\":\"{method}\",\"params\":[\"{infoHash}\"],\"id\":1}}";
        using var doc = JsonDocument.Parse(requestJson);
        var request = new HadoukenRpcRequest
        {
            Method = method,
            Params = doc.RootElement.GetProperty("params"),
            Id = 1,
        };

        var actionResult = await this.controller.HandleRpc(request);
        actionResult.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)actionResult;
        okResult.Value.Should().NotBeNull();

        // Before enrichment progress was 0.0, enricher sets it to torrent.Progress (0.5)
        files[0].Progress.Should().Be(0.5);
    }
}

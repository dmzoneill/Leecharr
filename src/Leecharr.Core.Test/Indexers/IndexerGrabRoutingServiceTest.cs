// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Core.Test.Network;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class IndexerGrabRoutingServiceTest
{
    private IDownloadClientRepository downloadClientRepository = null!;
    private ITorrentService torrentService = null!;
    private ISafeHttpClientService safeHttpClientService = null!;

    [SetUp]
    public void SetUp()
    {
        this.downloadClientRepository = Substitute.For<IDownloadClientRepository>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();
        this.safeHttpClientService
            .When(x => x.ValidateUrl(Arg.Any<string>()))
            .Do(_ => { });
    }

    [Test]
    public async Task GrabAsync_WhenDownloadClientIdIsZero_UsesEmbeddedTorrentService()
    {
        var magnet = "magnet:?xt=urn:btih:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&dn=test";
        var torrent = new Torrent { Id = 3, InfoHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" };
        this.torrentService.AddFromMagnetAsync(magnet, "tv", null, false).Returns(torrent);

        var service = new IndexerGrabRoutingService(this.downloadClientRepository, this.torrentService);
        var result = await service.GrabAsync(0, new IndexerGrabRequest { MagnetUrl = magnet, Category = "tv" });

        result.Success.Should().BeTrue();
        result.UsedExternalClient.Should().BeFalse();
        result.Torrent.Should().Be(torrent);
        await this.torrentService.Received(1).AddFromMagnetAsync(magnet, "tv", null, false);
    }

    [Test]
    public async Task GrabAsync_WhenDownloadClientConfigured_SendsMagnetToRemoteClient()
    {
        var requestedPaths = new List<string>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requestedPaths.Add(req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Ok.", Encoding.UTF8, "text/plain"),
            };
        });

        using var http = new HttpClient(handler);
        var client = new DownloadClientDefinition
        {
            Id = 7,
            Name = "RemoteQBit",
            ClientType = "qBittorrent",
            Host = "127.0.0.1",
            Port = 8080,
            Enable = true,
        };
        this.downloadClientRepository.Get(7).Returns(client);

        var magnet = "magnet:?xt=urn:btih:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb&dn=remote";
        var service = new IndexerGrabRoutingService(
            this.downloadClientRepository,
            this.torrentService,
            safeHttpClientService: this.safeHttpClientService,
            httpClient: http);
        var result = await service.GrabAsync(7, new IndexerGrabRequest { MagnetUrl = magnet, Category = "movies" });

        result.Success.Should().BeTrue();
        result.UsedExternalClient.Should().BeTrue();
        result.InfoHash.Should().Be("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        await this.torrentService.DidNotReceive().AddFromMagnetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        requestedPaths.Should().Contain("/api/v2/torrents/add");
    }
}

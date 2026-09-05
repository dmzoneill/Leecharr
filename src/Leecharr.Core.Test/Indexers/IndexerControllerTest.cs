// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Indexers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class IndexerControllerTest
{
    private IIndexerRepository indexerRepository = null!;
    private ITorznabClient torznabClient = null!;
    private IProwlarrSyncService prowlarrSyncService = null!;
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private IDownloadHistoryService downloadHistoryService = null!;
    private IndexerController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.indexerRepository = Substitute.For<IIndexerRepository>();
        this.torznabClient = Substitute.For<ITorznabClient>();
        this.prowlarrSyncService = Substitute.For<IProwlarrSyncService>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();
        this.downloadHistoryService = Substitute.For<IDownloadHistoryService>();

        this.controller = new IndexerController(
            this.indexerRepository,
            this.torznabClient,
            this.prowlarrSyncService,
            this.torrentService,
            this.torrentFileParser,
            this.safeHttpClientService,
            downloadHistoryService: this.downloadHistoryService);
    }

    [Test]
    public async Task DownloadRelease_WithMagnetUrlAndIndexerAttribution_RecordsAttribution()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Ubuntu 24.04 ISO",
            MagnetUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu",
            IndexerId = 1,
            IndexerName = "Prowlarr (TrackerAlpha)",
            Category = "linux",
        };

        var createdTorrent = new Torrent
        {
            Id = 10,
            Name = request.Title,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Category = request.Category,
        };

        this.torrentService.AddFromMagnetAsync(request.MagnetUrl, request.Category, null, false)
            .Returns(Task.FromResult(createdTorrent));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.downloadHistoryService.Received(1).RecordTorrentAdded(
            createdTorrent,
            source: "Prowlarr (TrackerAlpha)",
            magnetUrl: request.MagnetUrl,
            downloadUrl: null,
            indexerName: "Prowlarr (TrackerAlpha)");
    }

    [Test]
    public async Task DownloadRelease_WithIndexerIdOnly_FetchesIndexerNameFromRepository()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Debian 12 ISO",
            MagnetUrl = "magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01&dn=Debian",
            IndexerId = 42,
            IndexerName = null,
        };

        var createdTorrent = new Torrent
        {
            Id = 11,
            Name = request.Title,
            InfoHash = "abcdef0123456789abcdef0123456789abcdef01",
        };

        this.indexerRepository.Get(42).Returns(new IndexerDefinition
        {
            Id = 42,
            Name = "ResolvedIndexer",
        });

        this.torrentService.AddFromMagnetAsync(request.MagnetUrl, null, null, false)
            .Returns(Task.FromResult(createdTorrent));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.downloadHistoryService.Received(1).RecordTorrentAdded(
            createdTorrent,
            source: "ResolvedIndexer",
            magnetUrl: request.MagnetUrl,
            downloadUrl: null,
            indexerName: "ResolvedIndexer");
    }

    [Test]
    public async Task DownloadRelease_WithDownloadUrl_DownloadsBytesAndRecordsDownloadHistory()
    {
        var torrentBytes = new byte[] { 0x64, 0x31, 0x30, 0x65 };
        var parsed = new ParsedTorrent { Name = "Test Torrent", InfoHash = "fedcba9876543210" };
        var request = new DownloadReleaseRequest
        {
            Title = "Test Torrent",
            DownloadUrl = "https://tracker.example.com/download/test.torrent",
            IndexerName = "TorrentTracker",
        };

        var createdTorrent = new Torrent
        {
            Id = 12,
            Name = request.Title,
            InfoHash = parsed.InfoHash,
        };

        this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl)
            .Returns(Task.FromResult(torrentBytes));
        this.torrentFileParser.Parse(torrentBytes)
            .Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, torrentBytes)
            .Returns(Task.FromResult(createdTorrent));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.downloadHistoryService.Received(1).RecordTorrentAdded(
            createdTorrent,
            source: "TorrentTracker",
            magnetUrl: null,
            downloadUrl: request.DownloadUrl,
            indexerName: "TorrentTracker");
    }

    [Test]
    public async Task DownloadRelease_WithDownloadUrlAsMagnet_AddsFromMagnetAndRecordsHistory()
    {
        var magnet = "magnet:?xt=urn:btih:fedcba9876543210fedcba9876543210fedcba98";
        var request = new DownloadReleaseRequest
        {
            Title = "Magnet via DownloadUrl",
            DownloadUrl = magnet,
            IndexerName = "MagnetTracker",
        };

        var createdTorrent = new Torrent
        {
            Id = 13,
            Name = request.Title,
            InfoHash = "fedcba9876543210fedcba9876543210fedcba98",
        };

        this.torrentService.AddFromMagnetAsync(magnet, null, null, false)
            .Returns(Task.FromResult(createdTorrent));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.downloadHistoryService.Received(1).RecordTorrentAdded(
            createdTorrent,
            source: "MagnetTracker",
            magnetUrl: null,
            downloadUrl: magnet,
            indexerName: "MagnetTracker");
    }

    [Test]
    public async Task DownloadRelease_WhenNoIndexerAttributionProvided_DefaultsSourceToIndexer()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Unknown Indexer Release",
            MagnetUrl = "magnet:?xt=urn:btih:1111111111111111111111111111111111111111",
            IndexerName = null,
            IndexerId = null,
        };

        var createdTorrent = new Torrent
        {
            Id = 14,
            Name = request.Title,
            InfoHash = "1111111111111111111111111111111111111111",
        };

        this.torrentService.AddFromMagnetAsync(request.MagnetUrl, null, null, false)
            .Returns(Task.FromResult(createdTorrent));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.downloadHistoryService.Received(1).RecordTorrentAdded(
            createdTorrent,
            source: "Indexer",
            magnetUrl: request.MagnetUrl,
            downloadUrl: null,
            indexerName: null);
    }

    [Test]
    public async Task DownloadRelease_WhenTorrentServiceFails_ReturnsBadRequestAndDoesNotRecordHistory()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Failed Release",
            MagnetUrl = "magnet:?xt=urn:btih:2222222222222222222222222222222222222222",
        };

        this.torrentService.AddFromMagnetAsync(request.MagnetUrl, null, null, false)
            .Returns(Task.FromResult<Torrent>(null!));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        this.downloadHistoryService.DidNotReceiveWithAnyArgs().RecordTorrentAdded(null!, null!, null!, null!, null!);
    }

    [Test]
    public async Task DownloadRelease_WhenRequestIsNull_ReturnsBadRequest()
    {
        var result = await this.controller.DownloadRelease(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task Test_WhenTorznabTestFails_ReturnsFailureMessage()
    {
        var resource = new IndexerResource
        {
            Name = "DeadIndexer",
            Url = "https://dead.indexer.local",
            Implementation = "Torznab",
        };

        this.torznabClient.TestConnectionAsync(Arg.Any<IndexerDefinition>())
            .Returns(Task.FromResult(TorznabTestResult.Fail("HTTP 403 Forbidden")));

        var result = await this.controller.TestDirect(resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var testResult = (IndexerTestResult)okResult.Value!;
        testResult.Success.Should().BeFalse();
        testResult.Message.Should().Contain("HTTP 403 Forbidden");
    }

    [Test]
    public async Task Test_WhenTorznabTestSucceeds_ReturnsSuccessMessageAndUpdatesCategories()
    {
        var resource = new IndexerResource
        {
            Id = 5,
            Name = "LiveIndexer",
            Url = "https://live.indexer.local",
            Implementation = "Torznab",
        };

        var existingIndexer = new IndexerDefinition { Id = 5, Name = "LiveIndexer", Url = "https://live.indexer.local" };
        this.indexerRepository.Get(5).Returns(existingIndexer);

        var caps = new TorznabCapabilities
        {
            Categories = new System.Collections.Generic.List<TorznabCategory>
            {
                new() { Id = 2000, Name = "Movies" },
                new() { Id = 5000, Name = "TV" },
            },
        };

        this.torznabClient.TestConnectionAsync(Arg.Any<IndexerDefinition>())
            .Returns(Task.FromResult(TorznabTestResult.Ok(caps)));

        var result = await this.controller.TestDirect(resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var testResult = (IndexerTestResult)okResult.Value!;
        testResult.Success.Should().BeTrue();
        testResult.Message.Should().Contain("Connected successfully to LiveIndexer");
        this.indexerRepository.Received(1).Update(Arg.Is<IndexerDefinition>(idx => idx.Categories.Contains(2000) && idx.Categories.Contains(5000)));
    }
}

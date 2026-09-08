// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
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
    public async Task DownloadRelease_WhenDownloadUrlFailsAndInfoHashProvided_FallsBackToMagnetUrl()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Ubuntu 24.04 ISO",
            DownloadUrl = "https://tracker.example.com/dead.torrent",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Category = "linux",
        };

        var createdTorrent = new Torrent
        {
            Id = 15,
            Name = request.Title,
            InfoHash = request.InfoHash,
            Category = request.Category,
        };

        this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl)
            .Returns(Task.FromException<byte[]>(new HttpRequestException("404 Not Found")));

        var expectedFallbackMagnet = $"magnet:?xt=urn:btih:{request.InfoHash}&dn={Uri.EscapeDataString(request.Title)}";
        this.torrentService.AddFromMagnetAsync(expectedFallbackMagnet, request.Category, null, false)
            .Returns(Task.FromResult(createdTorrent));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.downloadHistoryService.Received(1).RecordTorrentAdded(
            createdTorrent,
            source: "Indexer",
            magnetUrl: null,
            downloadUrl: request.DownloadUrl,
            indexerName: null);
    }

    [Test]
    public async Task DownloadRelease_WithOnlyInfoHash_AddsFromFallbackMagnet()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Debian ISO",
            InfoHash = "abcdef0123456789abcdef0123456789abcdef01",
            Category = "linux",
        };

        var createdTorrent = new Torrent
        {
            Id = 16,
            Name = request.Title,
            InfoHash = request.InfoHash,
            Category = request.Category,
        };

        var expectedFallbackMagnet = $"magnet:?xt=urn:btih:{request.InfoHash}&dn={Uri.EscapeDataString(request.Title)}";
        this.torrentService.AddFromMagnetAsync(expectedFallbackMagnet, request.Category, null, false)
            .Returns(Task.FromResult(createdTorrent));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task DownloadRelease_WhenDownloadUrlFailsAndNoInfoHash_ReturnsBadRequest()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Dead Release",
            DownloadUrl = "https://tracker.example.com/dead.torrent",
        };

        this.safeHttpClientService.DownloadBytesAsync(request.DownloadUrl)
            .Returns(Task.FromException<byte[]>(new HttpRequestException("500 Internal Error")));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
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

    [Test]
    public async Task SyncProwlarr_WithValidCredentials_ReturnsOkWithCount()
    {
        var request = new ProwlarrSyncRequest { Url = "http://localhost:9696", ApiKey = "valid-key" };
        this.prowlarrSyncService.SyncFromProwlarrAsync(request.Url, request.ApiKey).Returns(Task.FromResult(5));

        var result = await this.controller.SyncProwlarr(request);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task SyncProwlarr_WhenNoCredentialsAndNotConfigured_ReturnsBadRequest()
    {
        this.prowlarrSyncService.IsConfigured().Returns(false);

        var result = await this.controller.SyncProwlarr(new ProwlarrSyncRequest());

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task SyncProwlarr_WhenNoCredentialsAndConfigured_CallsSyncAll()
    {
        this.prowlarrSyncService.IsConfigured().Returns(true);
        this.prowlarrSyncService.SyncAllAsync().Returns(Task.FromResult(3));

        var result = await this.controller.SyncProwlarr(null);

        result.Result.Should().BeOfType<OkObjectResult>();
        await this.prowlarrSyncService.Received(1).SyncAllAsync();
    }

    [Test]
    public async Task SyncProwlarr_WhenProwlarrReturns401_ReturnsBadRequest()
    {
        var request = new ProwlarrSyncRequest { Url = "http://localhost:9696", ApiKey = "bad-key" };
        this.prowlarrSyncService.SyncFromProwlarrAsync(request.Url, request.ApiKey)
            .Returns(Task.FromException<int>(new HttpRequestException("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized)));

        var result = await this.controller.SyncProwlarr(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task SyncProwlarr_WhenProwlarrConnectivityFails_ReturnsBadGateway502()
    {
        var request = new ProwlarrSyncRequest { Url = "http://localhost:9696", ApiKey = "key" };
        this.prowlarrSyncService.SyncFromProwlarrAsync(request.Url, request.ApiKey)
            .Returns(Task.FromException<int>(new HttpRequestException("Connection refused")));

        var result = await this.controller.SyncProwlarr(request);

        result.Result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result.Result!;
        objResult.StatusCode.Should().Be(502);
    }

    [Test]
    public void GetAll_MapsCapabilitiesFromSettingsToResource()
    {
        var settingsJson = System.Text.Json.JsonSerializer.Serialize(new IndexerSettings
        {
            SupportsTvSearch = true,
            SupportsMovieSearch = true,
            SupportsMusicSearch = false,
            SupportsBookSearch = true,
            SupportedTvParams = new List<string> { "q", "season", "ep", "tvdbid" },
            SupportedMovieParams = new List<string> { "q", "imdbid" },
            DefaultPageSize = 35,
            MaxPageSize = 120,
        });

        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "TrackerWithCaps",
            Settings = settingsJson,
            Enable = true,
        };

        this.indexerRepository.All().Returns(new List<IndexerDefinition> { indexer });

        var result = this.controller.GetAll();
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var list = (List<IndexerResource>)okResult.Value!;

        list.Should().HaveCount(1);
        var res = list[0];
        res.SupportsTvSearch.Should().BeTrue();
        res.SupportsMovieSearch.Should().BeTrue();
        res.SupportsBookSearch.Should().BeTrue();
        res.SupportedTvParams.Should().Contain(new[] { "q", "season", "ep", "tvdbid" });
        res.DefaultPageSize.Should().Be(35);
        res.MaxPageSize.Should().Be(120);
    }

    [Test]
    public async Task SearchGet_WithNewznabParams_PassesParametersToTorznabClient()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Alpha", Enable = true, EnableSearch = true, Url = "http://alpha" };
        this.indexerRepository.Get(1).Returns(indexer);

        this.torznabClient.SearchAsync(
            indexer,
            "Mr Robot",
            categoryId: null,
            limit: 50,
            offset: 0,
            season: 1,
            ep: 1,
            imdbId: "tt4158110",
            tmdbId: "62560",
            searchType: null,
            tvdbId: "289590",
            rid: "4050",
            year: 2015,
            artist: null,
            album: null,
            author: null,
            isbn: null,
            cancellationToken: Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Mr.Robot.S01E01.1080p", Seeders = 100, DownloadUrl = "http://dl" },
            }));

        var actionResult = await this.controller.SearchGet(
            query: "Mr Robot",
            indexerId: 1,
            season: 1,
            ep: 1,
            imdbId: "tt4158110",
            tmdbId: "62560",
            tvdbId: "289590",
            rid: "4050",
            year: 2015);

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var results = (List<ReleaseInfoResource>)okResult.Value!;
        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Mr.Robot.S01E01.1080p");
    }

    [Test]
    public async Task SearchPost_WithNewznabParams_PassesParametersToTorznabClient()
    {
        var indexer = new IndexerDefinition { Id = 2, Name = "Beta", Enable = true, EnableSearch = true, Url = "http://beta" };
        this.indexerRepository.Get(2).Returns(indexer);

        this.torznabClient.SearchAsync(
            indexer,
            "Dune",
            categoryId: null,
            limit: 50,
            offset: 0,
            season: null,
            ep: null,
            imdbId: null,
            tmdbId: null,
            searchType: "book",
            tvdbId: null,
            rid: null,
            year: 1965,
            artist: null,
            album: null,
            author: "Frank Herbert",
            isbn: "9780441172719",
            cancellationToken: Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Dune - Frank Herbert (1965)", Seeders = 25, DownloadUrl = "http://dl-book" },
            }));

        var request = new IndexerSearchRequest
        {
            Query = "Dune",
            IndexerId = 2,
            Type = "book",
            Author = "Frank Herbert",
            Isbn = "9780441172719",
            Year = 1965,
        };

        var actionResult = await this.controller.SearchPost(request);

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var results = (List<ReleaseInfoResource>)okResult.Value!;
        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Dune - Frank Herbert (1965)");
    }

    [Test]
    public async Task ExecuteSearch_MultiIndexer_FastFailsOnOneIndexerWhileReturningResultsFromOthers()
    {
        var indexer1 = new IndexerDefinition { Id = 1, Name = "FastTracker", Enable = true, EnableSearch = true, Url = "http://fast" };
        var indexer2 = new IndexerDefinition { Id = 2, Name = "FailingTracker", Enable = true, EnableSearch = true, Url = "http://failing" };
        this.indexerRepository.GetSearchEnabled().Returns(new List<IndexerDefinition> { indexer1, indexer2 });

        this.torznabClient.SearchAsync(
            indexer1,
            "test",
            Arg.Any<int?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Fast Result", Seeders = 50, DownloadUrl = "http://dl-fast" },
            }));

        this.torznabClient.SearchAsync(
            indexer2,
            "test",
            Arg.Any<int?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromException<List<TorznabSearchResult>>(new HttpRequestException("Indexer connection timeout")));

        var actionResult = await this.controller.SearchGet(query: "test");

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var results = (List<ReleaseInfoResource>)okResult.Value!;

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Fast Result");
    }
}

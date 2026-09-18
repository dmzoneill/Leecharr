// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
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
            Arg.Is<TorznabSearchCriteria>(c =>
                c.Query == "Mr Robot" &&
                c.Season == 1 &&
                c.Ep == 1 &&
                c.ImdbId == "tt4158110" &&
                c.TmdbId == "62560" &&
                c.TvdbId == "289590" &&
                c.Rid == "4050" &&
                c.Year == 2015),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Mr.Robot.S01E01.1080p", Seeders = 100, DownloadUrl = "http://dl" },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest
        {
            Query = "Mr Robot",
            IndexerId = 1,
            Season = 1,
            Ep = 1,
            ImdbId = "tt4158110",
            TmdbId = "62560",
            TvdbId = "289590",
            Rid = "4050",
            Year = 2015,
        });

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
            Arg.Is<TorznabSearchCriteria>(c =>
                c.Query == "Dune" &&
                c.SearchType == "book" &&
                c.Author == "Frank Herbert" &&
                c.Isbn == "9780441172719" &&
                c.Year == 1965),
            Arg.Any<System.Threading.CancellationToken>())
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
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "test"),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Fast Result", Seeders = 50, DownloadUrl = "http://dl-fast" },
            }));

        this.torznabClient.SearchAsync(
            indexer2,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "test"),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromException<List<TorznabSearchResult>>(new HttpRequestException("Indexer connection timeout")));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "test" });

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var results = (List<ReleaseInfoResource>)okResult.Value!;

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Fast Result");
    }

    [TestCase("movies", 2000)]
    [TestCase("tv", 5000)]
    [TestCase("music", 3000)]
    [TestCase("audio", 3000)]
    [TestCase("anime", 5070)]
    [TestCase("books", 7000)]
    [TestCase("other", 8000)]
    [TestCase("5040", 5040)]
    public async Task SearchGet_WithStringCategoriesAndNumericCategories_MapsCorrectCategoryId(string categoryInput, int expectedCategoryId)
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Alpha", Enable = true, EnableSearch = true, Url = "http://alpha" };
        this.indexerRepository.Get(1).Returns(indexer);

        this.torznabClient.SearchAsync(
            indexer,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "test query" && c.CategoryId == expectedCategoryId),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Result 1", Seeders = 10, DownloadUrl = "http://dl" },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "test query", IndexerId = 1, Category = categoryInput });

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        await this.torznabClient.Received(1).SearchAsync(
            indexer,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "test query" && c.CategoryId == expectedCategoryId),
            Arg.Any<System.Threading.CancellationToken>());
    }

    [Test]
    public async Task SearchGet_MultiIndexerPagination_QueriesIndexersFromOffsetZeroAndPaginatesGlobally()
    {
        var indexer1 = new IndexerDefinition { Id = 1, Name = "Tracker1", Enable = true, EnableSearch = true, Url = "http://t1" };
        var indexer2 = new IndexerDefinition { Id = 2, Name = "Tracker2", Enable = true, EnableSearch = true, Url = "http://t2" };
        this.indexerRepository.GetSearchEnabled().Returns(new List<IndexerDefinition> { indexer1, indexer2 });

        this.torznabClient.SearchAsync(
            indexer1,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "popular movie" && c.Limit == 3 && c.Offset == 0),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Movie T1 High", Seeders = 100, DownloadUrl = "http://dl-t1-1" },
                new() { Title = "Movie T1 Mid", Seeders = 70, DownloadUrl = "http://dl-t1-2" },
                new() { Title = "Movie T1 Low", Seeders = 40, DownloadUrl = "http://dl-t1-3" },
            }));

        this.torznabClient.SearchAsync(
            indexer2,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "popular movie" && c.Limit == 3 && c.Offset == 0),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Movie T2 High", Seeders = 90, DownloadUrl = "http://dl-t2-1" },
                new() { Title = "Movie T2 Mid", Seeders = 60, DownloadUrl = "http://dl-t2-2" },
                new() { Title = "Movie T2 Low", Seeders = 30, DownloadUrl = "http://dl-t2-3" },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "popular movie", Offset = 1, Limit = 2 });

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var results = (List<ReleaseInfoResource>)okResult.Value!;

        results.Should().HaveCount(2);
        results[0].Title.Should().Be("Movie T2 High");
        results[0].Seeders.Should().Be(90);
        results[1].Title.Should().Be("Movie T1 Mid");
        results[1].Seeders.Should().Be(70);
    }

    [Test]
    public async Task SearchGet_WhenLimitExceedsMax_ClampsTo250()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Alpha", Enable = true, EnableSearch = true, Url = "http://alpha" };
        this.indexerRepository.Get(1).Returns(indexer);

        this.torznabClient.SearchAsync(
            indexer,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "test" && c.Limit == 250),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Result 1", Seeders = 10, DownloadUrl = "http://dl" },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "test", IndexerId = 1, Limit = 1000 });

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        await this.torznabClient.Received(1).SearchAsync(
            indexer,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "test" && c.Limit == 250),
            Arg.Any<System.Threading.CancellationToken>());
    }

    [Test]
    public async Task SearchGet_WhenLimitIsZeroOrNegative_DefaultsTo50()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Alpha", Enable = true, EnableSearch = true, Url = "http://alpha" };
        this.indexerRepository.Get(1).Returns(indexer);

        this.torznabClient.SearchAsync(
            indexer,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "test" && c.Limit == 50),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Result 1", Seeders = 10, DownloadUrl = "http://dl" },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "test", IndexerId = 1, Limit = -5 });

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        await this.torznabClient.Received(1).SearchAsync(
            indexer,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "test" && c.Limit == 50),
            Arg.Any<System.Threading.CancellationToken>());
    }

    [Test]
    public async Task SearchGet_MultiIndexerSearchWithLargeLimit_CapsFetchLimitPerIndexerTo100()
    {
        var indexer1 = new IndexerDefinition { Id = 1, Name = "Tracker1", Enable = true, EnableSearch = true, Url = "http://t1" };
        var indexer2 = new IndexerDefinition { Id = 2, Name = "Tracker2", Enable = true, EnableSearch = true, Url = "http://t2" };
        this.indexerRepository.GetSearchEnabled().Returns(new List<IndexerDefinition> { indexer1, indexer2 });

        this.torznabClient.SearchAsync(
            indexer1,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "popular release" && c.Limit == 100),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Popular Release A", Seeders = 50, DownloadUrl = "http://dl-a" },
            }));

        this.torznabClient.SearchAsync(
            indexer2,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "popular release" && c.Limit == 100),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Popular Release B", Seeders = 100, DownloadUrl = "http://dl-b" },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "popular release", Limit = 250 });

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var results = (List<ReleaseInfoResource>)okResult.Value!;

        results.Should().HaveCount(2);
        results[0].Title.Should().Be("Popular Release B");
        results[1].Title.Should().Be("Popular Release A");

        await this.torznabClient.Received(1).SearchAsync(
            indexer1,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "popular release" && c.Limit == 100),
            Arg.Any<System.Threading.CancellationToken>());

        await this.torznabClient.Received(1).SearchAsync(
            indexer2,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "popular release" && c.Limit == 100),
            Arg.Any<System.Threading.CancellationToken>());
    }

    [Test]
    public async Task SearchGet_MultiIndexerDeepPaginationAtOffset500_QueriesIndexersAtOffset500AndPreservesResults()
    {
        var indexer1 = new IndexerDefinition { Id = 1, Name = "Tracker1", Enable = true, EnableSearch = true, Url = "http://t1" };
        var indexer2 = new IndexerDefinition { Id = 2, Name = "Tracker2", Enable = true, EnableSearch = true, Url = "http://t2" };
        this.indexerRepository.GetSearchEnabled().Returns(new List<IndexerDefinition> { indexer1, indexer2 });

        this.torznabClient.SearchAsync(
            indexer1,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "deep search" && c.Limit == 50 && c.Offset == 500),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Deep Release Alpha", Seeders = 10, DownloadUrl = "http://dl-alpha" },
            }));

        this.torznabClient.SearchAsync(
            indexer2,
            Arg.Is<TorznabSearchCriteria>(c => c.Query == "deep search" && c.Limit == 50 && c.Offset == 500),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Deep Release Beta", Seeders = 20, DownloadUrl = "http://dl-beta" },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "deep search", Offset = 500, Limit = 50 });

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var results = (List<ReleaseInfoResource>)okResult.Value!;

        results.Should().HaveCount(2);
        results[0].Title.Should().Be("Deep Release Beta");
        results[1].Title.Should().Be("Deep Release Alpha");
    }

    [Test]
    public async Task TestAll_WhenCalled_RunsTestForEachIndexerAndReturnsBatchResults()
    {
        var indexer1 = new IndexerDefinition { Id = 1, Name = "Alpha", Url = "http://alpha", Implementation = "Torznab" };
        var indexer2 = new IndexerDefinition { Id = 2, Name = "Beta", Url = "http://beta", Implementation = "Torznab" };
        this.indexerRepository.All().Returns(new List<IndexerDefinition> { indexer1, indexer2 });

        this.torznabClient.TestConnectionAsync(indexer1, Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(TorznabTestResult.Ok()));
        this.torznabClient.TestConnectionAsync(indexer2, Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(TorznabTestResult.Fail("HTTP 500")));

        var actionResult = await this.controller.TestAll();

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var batchResults = (List<IndexerBatchTestResult>)okResult.Value!;

        batchResults.Should().HaveCount(2);
        batchResults.First(r => r.Id == 1).Success.Should().BeTrue();
        batchResults.First(r => r.Id == 2).Success.Should().BeFalse();
    }

    [Test]
    public async Task SearchGet_ExtractsResponseTotalAndPopulatesEnvelope()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Tracker1", Enable = true, EnableSearch = true, Url = "http://t1" };
        this.indexerRepository.Get(1).Returns(indexer);

        this.torznabClient.SearchAsync(
            indexer,
            Arg.Any<TorznabSearchCriteria>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "Release 1", Seeders = 10, DownloadUrl = "http://dl-1", ResponseTotal = 500, ResponseOffset = 0 },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "test", IndexerId = 1, Limit = 50 });

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var envelope = (IndexerSearchEnvelope)okResult.Value!;

        envelope.Total.Should().Be(500);
        envelope.Page.Should().Be(1);
        envelope.Limit.Should().Be(50);
        envelope.Results.Should().HaveCount(1);
    }

    [Test]
    public async Task DownloadRelease_WithCookiesAndUserAgent_PassesCustomHeadersToSafeHttpClientService()
    {
        var torrentBytes = new byte[] { 0x64, 0x31, 0x30, 0x65 };
        var parsed = new ParsedTorrent { Name = "Private Torrent", InfoHash = "0123456789abcdef0123456789abcdef01234567" };
        var request = new DownloadReleaseRequest
        {
            Title = "Private Torrent",
            DownloadUrl = "https://private.tracker.local/download/123.torrent",
            IndexerId = 5,
            Cookie = "uid=123; pass=secret",
            UserAgent = "MyCustomAgent/1.0",
        };

        var indexer = new IndexerDefinition
        {
            Id = 5,
            Name = "PrivateTracker",
            ApiKey = "api-token-xyz",
        };
        this.indexerRepository.Get(5).Returns(indexer);

        var createdTorrent = new Torrent
        {
            Id = 20,
            Name = request.Title,
            InfoHash = parsed.InfoHash,
        };

        this.safeHttpClientService.DownloadBytesAsync(
            request.DownloadUrl,
            Arg.Is<IDictionary<string, string>>(h =>
                h.ContainsKey("Cookie") && h["Cookie"] == "uid=123; pass=secret" &&
                h.ContainsKey("User-Agent") && h["User-Agent"] == "MyCustomAgent/1.0" &&
                h.ContainsKey("X-Api-Key") && h["X-Api-Key"] == "api-token-xyz"))
            .Returns(Task.FromResult(torrentBytes));

        this.torrentFileParser.Parse(torrentBytes)
            .Returns(parsed);
        this.torrentService.AddFromParsedTorrentAsync(parsed, null, null, false, torrentBytes)
            .Returns(Task.FromResult(createdTorrent));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task DownloadRelease_WhenDownloadUrlEndsWithNzb_ReturnsBadRequest()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Usenet Release",
            DownloadUrl = "http://indexer.local/api?t=get&id=123.nzb",
        };

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.Value.Should().Be("Usenet/NZB releases are not supported. Leecharr is a BitTorrent engine.");
    }

    [Test]
    public async Task DownloadRelease_WhenPayloadIsXmlNzb_ReturnsBadRequest()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Usenet XML Release",
            DownloadUrl = "http://indexer.local/download/123",
        };

        var xmlBytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<nzb xmlns=\"...\">");
        this.safeHttpClientService.DownloadBytesAsync("http://indexer.local/download/123")
            .Returns(Task.FromResult(xmlBytes));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.Value!.ToString().Should().Contain("XML/NZB");
    }

    [Test]
    public async Task SearchGet_WhenTorznabClientThrowsTorznabException_RecordsFailureInIndexerStatusService()
    {
        var mockStatusService = Substitute.For<IIndexerStatusService>();
        var customController = new IndexerController(
            this.indexerRepository,
            this.torznabClient,
            this.prowlarrSyncService,
            this.torrentService,
            this.torrentFileParser,
            this.safeHttpClientService,
            downloadHistoryService: this.downloadHistoryService,
            indexerStatusService: mockStatusService);

        var indexer = new IndexerDefinition { Id = 42, Name = "ErrTracker", Enable = true, EnableSearch = true, Url = "http://err" };
        this.indexerRepository.Get(42).Returns(indexer);

        this.torznabClient.SearchAsync(
            indexer,
            Arg.Any<TorznabSearchCriteria>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromException<List<TorznabSearchResult>>(new TorznabException(100, "Invalid API Key")));

        var actionResult = await customController.SearchGet(new IndexerSearchRequest { Query = "test", IndexerId = 42 });

        mockStatusService.Received(1).RecordFailure(42, 100, Arg.Is<string>(msg => msg.Contains("Invalid API Key")), Arg.Any<Exception>());
        mockStatusService.DidNotReceive().RecordSuccess(42);
    }

    [Test]
    public async Task TestDirect_WhenUrlValidationFails_ReturnsFailureMessageAndDoesNotSendRequest()
    {
        var resource = new IndexerResource
        {
            Name = "MetadataIndexer",
            Url = "http://169.254.169.254/latest/meta-data",
        };

        this.safeHttpClientService.When(x => x.ValidateUrl(resource.Url))
            .Do(x => throw new System.Security.SecurityException("SSRF blocked: IP address prohibited."));

        var result = await this.controller.TestDirect(resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var testResult = (IndexerTestResult)okResult.Value!;
        testResult.Success.Should().BeFalse();
        testResult.Message.Should().Contain("URL validation failed");
        testResult.Message.Should().Contain("SSRF blocked");
        this.safeHttpClientService.Received(1).ValidateUrl(resource.Url);
        await this.torznabClient.DidNotReceiveWithAnyArgs().TestConnectionAsync(default!);
    }

    [TestCase("ftp://example.com/api")]
    [TestCase("javascript:alert(1)")]
    [TestCase("file:///etc/passwd")]
    [TestCase("invalid-uri")]
    public void Create_WhenUrlInvalidOrNonHttp_ReturnsBadRequest(string url)
    {
        var resource = new IndexerResource
        {
            Name = "BadUrlIndexer",
            Url = url,
        };

        var result = this.controller.Create(resource);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Indexer URL must be a valid absolute HTTP or HTTPS URL.");
        this.indexerRepository.DidNotReceive().Insert(Arg.Any<IndexerDefinition>());
    }

    [TestCase("http://example.com/torznab")]
    [TestCase("https://indexer.local/api")]
    public void Create_WhenUrlValidHttpOrHttps_Succeeds(string url)
    {
        var resource = new IndexerResource
        {
            Name = "GoodUrlIndexer",
            Url = url,
        };

        this.indexerRepository.Insert(Arg.Any<IndexerDefinition>()).Returns(ci => ci.Arg<IndexerDefinition>());

        var result = this.controller.Create(resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.indexerRepository.Received(1).Insert(Arg.Is<IndexerDefinition>(idx => idx.Url == url));
    }

    [TestCase("ftp://example.com/api")]
    [TestCase("file:///etc/passwd")]
    [TestCase("not-a-url")]
    public void Update_WhenUrlInvalidOrNonHttp_ReturnsBadRequest(string url)
    {
        var resource = new IndexerResource
        {
            Name = "UpdateIndexer",
            Url = url,
        };

        var result = this.controller.Update(1, resource);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Indexer URL must be a valid absolute HTTP or HTTPS URL.");
        this.indexerRepository.DidNotReceive().Update(Arg.Any<IndexerDefinition>());
    }

    [TestCase("http://example.com/torznab")]
    [TestCase("https://indexer.local/api")]
    public void Update_WhenUrlValidHttpOrHttps_Succeeds(string url)
    {
        var resource = new IndexerResource
        {
            Name = "UpdateIndexer",
            Url = url,
        };

        this.indexerRepository.Get(1).Returns(new IndexerDefinition { Id = 1, Name = "Old", Url = "http://old" });

        var result = this.controller.Update(1, resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.indexerRepository.Received(1).Update(Arg.Is<IndexerDefinition>(idx => idx.Id == 1 && idx.Url == url));
    }

    [Test]
    public async Task DownloadRelease_WithMinimumRatioAndMinimumSeedTime_UpdatesTorrentTargetsAndPersists()
    {
        var request = new DownloadReleaseRequest
        {
            Title = "Arch Linux ISO",
            MagnetUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Arch",
            MinimumRatio = 1.5,
            MinimumSeedTime = 7200, // 7200 seconds = 120 minutes
        };

        var createdTorrent = new Torrent
        {
            Id = 12,
            Name = request.Title,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            TargetRatio = 1.0,
            TargetSeedTimeMinutes = 60,
        };

        this.torrentService.AddFromMagnetAsync(request.MagnetUrl, null, null, false)
            .Returns(Task.FromResult(createdTorrent));

        var result = await this.controller.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        createdTorrent.TargetRatio.Should().Be(1.5);
        createdTorrent.TargetSeedTimeMinutes.Should().Be(120);
        await this.torrentService.Received(1).UpdateAsync(createdTorrent);
    }

    [Test]
    public async Task DownloadRelease_WithTorrentRepository_UpdatesRepositoryDirectly()
    {
        var torrentRepo = Substitute.For<ITorrentRepository>();
        var ctrl = new IndexerController(
            this.indexerRepository,
            this.torznabClient,
            this.prowlarrSyncService,
            this.torrentService,
            this.torrentFileParser,
            this.safeHttpClientService,
            downloadHistoryService: this.downloadHistoryService,
            torrentRepository: torrentRepo);

        var request = new DownloadReleaseRequest
        {
            Title = "Arch Linux ISO",
            MagnetUrl = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Arch",
            MinimumRatio = 2.0,
            MinimumSeedTime = 3600, // 3600 seconds = 60 minutes
        };

        var createdTorrent = new Torrent
        {
            Id = 13,
            Name = request.Title,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            TargetRatio = 0,
            TargetSeedTimeMinutes = 0,
        };

        this.torrentService.AddFromMagnetAsync(request.MagnetUrl, null, null, false)
            .Returns(Task.FromResult(createdTorrent));

        var result = await ctrl.DownloadRelease(request);

        result.Result.Should().BeOfType<OkObjectResult>();
        createdTorrent.TargetRatio.Should().Be(2.0);
        createdTorrent.TargetSeedTimeMinutes.Should().Be(60);
        torrentRepo.Received(1).Update(createdTorrent);
    }

    [Test]
    public async Task SearchGet_MultiIndexer_DeduplicatesByIdenticalInfoHash_AndMergesMetrics()
    {
        var idx1 = new IndexerDefinition { Id = 1, Name = "Tracker1", Enable = true, EnableSearch = true, Url = "http://t1" };
        var idx2 = new IndexerDefinition { Id = 2, Name = "Tracker2", Enable = true, EnableSearch = true, Url = "http://t2" };
        this.indexerRepository.GetSearchEnabled().Returns(new List<IndexerDefinition> { idx1, idx2 });

        this.torznabClient.SearchAsync(
            idx1,
            Arg.Any<TorznabSearchCriteria>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new()
                {
                    Title = "Linux.Distro.2024.1080p",
                    Size = 5000000,
                    Seeders = 10,
                    Leechers = 2,
                    DownloadVolumeFactor = 1.0,
                    DownloadUrl = "http://t1/download/1",
                    MagnetUrl = null,
                    Comments = "https://tracker1.org/comments/1",
                    InfoHash = "482e9495c37890123456789abcdef0123456789a",
                    PublishDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                },
            }));

        this.torznabClient.SearchAsync(
            idx2,
            Arg.Any<TorznabSearchCriteria>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new()
                {
                    Title = "Linux Distro 2024",
                    Size = 5000000,
                    Seeders = 35,
                    Leechers = 12,
                    DownloadVolumeFactor = 0.0,
                    DownloadUrl = null,
                    MagnetUrl = "magnet:?xt=urn:btih:482e9495c37890123456789abcdef0123456789a&dn=Linux",
                    Comments = null,
                    InfoHash = null,
                    PublishDate = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "linux" });
        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var envelope = (IndexerSearchEnvelope)okResult.Value!;

        envelope.Results.Should().HaveCount(1);
        var merged = envelope.Results[0];
        merged.Seeders.Should().Be(35);
        merged.Leechers.Should().Be(12);
        merged.DownloadVolumeFactor.Should().Be(0.0);
        merged.IsFreeleech.Should().BeTrue();
        merged.DownloadUrl.Should().Be("http://t1/download/1");
        merged.MagnetUrl.Should().Be("magnet:?xt=urn:btih:482e9495c37890123456789abcdef0123456789a&dn=Linux");
        merged.Comments.Should().Be("https://tracker1.org/comments/1");
        merged.InfoHash.Should().Be("482e9495c37890123456789abcdef0123456789a");
    }

    [Test]
    public async Task SearchGet_MultiIndexer_DeduplicatesByTitleAndSize_WhenOneLacksInfoHash()
    {
        var idx1 = new IndexerDefinition { Id = 1, Name = "Tracker1", Enable = true, EnableSearch = true, Url = "http://t1" };
        var idx2 = new IndexerDefinition { Id = 2, Name = "Tracker2", Enable = true, EnableSearch = true, Url = "http://t2" };
        this.indexerRepository.GetSearchEnabled().Returns(new List<IndexerDefinition> { idx1, idx2 });

        this.torznabClient.SearchAsync(
            idx1,
            Arg.Any<TorznabSearchCriteria>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new()
                {
                    Title = "FreeBSD.14.0.RELEASE.x86_64",
                    Size = 4000000000,
                    Seeders = 50,
                    InfoHash = "a1b2c3d4e5f60123456789abcdef0123456789ab",
                },
            }));

        this.torznabClient.SearchAsync(
            idx2,
            Arg.Any<TorznabSearchCriteria>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new()
                {
                    Title = "freebsd.14.0.release.x86_64",
                    Size = 4000000000,
                    Seeders = 10,
                    InfoHash = null,
                    MagnetUrl = null,
                },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "freebsd" });
        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var envelope = (IndexerSearchEnvelope)okResult.Value!;

        envelope.Results.Should().HaveCount(1);
        envelope.Results[0].Seeders.Should().Be(50);
        envelope.Results[0].InfoHash.Should().Be("a1b2c3d4e5f60123456789abcdef0123456789ab");
    }

    [Test]
    public async Task SearchGet_PreservesCommentsAndPublishDateFromTorznabResult()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Tracker1", Enable = true, EnableSearch = true, Url = "http://t1" };
        this.indexerRepository.Get(1).Returns(indexer);

        var pubDate = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        this.torznabClient.SearchAsync(
            indexer,
            Arg.Any<TorznabSearchCriteria>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new()
                {
                    Title = "Some Release",
                    Seeders = 10,
                    Comments = "https://tracker1.org/details.php?id=12345",
                    PublishDate = pubDate,
                },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "test", IndexerId = 1 });
        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var envelope = (IndexerSearchEnvelope)okResult.Value!;

        envelope.Results.Should().HaveCount(1);
        envelope.Results[0].Comments.Should().Be("https://tracker1.org/details.php?id=12345");
        envelope.Results[0].PublishDate.Should().Be(pubDate);
    }

    [Test]
    public async Task SearchGet_EnforcesDeterministicSorting_WhenSeedersTied()
    {
        var idx = new IndexerDefinition { Id = 1, Name = "Tracker1", Enable = true, EnableSearch = true, Url = "http://t1" };
        this.indexerRepository.Get(1).Returns(idx);

        var dateOld = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dateNew = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        this.torznabClient.SearchAsync(
            idx,
            Arg.Any<TorznabSearchCriteria>(),
            Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult(new List<TorznabSearchResult>
            {
                new() { Title = "B Release", Seeders = 100, DownloadVolumeFactor = 1.0, PublishDate = dateOld },
                new() { Title = "A Release Freeleech", Seeders = 100, DownloadVolumeFactor = 0.0, PublishDate = dateOld },
                new() { Title = "C Release Newer", Seeders = 100, DownloadVolumeFactor = 1.0, PublishDate = dateNew },
                new() { Title = "A Release Not Freeleech", Seeders = 100, DownloadVolumeFactor = 1.0, PublishDate = dateOld },
            }));

        var actionResult = await this.controller.SearchGet(new IndexerSearchRequest { Query = "tied", IndexerId = 1 });
        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var envelope = (IndexerSearchEnvelope)okResult.Value!;

        envelope.Results.Select(r => r.Title).Should().ContainInOrder(
            "A Release Freeleech",
            "C Release Newer",
            "A Release Not Freeleech",
            "B Release");
    }

    [Test]
    public void DeduplicateReleases_WhenNeitherHasInfoHash_DeduplicatesByTitleAndSize()
    {
        var releases = new List<ReleaseInfoResource>
        {
            new() { Title = "Ubuntu-24.04-desktop-amd64.iso", Size = 5000000000, Seeders = 10, DownloadVolumeFactor = 1.0 },
            new() { Title = " ubuntu-24.04-desktop-amd64.iso ", Size = 5000000000, Seeders = 30, DownloadVolumeFactor = 0.0 },
        };

        var result = IndexerController.DeduplicateReleases(releases);

        result.Should().HaveCount(1);
        result[0].Seeders.Should().Be(30);
        result[0].IsFreeleech.Should().BeTrue();
    }
}

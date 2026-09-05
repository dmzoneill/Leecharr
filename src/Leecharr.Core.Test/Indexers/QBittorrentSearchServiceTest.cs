// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Search;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class QBittorrentSearchServiceTest
{
    private IIndexerRepository indexerRepository;
    private ITorznabClient torznabClient;
    private QBittorrentSearchService searchService;

    [SetUp]
    public void SetUp()
    {
        this.indexerRepository = Substitute.For<IIndexerRepository>();
        this.torznabClient = Substitute.For<ITorznabClient>();
        this.searchService = new QBittorrentSearchService(this.indexerRepository, this.torznabClient);
    }

    [Test]
    public async Task StartSearch_ExecutesAcrossEnabledIndexers_AggregatesResults()
    {
        var indexer1 = new IndexerDefinition { Id = 1, Name = "IndexerOne", Enable = true, EnableSearch = true, Url = "http://indexer1" };
        var indexer2 = new IndexerDefinition { Id = 2, Name = "IndexerTwo", Enable = true, EnableSearch = true, Url = "http://indexer2" };
        this.indexerRepository.GetSearchEnabled().Returns(new[] { indexer1, indexer2 });

        this.torznabClient.SearchAsync(indexer1, "ubuntu", limit: 100)
            .Returns(new List<TorznabSearchResult>
            {
                new() { Title = "Ubuntu 24.04 Desktop", Size = 4000000000, Seeders = 100, Leechers = 10, DownloadUrl = "http://dl1", InfoHash = "hash1" },
            });

        this.torznabClient.SearchAsync(indexer2, "ubuntu", limit: 100)
            .Returns(new List<TorznabSearchResult>
            {
                new() { Title = "Ubuntu 24.04 Server", Size = 2000000000, Seeders = 50, Leechers = 5, MagnetUrl = "magnet:?xt=urn:btih:hash2", InfoHash = "hash2" },
            });

        var id = this.searchService.StartSearch("ubuntu");
        id.Should().BeGreaterThan(0);

        // Allow background search task to complete
        await Task.Delay(200);

        var status = this.searchService.GetStatus(id);
        status.Should().NotBeNull();
        status.Status.Should().Be("Stopped");
        status.Total.Should().Be(2);

        var results = this.searchService.GetResults(id);
        results.Results.Should().HaveCount(2);
        results.Results[0].FileName.Should().Be("Ubuntu 24.04 Desktop");
        results.Results[0].FileSize.Should().Be(4000000000);
        results.Results[1].FileName.Should().Be("Ubuntu 24.04 Server");
        results.Results[1].FileUrl.Should().Be("magnet:?xt=urn:btih:hash2");
    }

    [Test]
    public void GetResults_WithLimitAndOffset_PaginatesProperly()
    {
        var id = this.searchService.StartSearch("test");
        var res = this.searchService.GetResults(id, limit: 10, offset: 5);
        res.Should().NotBeNull();
    }

    [Test]
    public void PluginsAndCategories_ReturnExpectedDefaults()
    {
        var plugins = this.searchService.GetPlugins();
        plugins.Should().NotBeEmpty();

        var categories = this.searchService.GetCategories();
        categories.Should().Contain(new[] { "all", "movies", "tv", "music" });
    }

    [Test]
    public void StopAndDelete_ManageJobLifecycle()
    {
        var id = this.searchService.StartSearch("linux");
        this.searchService.StopSearch(id).Should().BeTrue();
        this.searchService.DeleteSearch(id).Should().BeTrue();
        this.searchService.GetStatus(id).Should().BeNull();
    }

    [Test]
    public async Task StartSearch_WhenExpiredJobsExist_AutomaticallyPrunesExpiredJobs()
    {
        using var shortTtlService = new QBittorrentSearchService(this.indexerRepository, this.torznabClient, maxJobs: 100, jobTtl: TimeSpan.FromMilliseconds(50));
        var id1 = shortTtlService.StartSearch("query1");
        var job1 = shortTtlService.GetJob(id1);
        job1.Should().NotBeNull();

        // Wait for TTL to expire
        await Task.Delay(100);

        // Starting a new search should trigger pruning of expired job1
        var id2 = shortTtlService.StartSearch("query2");

        shortTtlService.GetStatus(id1).Should().BeNull();
        shortTtlService.GetStatus(id2).Should().NotBeNull();

        // Verify job1 CTS was properly disposed
        Action act = () => _ = job1.Cts.Token;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Test]
    public void StartSearch_WhenMaxCapacityExceeded_EvictsOldestCompletedJob()
    {
        using var smallCapacityService = new QBittorrentSearchService(this.indexerRepository, this.torznabClient, maxJobs: 2);
        var id1 = smallCapacityService.StartSearch("query1");
        smallCapacityService.StopSearch(id1);

        var id2 = smallCapacityService.StartSearch("query2");
        smallCapacityService.StopSearch(id2);

        // Starting job 3 with maxJobs=2 should evict the oldest completed job (id1)
        var id3 = smallCapacityService.StartSearch("query3");

        smallCapacityService.GetStatus(id1).Should().BeNull();
        smallCapacityService.GetStatus(id2).Should().NotBeNull();
        smallCapacityService.GetStatus(id3).Should().NotBeNull();
    }

    [Test]
    public void DeleteSearch_ProperlyDisposesCancellationTokenSource()
    {
        var id = this.searchService.StartSearch("query");
        var job = this.searchService.GetJob(id);
        job.Should().NotBeNull();
        job.Cts.Should().NotBeNull();

        this.searchService.DeleteSearch(id).Should().BeTrue();

        Action act = () => _ = job.Cts.Token;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Test]
    public void StopSearch_ProperlyDisposesCancellationTokenSource()
    {
        var id = this.searchService.StartSearch("query");
        var job = this.searchService.GetJob(id);
        job.Should().NotBeNull();
        job.Cts.Should().NotBeNull();

        this.searchService.StopSearch(id).Should().BeTrue();

        Action act = () => _ = job.Cts.Token;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Test]
    public void Dispose_DisposesAllRemainingJobsAndCancellationTokenSources()
    {
        var service = new QBittorrentSearchService(this.indexerRepository, this.torznabClient);
        var id1 = service.StartSearch("query1");
        var id2 = service.StartSearch("query2");

        var job1 = service.GetJob(id1);
        var job2 = service.GetJob(id2);

        service.Dispose();

        Action act1 = () => _ = job1.Cts.Token;
        act1.Should().Throw<ObjectDisposedException>();

        Action act2 = () => _ = job2.Cts.Token;
        act2.Should().Throw<ObjectDisposedException>();

        service.GetAllStatuses().Should().BeEmpty();
    }

    [Test]
    public async Task StartSearch_WithSpecificPluginAndCategory_QueriesOnlyFilteredIndexerWithCategoryId()
    {
        var indexer1 = new IndexerDefinition { Id = 1, Name = "IndexerOne", Enable = true, EnableSearch = true, Url = "http://indexer1" };
        var indexer2 = new IndexerDefinition { Id = 2, Name = "IndexerTwo", Enable = true, EnableSearch = true, Url = "http://indexer2" };
        this.indexerRepository.GetSearchEnabled().Returns(new[] { indexer1, indexer2 });

        this.torznabClient.SearchAsync(indexer1, "matrix", categoryId: 2000, limit: 100)
            .Returns(new List<TorznabSearchResult>
            {
                new() { Title = "The Matrix 1999 4K", Size = 15000000000, Seeders = 80, Leechers = 2, DownloadUrl = "http://dl-matrix" },
            });

        var id = this.searchService.StartSearch("matrix", plugins: "IndexerOne", category: "movies");
        id.Should().BeGreaterThan(0);

        await Task.Delay(200);

        await this.torznabClient.Received(1).SearchAsync(indexer1, "matrix", categoryId: 2000, limit: 100);
        await this.torznabClient.DidNotReceive().SearchAsync(indexer2, Arg.Any<string>(), categoryId: Arg.Any<int?>(), limit: Arg.Any<int>());

        var results = this.searchService.GetResults(id);
        results.Results.Should().HaveCount(1);
        results.Results[0].FileName.Should().Be("The Matrix 1999 4K");
    }
}

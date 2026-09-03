// Copyright (c) PlaceholderCompany. All rights reserved.

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
}

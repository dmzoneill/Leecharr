// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class ProwlarrSyncServiceTest
{
    private IIndexerRepository repository = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<IIndexerRepository>();
    }

    [Test]
    public async Task SyncFromProwlarrAsync_WhenUrlOrKeyEmpty_ReturnsZero()
    {
        var service = new ProwlarrSyncService(this.repository);
        var count = await service.SyncFromProwlarrAsync(string.Empty, string.Empty);
        count.Should().Be(0);
    }

    [Test]
    public async Task SyncFromProwlarrAsync_ParsesProwlarrJson_InsertsNewTorrentIndexers()
    {
        var json = @"[
          {
            ""id"": 1,
            ""name"": ""Prowlarr Tracker 1"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 25,
            ""protocol"": ""torrent""
          },
          {
            ""id"": 2,
            ""name"": ""Prowlarr Usenet 1"",
            ""implementation"": ""Newznab"",
            ""enable"": true,
            ""priority"": 25,
            ""protocol"": ""usenet""
          }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        });

        using var httpClient = new HttpClient(handler);
        var service = new ProwlarrSyncService(this.repository, httpClient);

        this.repository.All().Returns(new List<IndexerDefinition>());

        var synced = await service.SyncFromProwlarrAsync("http://prowlarr.local:9696", "fake-prowlarr-key");

        synced.Should().Be(1);
        this.repository.Received(1).Insert(Arg.Is<IndexerDefinition>(i =>
            i.Name == "Prowlarr Tracker 1" &&
            i.Implementation == "Torznab" &&
            i.Url == "http://prowlarr.local:9696/1/api" &&
            i.ApiKey == "fake-prowlarr-key" &&
            i.Enable == true &&
            i.Priority == 25 &&
            i.ProwlarrIndexerId == 1 &&
            i.IsProwlarrManaged == true));
    }

    [Test]
    public async Task SyncFromProwlarrAsync_WhenIndexerAlreadyExists_ReconcilesByProwlarrIndexerIdAndHandlesRenames()
    {
        var json = @"[
          {
            ""id"": 5,
            ""name"": ""Renamed Tracker"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 10,
            ""protocol"": ""torrent""
          }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        });

        using var httpClient = new HttpClient(handler);
        var service = new ProwlarrSyncService(this.repository, httpClient);

        var existing = new IndexerDefinition
        {
            Id = 100,
            Name = "Old Tracker Name",
            Url = "http://old-url",
            ApiKey = "old-key",
            Priority = 7,
            ProwlarrIndexerId = 5,
            IsProwlarrManaged = true,
        };
        this.repository.All().Returns(new List<IndexerDefinition> { existing });

        var synced = await service.SyncFromProwlarrAsync("http://prowlarr.local:9696", "new-key");

        synced.Should().Be(1);
        this.repository.Received(1).Update(Arg.Is<IndexerDefinition>(i =>
            i.Id == 100 &&
            i.Name == "Renamed Tracker" &&
            i.ApiKey == "new-key" &&
            i.Url == "http://prowlarr.local:9696/5/api" &&
            i.Priority == 7 &&
            i.ProwlarrIndexerId == 5 &&
            i.IsProwlarrManaged == true));
    }

    [Test]
    public async Task SyncFromProwlarrAsync_PreservesLocalOverrides_PriorityFreeleechMinSeedersCategories()
    {
        var json = @"[
          {
            ""id"": 5,
            ""name"": ""Existing Tracker"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 10,
            ""protocol"": ""torrent"",
            ""categories"": [2000, 5000]
          }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        });

        using var httpClient = new HttpClient(handler);
        var service = new ProwlarrSyncService(this.repository, httpClient);

        var existing = new IndexerDefinition
        {
            Id = 100,
            Name = "Existing Tracker",
            Url = "http://old-url",
            ApiKey = "old-key",
            Priority = 3,
            FreeleechOnly = true,
            MinSeeders = 15,
            DownloadClientId = 2,
            Categories = new List<int> { 8000, 8010 },
            Tags = new List<int> { 1, 2 },
            ProwlarrIndexerId = 5,
            IsProwlarrManaged = true,
        };
        this.repository.All().Returns(new List<IndexerDefinition> { existing });

        var synced = await service.SyncFromProwlarrAsync("http://prowlarr.local:9696", "new-key");

        synced.Should().Be(1);
        this.repository.Received(1).Update(Arg.Is<IndexerDefinition>(i =>
            i.Id == 100 &&
            i.Priority == 3 &&
            i.FreeleechOnly == true &&
            i.MinSeeders == 15 &&
            i.DownloadClientId == 2 &&
            i.Categories.Count == 2 &&
            i.Categories.Contains(8000) &&
            i.Categories.Contains(8010) &&
            i.Tags.Contains(1)));
    }

    [Test]
    public async Task SyncFromProwlarrAsync_PrunesDeletedProwlarrIndexers_AndPreservesManualIndexers()
    {
        var json = @"[
          {
            ""id"": 1,
            ""name"": ""Active Prowlarr Tracker"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 25,
            ""protocol"": ""torrent""
          }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        });

        using var httpClient = new HttpClient(handler);
        var service = new ProwlarrSyncService(this.repository, httpClient);

        var activeProwlarrIndexer = new IndexerDefinition
        {
            Id = 10,
            Name = "Active Prowlarr Tracker",
            ProwlarrIndexerId = 1,
            IsProwlarrManaged = true,
        };
        var deletedProwlarrIndexer = new IndexerDefinition
        {
            Id = 20,
            Name = "Deleted Prowlarr Tracker",
            ProwlarrIndexerId = 99,
            IsProwlarrManaged = true,
        };
        var manualIndexer = new IndexerDefinition
        {
            Id = 30,
            Name = "Manual Custom Indexer",
            ProwlarrIndexerId = null,
            IsProwlarrManaged = false,
        };

        this.repository.All().Returns(new List<IndexerDefinition> { activeProwlarrIndexer, deletedProwlarrIndexer, manualIndexer });

        var synced = await service.SyncFromProwlarrAsync("http://prowlarr.local:9696", "prowlarr-key");

        synced.Should().Be(1);
        this.repository.Received(1).Delete(20);
        this.repository.DidNotReceive().Delete(10);
        this.repository.DidNotReceive().Delete(30);
    }

    [Test]
    public async Task SyncFromProwlarrAsync_ConcurrentCapabilityProbing_UsesTorznabClient()
    {
        var json = @"[
          {
            ""id"": 101,
            ""name"": ""Probe Tracker 1"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 5,
            ""protocol"": ""torrent""
          },
          {
            ""id"": 102,
            ""name"": ""Probe Tracker 2"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 5,
            ""protocol"": ""torrent""
          }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        });

        var torznabClient = Substitute.For<ITorznabClient>();
        torznabClient.FetchCapabilitiesAsync(Arg.Any<IndexerDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TorznabCapabilities
            {
                Categories = new List<TorznabCategory>
                {
                    new() { Id = 2000, Name = "Movies", SubCategories = new List<TorznabCategory>() },
                },
            }));

        using var httpClient = new HttpClient(handler);
        var service = new ProwlarrSyncService(this.repository, null, httpClient, torznabClient);

        this.repository.All().Returns(new List<IndexerDefinition>());

        var synced = await service.SyncFromProwlarrAsync("http://prowlarr.local:9696", "prowlarr-key");

        synced.Should().Be(2);
        this.repository.Received(2).Insert(Arg.Is<IndexerDefinition>(i => i.Categories.Contains(2000)));
        await torznabClient.Received(2).FetchCapabilitiesAsync(Arg.Any<IndexerDefinition>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SyncFromProwlarrAsync_ExtractsCategoriesFromFields_AssignsToIndexerDefinition()
    {
        var json = @"[
          {
            ""id"": 10,
            ""name"": ""Category Tracker"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 5,
            ""protocol"": ""torrent"",
            ""fields"": [
              {
                ""name"": ""categories"",
                ""value"": [2000, 5000, 5040]
              }
            ]
          }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        });

        using var httpClient = new HttpClient(handler);
        var service = new ProwlarrSyncService(this.repository, httpClient);

        this.repository.All().Returns(new List<IndexerDefinition>());

        var synced = await service.SyncFromProwlarrAsync("http://prowlarr.local:9696", "prowlarr-key");

        synced.Should().Be(1);
        this.repository.Received(1).Insert(Arg.Is<IndexerDefinition>(i =>
            i.Name == "Category Tracker" &&
            i.Categories != null &&
            i.Categories.Count == 3 &&
            i.Categories.Contains(2000) &&
            i.Categories.Contains(5000) &&
            i.Categories.Contains(5040)));
    }

    [Test]
    public async Task SyncFromProwlarrAsync_ExtractsCategoriesFromCapabilities_AssignsToIndexerDefinition()
    {
        var json = @"[
          {
            ""id"": 11,
            ""name"": ""Capabilities Tracker"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 5,
            ""protocol"": ""torrent"",
            ""capabilities"": {
              ""categories"": [
                { ""id"": 2000, ""name"": ""Movies"" },
                { ""id"": 5000, ""name"": ""TV"" }
              ]
            }
          }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        });

        using var httpClient = new HttpClient(handler);
        var service = new ProwlarrSyncService(this.repository, httpClient);

        this.repository.All().Returns(new List<IndexerDefinition>());

        var synced = await service.SyncFromProwlarrAsync("http://prowlarr.local:9696", "prowlarr-key");

        synced.Should().Be(1);
        this.repository.Received(1).Insert(Arg.Is<IndexerDefinition>(i =>
            i.Name == "Capabilities Tracker" &&
            i.Categories != null &&
            i.Categories.Count == 2 &&
            i.Categories.Contains(2000) &&
            i.Categories.Contains(5000)));
    }

    [Test]
    public async Task SyncFromProwlarrAsync_MapsEnableRssAndSearchFlags()
    {
        var json = @"[
          {
            ""id"": 12,
            ""name"": ""Search Only Tracker"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 5,
            ""protocol"": ""torrent"",
            ""enableRss"": false,
            ""enableAutomaticSearch"": true,
            ""enableInteractiveSearch"": false
          }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        });

        using var httpClient = new HttpClient(handler);
        var service = new ProwlarrSyncService(this.repository, httpClient);

        this.repository.All().Returns(new List<IndexerDefinition>());

        var synced = await service.SyncFromProwlarrAsync("http://prowlarr.local:9696", "prowlarr-key");

        synced.Should().Be(1);
        this.repository.Received(1).Insert(Arg.Is<IndexerDefinition>(i =>
            i.Name == "Search Only Tracker" &&
            i.EnableRss == false &&
            i.EnableSearch == true));
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.handler(request));
        }
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ProwlarrSyncComprehensiveIntegrationTests : IntegrationTestBase
{
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

    [Test]
    public async Task ProwlarrSync_SyncFromProwlarrAsync_ParsesTorrentIndexersAndFiltersUsenet()
    {
        var services = GlobalSetup.Factory.Services;
        var indexerRepo = (IIndexerRepository)services.GetService(typeof(IIndexerRepository))!;

        var prowlarrJson = @"[
          {
            ""id"": 10,
            ""name"": ""Prowlarr Torrent Indexer A"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""priority"": 15,
            ""protocol"": ""torrent"",
            ""fields"": [
                { ""name"": ""baseUrl"", ""value"": ""https://tracker-a.example.org"" }
            ],
            ""capabilities"": {
                ""categories"": [
                    { ""id"": 2000, ""name"": ""Movies"" },
                    { ""id"": 5000, ""name"": ""TV"" }
                ]
            }
          },
          {
            ""id"": 20,
            ""name"": ""Prowlarr Usenet Indexer B"",
            ""implementation"": ""Newznab"",
            ""enable"": true,
            ""priority"": 50,
            ""protocol"": ""usenet""
          }
        ]";

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri != null && req.RequestUri.AbsolutePath.Contains("/api/v1/indexer"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(prowlarrJson),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(mockHandler);
        var syncService = new ProwlarrSyncService(indexerRepo, httpClient);

        // 1. Initially IsConfigured is evaluated
        var isConfBefore = syncService.IsConfigured();

        // 2. Perform Sync
        var count = await syncService.SyncFromProwlarrAsync("http://127.0.0.1:9696", "prowlarr-secret-key-123", syncCategories: true);
        count.Should().Be(1);

        try
        {
            // 3. Verify indexer was inserted in database
            var allIndexers = indexerRepo.All().ToList();
            var synced = allIndexers.FirstOrDefault(i => i.ProwlarrIndexerId == 10);
            synced.Should().NotBeNull();
            synced!.Name.Should().Be("Prowlarr Torrent Indexer A");
            synced.Implementation.Should().Be("Torznab");
            synced.IsProwlarrManaged.Should().BeTrue();
            synced.ApiKey.Should().Be("prowlarr-secret-key-123");

            // 4. Verify Usenet indexer was skipped
            var skippedUsenet = allIndexers.FirstOrDefault(i => i.ProwlarrIndexerId == 20);
            skippedUsenet.Should().BeNull();

            // 5. IsConfigured should now be true
            syncService.IsConfigured().Should().BeTrue();
        }
        finally
        {
            // Cleanup synced indexer
            var toClean = indexerRepo.All().FirstOrDefault(i => i.ProwlarrIndexerId == 10);
            if (toClean != null)
            {
                indexerRepo.Delete(toClean.Id);
            }
        }
    }

    [Test]
    public async Task ProwlarrSync_SyncAllAsync_WithArrConnection_SyncsAcrossInstances()
    {
        var indexerRepo = Substitute.For<IIndexerRepository>();
        var arrRepo = Substitute.For<IArrConnectionRepository>();

        var mockConnections = new List<ArrConnectionDefinition>
        {
            new()
            {
                Id = 1,
                Name = "Local Prowlarr Instance",
                Url = "http://prowlarr.test:9696",
                ApiKey = "mock-arr-prowlarr-key",
                ArrType = "Prowlarr",
                Enable = true,
                SyncCategories = true,
            },
        };

        arrRepo.GetEnabled().Returns(mockConnections);
        arrRepo.All().Returns(mockConnections);

        var prowlarrJson = @"[
          {
            ""id"": 101,
            ""name"": ""Prowlarr Tracker Alpha"",
            ""implementation"": ""Torznab"",
            ""enable"": true,
            ""protocol"": ""torrent"",
            ""priority"": 20
          }
        ]";

        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(prowlarrJson),
        });

        using var httpClient = new HttpClient(mockHandler);
        var syncService = new ProwlarrSyncService(indexerRepo, httpClient, arrRepository: arrRepo);

        var synced = await syncService.SyncAllAsync();
        synced.Should().Be(1);

        indexerRepo.Received(1).Insert(Arg.Is<IndexerDefinition>(i =>
            i.ProwlarrIndexerId == 101 &&
            i.Name == "Prowlarr Tracker Alpha" &&
            i.IsProwlarrManaged == true));
    }
}

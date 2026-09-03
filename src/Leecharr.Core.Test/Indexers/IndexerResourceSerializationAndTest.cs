// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Api.V1.Indexers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class IndexerResourceSerializationAndTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Test]
    public void Categories_Deserializes_From_CommaSeparatedString()
    {
        var json = @"{ ""name"": ""Test"", ""categories"": ""2000,5000"" }";
        var res = JsonSerializer.Deserialize<IndexerResource>(json, JsonOptions);

        res.Should().NotBeNull();
        res!.Categories.Should().Equal(2000, 5000);
    }

    [Test]
    public void Categories_Deserializes_From_CommaSeparatedStringWithSpaces()
    {
        var json = @"{ ""name"": ""Test"", ""categories"": "" 2000 , 5000 , 8000 "" }";
        var res = JsonSerializer.Deserialize<IndexerResource>(json, JsonOptions);

        res.Should().NotBeNull();
        res!.Categories.Should().Equal(2000, 5000, 8000);
    }

    [Test]
    public void Categories_Deserializes_From_IntArray()
    {
        var json = @"{ ""name"": ""Test"", ""categories"": [2000, 5000] }";
        var res = JsonSerializer.Deserialize<IndexerResource>(json, JsonOptions);

        res.Should().NotBeNull();
        res!.Categories.Should().Equal(2000, 5000);
    }

    [Test]
    public void Categories_Deserializes_From_SingleInt()
    {
        var json = @"{ ""name"": ""Test"", ""categories"": 2000 }";
        var res = JsonSerializer.Deserialize<IndexerResource>(json, JsonOptions);

        res.Should().NotBeNull();
        res!.Categories.Should().Equal(2000);
    }

    [Test]
    public void Categories_Deserializes_From_EmptyString()
    {
        var json = @"{ ""name"": ""Test"", ""categories"": """" }";
        var res = JsonSerializer.Deserialize<IndexerResource>(json, JsonOptions);

        res.Should().NotBeNull();
        res!.Categories.Should().BeEmpty();
    }

    [Test]
    public void Categories_Deserializes_From_Null()
    {
        var json = @"{ ""name"": ""Test"", ""categories"": null }";
        var res = JsonSerializer.Deserialize<IndexerResource>(json, JsonOptions);

        res.Should().NotBeNull();
        res!.Categories.Should().BeEmpty();
    }

    [Test]
    public void IndexerType_AliasBindsToImplementation()
    {
        var json = @"{ ""name"": ""Test"", ""indexerType"": ""Prowlarr"" }";
        var res = JsonSerializer.Deserialize<IndexerResource>(json, JsonOptions);

        res.Should().NotBeNull();
        res!.Implementation.Should().Be("Prowlarr");
    }

    [Test]
    public void ArrConnectionResource_EnableAliasBindsToEnabled()
    {
        var json = @"{ ""name"": ""Sonarr"", ""enable"": false }";
        var res = JsonSerializer.Deserialize<ArrConnectionResource>(json, JsonOptions);

        res.Should().NotBeNull();
        res!.Enabled.Should().BeFalse();
    }

    [Test]
    public async Task TestDirectInternal_Prowlarr_ReturnsSuccessWithIndexerCount()
    {
        var prowlarrJson = @"[
            { ""id"": 1, ""name"": ""Indexer1"" },
            { ""id"": 2, ""name"": ""Indexer2"" }
        ]";

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/v1/indexer"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(prowlarrJson),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(mockHandler);
        var repo = Substitute.For<IIndexerRepository>();
        var torznabClient = Substitute.For<ITorznabClient>();
        var prowlarrSync = Substitute.For<IProwlarrSyncService>();
        var torrentService = Substitute.For<ITorrentService>();
        var torrentParser = Substitute.For<ITorrentFileParser>();

        var controller = new IndexerController(repo, torznabClient, prowlarrSync, torrentService, torrentParser, httpClient: httpClient);

        var resource = new IndexerResource
        {
            Name = "Prowlarr",
            Url = "http://localhost:9696",
            ApiKey = "test-key",
            Implementation = "Prowlarr",
        };

        var actionResult = await controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var testResult = okResult!.Value as IndexerTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();
        testResult.Message.Should().Contain("Found 2 indexers");
    }

    [Test]
    public async Task TestDirectInternal_Torznab_CapsVerified()
    {
        var capsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?><caps><server version=""1.0""/></caps>";

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Query.Contains("t=caps"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(capsXml),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(mockHandler);
        var repo = Substitute.For<IIndexerRepository>();
        var torznabClient = Substitute.For<ITorznabClient>();
        var prowlarrSync = Substitute.For<IProwlarrSyncService>();
        var torrentService = Substitute.For<ITorrentService>();
        var torrentParser = Substitute.For<ITorrentFileParser>();

        var controller = new IndexerController(repo, torznabClient, prowlarrSync, torrentService, torrentParser, httpClient: httpClient);

        var resource = new IndexerResource
        {
            Name = "MyTorznab",
            Url = "http://indexer.local/api",
            ApiKey = "my-key",
            Implementation = "Torznab",
        };

        var actionResult = await controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var testResult = okResult!.Value as IndexerTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();
        testResult.Message.Should().Contain("capabilities verified");
    }

    [Test]
    public async Task TestDirectInternal_Torznab_CapsFails_FallsBackToSearch()
    {
        var mockHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var httpClient = new HttpClient(mockHandler);
        var repo = Substitute.For<IIndexerRepository>();
        var torznabClient = Substitute.For<ITorznabClient>();
        torznabClient.SearchAsync(Arg.Any<IndexerDefinition>(), Arg.Any<string>(), limit: 1)
            .Returns(Task.FromResult(new List<TorznabSearchResult> { new() { Title = "Test Release" } }));

        var prowlarrSync = Substitute.For<IProwlarrSyncService>();
        var torrentService = Substitute.For<ITorrentService>();
        var torrentParser = Substitute.For<ITorrentFileParser>();

        var controller = new IndexerController(repo, torznabClient, prowlarrSync, torrentService, torrentParser, httpClient: httpClient);

        var resource = new IndexerResource
        {
            Name = "FallbackTorznab",
            Url = "http://indexer.local/api",
            ApiKey = "my-key",
            Implementation = "Torznab",
        };

        var actionResult = await controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var testResult = okResult!.Value as IndexerTestResult;
        testResult.Should().NotBeNull();
        testResult!.Success.Should().BeTrue();
        testResult.Message.Should().Be("Connected successfully to FallbackTorznab.");
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

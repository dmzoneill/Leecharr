// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment.Providers;

namespace Leecharr.Core.Test.MediaEnrichment;

[TestFixture]
public class TvdbMetadataProviderTest
{
    private IConfigService configService = null!;
    private IArrConnectionRepository arrRepository = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.arrRepository = Substitute.For<IArrConnectionRepository>();
    }

    [Test]
    public async Task ProbeHealthAsync_WhenApiKeyConfigured_ReturnsHealthyStatus()
    {
        this.configService.TvdbApiKey.Returns("valid-tvdb-api-key");
        var provider = new TvdbMetadataProvider(this.configService, this.arrRepository);

        var result = await provider.ProbeHealthAsync();

        result.Should().NotBeNull();
        result.IsHealthy.Should().BeTrue();
        result.StatusMessage.Should().Contain("active and configured");
    }

    [Test]
    public async Task ProbeHealthAsync_WhenArrConfigured_ReturnsHealthyStatusWithServarrFallback()
    {
        this.configService.TvdbApiKey.Returns(string.Empty);
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition>
        {
            new() { Name = "Sonarr", ArrType = "Sonarr", Url = "http://localhost:8989", Enable = true },
        });

        var provider = new TvdbMetadataProvider(this.configService, this.arrRepository);

        var result = await provider.ProbeHealthAsync();

        result.Should().NotBeNull();
        result.IsHealthy.Should().BeTrue();
        result.StatusMessage.Should().Contain("Servarr fallback");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenTitleEmpty_ReturnsNull()
    {
        var provider = new TvdbMetadataProvider(this.configService, this.arrRepository);
        var result = await provider.FetchMetadataAsync(string.Empty);
        result.Should().BeNull();
    }

    [Test]
    public async Task FetchMetadataAsync_WithTvdbV4Api_AuthenticatesAndFetchesExtendedSeriesMetadata()
    {
        this.configService.TvdbApiKey.Returns("my-tvdb-key");
        this.configService.TvdbPin.Returns("1234");

        var loginJson = @"{
            ""status"": ""success"",
            ""data"": {
                ""token"": ""jwt.fake.token.123""
            }
        }";

        var searchJson = @"{
            ""status"": ""success"",
            ""data"": [
                {
                    ""tvdb_id"": ""series-81189"",
                    ""name"": ""Breaking Bad"",
                    ""year"": ""2008"",
                    ""overview"": ""A high school chemistry teacher diagnosed with cancer."",
                    ""image_url"": ""https://artworks.thetvdb.com/banners/posters/81189-1.jpg"",
                    ""score"": 9.5
                }
            ]
        }";

        var extendedJson = @"{
            ""status"": ""success"",
            ""data"": {
                ""id"": 81189,
                ""name"": ""Breaking Bad (Extended)"",
                ""year"": ""2008"",
                ""overview"": ""Detailed description of Breaking Bad."",
                ""score"": 9.5,
                ""image"": ""https://artworks.thetvdb.com/banners/posters/81189-extended.jpg"",
                ""artworks"": [
                    { ""type"": 1, ""image"": ""https://artworks.thetvdb.com/banners/fanart/81189-bg.jpg"" },
                    { ""type"": 3, ""image"": ""https://artworks.thetvdb.com/banners/banners/81189-banner.jpg"" }
                ],
                ""genres"": [
                    { ""name"": ""Crime"" },
                    { ""name"": ""Drama"" },
                    { ""name"": ""Thriller"" }
                ],
                ""characters"": [
                    { ""personName"": ""Bryan Cranston"", ""name"": ""Walter White"" },
                    { ""personName"": ""Aaron Paul"", ""name"": ""Jesse Pinkman"" }
                ]
            }
        }";

        var handler = new MockHttpMessageHandler(req =>
        {
            var uri = req.RequestUri?.ToString() ?? string.Empty;
            if (uri.EndsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(loginJson, Encoding.UTF8, "application/json"),
                };
            }

            if (uri.Contains("/search", StringComparison.OrdinalIgnoreCase))
            {
                req.Headers.Authorization?.Scheme.Should().Be("Bearer");
                req.Headers.Authorization?.Parameter.Should().Be("jwt.fake.token.123");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(searchJson, Encoding.UTF8, "application/json"),
                };
            }

            if (uri.Contains("/series/81189/extended", StringComparison.OrdinalIgnoreCase))
            {
                req.Headers.Authorization?.Scheme.Should().Be("Bearer");
                req.Headers.Authorization?.Parameter.Should().Be("jwt.fake.token.123");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(extendedJson, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var provider = new TvdbMetadataProvider(this.configService, this.arrRepository, httpClient: httpClient);

        var result = await provider.FetchMetadataAsync("Breaking.Bad.S01E01.720p.HDTV.x264", "tv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Breaking Bad (Extended)");
        result.Year.Should().Be(2008);
        result.Overview.Should().Be("Detailed description of Breaking Bad.");
        result.Rating.Should().Be(9.5);
        result.TvdbId.Should().Be("81189");
        result.PosterUrl.Should().Be("https://artworks.thetvdb.com/banners/posters/81189-extended.jpg");
        result.BackdropUrl.Should().Be("https://artworks.thetvdb.com/banners/fanart/81189-bg.jpg");
        result.BannerUrl.Should().Be("https://artworks.thetvdb.com/banners/banners/81189-banner.jpg");
        result.Genres.Should().Be("Crime, Drama, Thriller");
        result.Cast.Should().ContainInOrder("Bryan Cranston", "Aaron Paul");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenApiFails_FallsBackToServarr()
    {
        this.configService.TvdbApiKey.Returns(string.Empty);

        var sonarrJson = @"[
            {
                ""title"": ""Better Call Saul"",
                ""year"": 2015,
                ""overview"": ""The trials and tribulations of criminal lawyer Jimmy McGill."",
                ""ratings"": { ""value"": 9.0 },
                ""tvdbId"": 273181,
                ""imdbId"": ""tt3032476"",
                ""genres"": [""Crime"", ""Drama""],
                ""images"": [
                    { ""coverType"": ""poster"", ""remoteUrl"": ""https://example.com/saul-poster.jpg"" },
                    { ""coverType"": ""fanart"", ""url"": ""/MediaCover/5/fanart.jpg"" },
                    { ""coverType"": ""banner"", ""url"": ""/MediaCover/5/banner.jpg"" }
                ]
            }
        ]";

        var handler = new MockHttpMessageHandler(req =>
        {
            var uri = req.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("/api/v3/series/lookup", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sonarrJson, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var conn = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://127.0.0.1:8989",
            ApiKey = "sonarr-key",
            Enable = true,
        };
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition> { conn });

        var provider = new TvdbMetadataProvider(this.configService, this.arrRepository, httpClient: httpClient);

        var result = await provider.FetchMetadataAsync("Better Call Saul S01", "tv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Better Call Saul");
        result.Year.Should().Be(2015);
        result.Overview.Should().Be("The trials and tribulations of criminal lawyer Jimmy McGill.");
        result.Rating.Should().Be(9.0);
        result.TvdbId.Should().Be("273181");
        result.ImdbId.Should().Be("tt3032476");
        result.Genres.Should().Be("Crime, Drama");
        result.PosterUrl.Should().Be("https://example.com/saul-poster.jpg");
        result.BackdropUrl.Should().Be("http://127.0.0.1:8989/MediaCover/5/fanart.jpg?apikey=sonarr-key");
        result.BannerUrl.Should().Be("http://127.0.0.1:8989/MediaCover/5/banner.jpg?apikey=sonarr-key");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenNoApiKeyAndNoArr_ReturnsHeuristicCleanMetadata()
    {
        this.configService.TvdbApiKey.Returns(string.Empty);
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var provider = new TvdbMetadataProvider(this.configService, this.arrRepository);

        var result = await provider.FetchMetadataAsync("Chernobyl.2019.S01E01.1080p.WEBRip.x265", "tv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Chernobyl");
        result.Year.Should().Be(2019);
        result.MediaType.Should().Be("TV");
        result.Overview.Should().BeEmpty();
        result.Rating.Should().Be(0.0);
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

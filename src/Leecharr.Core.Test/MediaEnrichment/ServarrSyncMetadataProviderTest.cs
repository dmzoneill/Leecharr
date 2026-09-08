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
using NzbDrone.Core.MediaEnrichment.Providers;

namespace Leecharr.Core.Test.MediaEnrichment;

[TestFixture]
public class ServarrSyncMetadataProviderTest
{
    private IArrConnectionRepository arrRepository = null!;

    [SetUp]
    public void SetUp()
    {
        this.arrRepository = Substitute.For<IArrConnectionRepository>();
    }

    [Test]
    public async Task FetchMetadataAsync_WhenArrReturnsRatingsNull_DoesNotThrowAndParsesOtherFields()
    {
        var responseJson = @"[
            {
                ""title"": ""Unrated Movie"",
                ""year"": 2024,
                ""overview"": ""A film without ratings"",
                ""ratings"": null,
                ""imdbId"": ""tt9999999"",
                ""tmdbId"": 12345,
                ""genres"": [""Drama"", ""Mystery""],
                ""images"": [
                    { ""coverType"": ""poster"", ""remoteUrl"": ""https://example.com/poster.jpg"" },
                    { ""coverType"": ""fanart"", ""url"": ""/MediaCover/1/fanart.jpg"" }
                ]
            }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });

        using var httpClient = new HttpClient(handler);
        var provider = new ServarrSyncMetadataProvider(this.arrRepository, httpClient);

        var conn = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Radarr",
            ArrType = "Radarr",
            Url = "http://127.0.0.1:7878",
            ApiKey = "test-api-key",
            Enable = true,
        };
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition> { conn });

        var result = await provider.FetchMetadataAsync("Unrated Movie", "movies");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Unrated Movie");
        result.Year.Should().Be(2024);
        result.Overview.Should().Be("A film without ratings");
        result.Rating.Should().Be(0.0);
        result.ImdbId.Should().Be("tt9999999");
        result.TmdbId.Should().Be("12345");
        result.Genres.Should().Be("Drama, Mystery");
        result.PosterUrl.Should().Be("https://example.com/poster.jpg");
        result.BackdropUrl.Should().Be("http://127.0.0.1:7878/MediaCover/1/fanart.jpg?apikey=test-api-key");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenArrReturnsValidRating_ParsesRatingCorrectly()
    {
        var responseJson = @"[
            {
                ""title"": ""Breaking Bad"",
                ""year"": 2008,
                ""overview"": ""A chemistry teacher diagnosed with cancer"",
                ""ratings"": { ""value"": 9.5 },
                ""tvdbId"": 81189
            }
        ]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });

        using var httpClient = new HttpClient(handler);
        var provider = new ServarrSyncMetadataProvider(this.arrRepository, httpClient);

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

        var result = await provider.FetchMetadataAsync("Breaking Bad", "tv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Breaking Bad");
        result.Year.Should().Be(2008);
        result.Overview.Should().Be("A chemistry teacher diagnosed with cancer");
        result.Rating.Should().Be(9.5);
        result.TvdbId.Should().Be("81189");
    }

    [TestCase(@"{ ""title"": ""Test Show"", ""ratings"": 42 }")]
    [TestCase(@"{ ""title"": ""Test Show"", ""ratings"": ""8.5"" }")]
    [TestCase(@"{ ""title"": ""Test Show"", ""ratings"": [1, 2, 3] }")]
    [TestCase(@"{ ""title"": ""Test Show"", ""ratings"": true }")]
    [TestCase(@"{ ""title"": ""Test Show"", ""ratings"": {} }")]
    [TestCase(@"{ ""title"": ""Test Show"", ""ratings"": { ""value"": ""not-a-number"" } }")]
    [TestCase(@"{ ""title"": ""Test Show"" }")]
    public async Task FetchMetadataAsync_WhenRatingsIsNonObjectOrMalformed_SafelyIgnoresRating(string itemJson)
    {
        var responseJson = $"[{itemJson}]";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });

        using var httpClient = new HttpClient(handler);
        var provider = new ServarrSyncMetadataProvider(this.arrRepository, httpClient);

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

        var result = await provider.FetchMetadataAsync("Test Show", "tv");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Show");
        result.Rating.Should().Be(0.0);
    }

    [Test]
    public async Task FetchMetadataAsync_WhenTargetingLidarr_QueriesSearchEndpoint()
    {
        string requestedUri = null!;
        var responseJson = @"[
            {
                ""title"": ""Dark Side of the Moon"",
                ""year"": 1973,
                ""overview"": ""Eighth studio album by Pink Floyd"",
                ""ratings"": { ""value"": 9.8 }
            }
        ]";

        var handler = new MockHttpMessageHandler(req =>
        {
            requestedUri = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var provider = new ServarrSyncMetadataProvider(this.arrRepository, httpClient);

        var conn = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Lidarr",
            ArrType = "Lidarr",
            Url = "http://127.0.0.1:8686",
            ApiKey = "lidarr-key",
            Enable = true,
        };
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition> { conn });

        var result = await provider.FetchMetadataAsync("Pink Floyd Dark Side of the Moon", "music");

        result.Should().NotBeNull();
        result!.MediaType.Should().Be("Music");
        requestedUri.Should().Contain("/api/v1/search?term=");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenNoEnabledConnections_ReturnsFallbackPlaceholder()
    {
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var provider = new ServarrSyncMetadataProvider(this.arrRepository);
        var result = await provider.FetchMetadataAsync("Inception.2010.1080p", "movies", 2010);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Inception");
        result.Year.Should().Be(2010);
        result.MediaType.Should().Be("Movie");
        result.Rating.Should().Be(8.0);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task FetchMetadataAsync_WhenTitleIsNullOrWhitespace_ReturnsNull(string title)
    {
        var provider = new ServarrSyncMetadataProvider(this.arrRepository);
        var result = await provider.FetchMetadataAsync(title);

        result.Should().BeNull();
    }

    [Test]
    public async Task ProbeHealthAsync_ReturnsHealthyWithArrCount()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Id = 1, Name = "Sonarr" },
            new() { Id = 2, Name = "Radarr" },
        };
        this.arrRepository.All().Returns(connections);

        var provider = new ServarrSyncMetadataProvider(this.arrRepository);
        var health = await provider.ProbeHealthAsync();

        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Contain("2 Arr instances");
    }

    [TestCase("Severance.S02E01.1080p.WEB-DL", "Severance")]
    [TestCase("The.Matrix.1999.2160p.UHD.HDR", "The Matrix")]
    [TestCase("Dune.Part.Two.2024.x265", "Dune Part Two")]
    public void CleanTitle_CleansReleaseTagsAndYears(string raw, string expected)
    {
        ServarrSyncMetadataProvider.CleanTitle(raw).Should().Be(expected);
    }

    [Test]
    public async Task FetchMetadataAsync_WhenQueueMatchesDownloadId_CorrelatesExactSeriesAndPopulatesArrMediaId()
    {
        var queueJson = @"{
            ""page"": 1,
            ""pageSize"": 10,
            ""totalRecords"": 1,
            ""records"": [
                {
                    ""id"": 101,
                    ""downloadId"": ""A1B2C3D4E5F6"",
                    ""title"": ""Severance.S02E01.1080p.WEB-DL"",
                    ""seriesId"": 42,
                    ""series"": {
                        ""id"": 42,
                        ""title"": ""Severance"",
                        ""year"": 2022,
                        ""overview"": ""Office workers divide memories"",
                        ""tvdbId"": 371980,
                        ""imdbId"": ""tt11280740"",
                        ""genres"": [""Drama"", ""Sci-Fi""],
                        ""ratings"": { ""value"": 8.7 },
                        ""images"": [
                            { ""coverType"": ""poster"", ""url"": ""/MediaCover/42/poster.jpg"" },
                            { ""coverType"": ""banner"", ""url"": ""/MediaCover/42/banner.jpg"" }
                        ]
                    }
                }
            ]
        }";

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/v3/queue"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(queueJson, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var provider = new ServarrSyncMetadataProvider(this.arrRepository, httpClient);

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

        var result = await provider.FetchMetadataAsync("Severance.S02E01.1080p", "tv", 2022, "A1B2C3D4E5F6");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Severance");
        result.Year.Should().Be(2022);
        result.ArrMediaId.Should().Be(42);
        result.TvdbId.Should().Be("371980");
        result.ImdbId.Should().Be("tt11280740");
        result.MediaType.Should().Be("TV");
        result.BannerUrl.Should().Be("http://127.0.0.1:8989/MediaCover/42/banner.jpg?apikey=sonarr-key");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenHistoryMatchesInfoHash_CorrelatesExactMovieAndPopulatesArrMediaId()
    {
        var historyJson = @"{
            ""page"": 1,
            ""pageSize"": 100,
            ""records"": [
                {
                    ""id"": 201,
                    ""downloadId"": ""F6E5D4C3B2A1"",
                    ""sourceTitle"": ""Oppenheimer.2023.2160p"",
                    ""movie"": {
                        ""id"": 99,
                        ""title"": ""Oppenheimer"",
                        ""year"": 2023,
                        ""overview"": ""The story of J. Robert Oppenheimer"",
                        ""tmdbId"": 872585,
                        ""imdbId"": ""tt15398776"",
                        ""ratings"": { ""value"": 8.9 }
                    }
                }
            ]
        }";

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/v3/queue"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"records\":[]}", Encoding.UTF8, "application/json"),
                };
            }

            if (req.RequestUri!.AbsolutePath.Contains("/api/v3/history"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(historyJson, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var provider = new ServarrSyncMetadataProvider(this.arrRepository, httpClient);

        var conn = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Radarr",
            ArrType = "Radarr",
            Url = "http://127.0.0.1:7878",
            ApiKey = "radarr-key",
            Enable = true,
        };
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition> { conn });

        var result = await provider.FetchMetadataAsync("Oppenheimer.2023.2160p", "movies", 2023, "F6E5D4C3B2A1");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Oppenheimer");
        result.Year.Should().Be(2023);
        result.ArrMediaId.Should().Be(99);
        result.TmdbId.Should().Be("872585");
        result.ImdbId.Should().Be("tt15398776");
        result.MediaType.Should().Be("Movie");
    }

    [Test]
    public async Task FetchMetadataAsync_WhenLidarrQueueMatches_PopulatesArtistNameAlbumTitleAndMusicBrainzId()
    {
        var queueJson = @"[
            {
                ""id"": 301,
                ""downloadId"": ""MUSIC123456"",
                ""artistId"": 15,
                ""albumId"": 88,
                ""artist"": {
                    ""id"": 15,
                    ""artistName"": ""Pink Floyd"",
                    ""foreignArtistId"": ""83d91898-7763-47d7-b03b-b92132375c47"",
                    ""overview"": ""English rock band formed in London""
                },
                ""album"": {
                    ""id"": 88,
                    ""title"": ""The Dark Side of the Moon"",
                    ""foreignAlbumId"": ""a1b2c3d4-e5f6-7890-1234-56789abcdef0"",
                    ""releaseDate"": ""1973-03-01T00:00:00Z"",
                    ""images"": [
                        { ""coverType"": ""cover"", ""url"": ""/MediaCover/88/cover.jpg"" }
                    ]
                }
            }
        ]";

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/v1/queue"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(queueJson, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var provider = new ServarrSyncMetadataProvider(this.arrRepository, httpClient);

        var conn = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Lidarr",
            ArrType = "Lidarr",
            Url = "http://127.0.0.1:8686",
            ApiKey = "lidarr-key",
            Enable = true,
        };
        this.arrRepository.GetEnabled().Returns(new List<ArrConnectionDefinition> { conn });

        var result = await provider.FetchMetadataAsync("Pink Floyd - The Dark Side of the Moon", "music", 1973, "MUSIC123456");

        result.Should().NotBeNull();
        result!.MediaType.Should().Be("Music");
        result.ArtistName.Should().Be("Pink Floyd");
        result.AlbumTitle.Should().Be("The Dark Side of the Moon");
        result.MusicBrainzId.Should().Be("83d91898-7763-47d7-b03b-b92132375c47");
        result.ArrMediaId.Should().Be(15);
        result.Year.Should().Be(1973);
        result.PosterUrl.Should().Be("http://127.0.0.1:8686/MediaCover/88/cover.jpg?apikey=lidarr-key");
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

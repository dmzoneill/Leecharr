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
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class TorznabAndProwlarrClientTest
{
    private IConfigService configService = null!;
    private IIndexerRepository indexerRepository = null!;
    private IArrConnectionRepository arrRepository = null!;

    [SetUp]
    public void SetUp()
    {
        TorznabClient.ClearCapabilitiesCache();
        this.configService = Substitute.For<IConfigService>();
        this.indexerRepository = Substitute.For<IIndexerRepository>();
        this.arrRepository = Substitute.For<IArrConnectionRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        TorznabClient.ClearCapabilitiesCache();
    }

    #region XML Capabilities Parsing

    [Test]
    public void ParseCapabilitiesXml_ExtractsLimitsAndSearchingModes()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <server version=""1.0"" title=""TestTracker""/>
    <limits default=""30"" max=""150""/>
    <searching>
        <search available=""yes"" supportedParams=""q""/>
        <tv-search available=""yes"" supportedParams=""q,season,ep,tvdbid,rid""/>
        <movie-search available=""yes"" supportedParams=""q,imdbid,tmdbid""/>
        <music-search available=""yes"" supportedParams=""q,artist,album""/>
        <book-search available=""yes"" supportedParams=""q,author,isbn""/>
    </searching>
    <categories>
        <category id=""2000"" name=""Movies"">
            <subcat id=""2040"" name=""Movies/HD""/>
            <subcat id=""2045"" name=""Movies/UHD""/>
        </category>
        <category id=""5000"" name=""TV"">
            <subcat id=""5040"" name=""TV/HD""/>
        </category>
    </categories>
</caps>";

        var client = new TorznabClient(this.configService);
        var caps = client.ParseCapabilitiesXml(xml);

        caps.DefaultPageSize.Should().Be(30);
        caps.MaxPageSize.Should().Be(150);
        caps.SupportsSearch.Should().BeTrue();
        caps.SupportsTvSearch.Should().BeTrue();
        caps.SupportsMovieSearch.Should().BeTrue();
        caps.SupportsMusicSearch.Should().BeTrue();
        caps.SupportsBookSearch.Should().BeTrue();

        caps.SupportedTvParams.Should().Contain(new[] { "q", "season", "ep", "tvdbid", "rid" });
        caps.SupportedMovieParams.Should().Contain(new[] { "q", "imdbid", "tmdbid" });
        caps.SupportedMusicParams.Should().Contain(new[] { "q", "artist", "album" });
        caps.SupportedBookParams.Should().Contain(new[] { "q", "author", "isbn" });

        caps.Categories.Should().HaveCount(2);
        var movieCategory = caps.Categories.First(c => c.Id == 2000);
        movieCategory.Name.Should().Be("Movies");
        movieCategory.SubCategories.Should().HaveCount(2);
        movieCategory.SubCategories.Select(s => s.Id).Should().Contain(new[] { 2040, 2045 });

        var tvCategory = caps.Categories.First(c => c.Id == 5000);
        tvCategory.Name.Should().Be("TV");
        tvCategory.SubCategories.Should().HaveCount(1);
        tvCategory.SubCategories[0].Id.Should().Be(5040);
        tvCategory.SubCategories[0].Name.Should().Be("TV/HD");
    }

    [Test]
    public void ParseCapabilitiesXml_WhenXmlEmptyOrMalformed_ReturnsDefaultCapabilitiesGracefully()
    {
        var client = new TorznabClient(this.configService);

        var emptyCaps = client.ParseCapabilitiesXml(string.Empty);
        emptyCaps.Should().NotBeNull();
        emptyCaps.Categories.Should().BeEmpty();
        emptyCaps.DefaultPageSize.Should().Be(50);
        emptyCaps.MaxPageSize.Should().Be(100);

        var malformedCaps = client.ParseCapabilitiesXml("<caps><notClosed");
        malformedCaps.Should().NotBeNull();
        malformedCaps.Categories.Should().BeEmpty();
    }

    [Test]
    public async Task FetchCapabilitiesAsync_CachesResult_AndDoesNotRepeatHttpCall()
    {
        var requestCount = 0;
        var capsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <limits default=""40"" max=""120""/>
    <searching>
        <search available=""yes""/>
    </searching>
</caps>";

        var handler = new MockHttpMessageHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(capsXml),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Id = 10,
            Name = "CachedTracker",
            Url = "https://cached-tracker.org/api",
            ApiKey = "secret123",
        };

        var firstCaps = await client.FetchCapabilitiesAsync(indexer);
        var secondCaps = await client.FetchCapabilitiesAsync(indexer);

        firstCaps.DefaultPageSize.Should().Be(40);
        secondCaps.DefaultPageSize.Should().Be(40);
        requestCount.Should().Be(1, "Subsequent capabilities fetch for the same tracker should be served from cache");
    }

    [Test]
    public async Task InvalidateCapabilities_EvictsCachedCapabilities()
    {
        var requestCount = 0;
        var capsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <limits default=""40"" max=""120""/>
</caps>";

        var handler = new MockHttpMessageHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(capsXml),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Id = 11,
            Name = "InvalidateTracker",
            Url = "https://invalidate-tracker.org/api",
            ApiKey = "secret456",
        };

        await client.FetchCapabilitiesAsync(indexer);
        requestCount.Should().Be(1);

        TorznabClient.InvalidateCapabilities(indexer.Url, indexer.ApiKey);

        await client.FetchCapabilitiesAsync(indexer);
        requestCount.Should().Be(2, "Invalidating capabilities should trigger a fresh HTTP call on next fetch");
    }

    #endregion

    #region Search Request Generation

    [Test]
    public async Task SearchAsync_BuildsTvSearchUrl_WhenSeasonOrEpSpecified()
    {
        HttpRequestMessage capturedRequest = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"<rss version=""2.0""><channel></channel></rss>"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "TvTracker",
            Url = "https://indexer.tv/api",
            ApiKey = "tvkey",
        };

        var criteria = new TorznabSearchCriteria
        {
            Query = "Severance",
            Season = 2,
            Ep = 1,
            TvdbId = "371980",
            Rid = "45678",
            Limit = 25,
            Offset = 0,
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;

        query.Should().Contain("t=tvsearch");
        query.Should().Contain("q=Severance");
        query.Should().Contain("season=2");
        query.Should().Contain("ep=1");
        query.Should().Contain("tvdbid=371980");
        query.Should().Contain("rid=45678");
        query.Should().Contain("apikey=tvkey");
        query.Should().Contain("limit=25");
        query.Should().Contain("offset=0");
    }

    [Test]
    public async Task SearchAsync_BuildsMovieSearchUrl_StrippingTtPrefixFromImdbId()
    {
        HttpRequestMessage capturedRequest = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"<rss version=""2.0""><channel></channel></rss>"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Id = 2,
            Name = "MovieTracker",
            Url = "https://indexer.movies/api",
            ApiKey = "moviekey",
        };

        var criteria = new TorznabSearchCriteria
        {
            Query = "Dune",
            ImdbId = "tt15239678",
            TmdbId = "693134",
            Year = 2024,
            Limit = 50,
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;

        query.Should().Contain("t=movie");
        query.Should().Contain("q=Dune");
        query.Should().Contain("imdbid=15239678", "The 'tt' prefix must be stripped from the IMDB ID");
        query.Should().Contain("tmdbid=693134");
        query.Should().Contain("year=2024");
    }

    [Test]
    public async Task SearchAsync_BuildsMusicSearchUrl_WhenArtistOrAlbumSpecified()
    {
        HttpRequestMessage capturedRequest = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"<rss version=""2.0""><channel></channel></rss>"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Id = 3,
            Name = "MusicTracker",
            Url = "https://indexer.music/api",
        };

        var criteria = new TorznabSearchCriteria
        {
            Artist = "Pink Floyd",
            Album = "The Dark Side of the Moon",
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;

        query.Should().Contain("t=music");
        query.Should().Contain("artist=Pink%20Floyd");
        query.Should().Contain("album=The%20Dark%20Side%20of%20the%20Moon");
    }

    [Test]
    public async Task SearchAsync_BuildsBookSearchUrl_WhenAuthorOrIsbnSpecified()
    {
        HttpRequestMessage capturedRequest = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"<rss version=""2.0""><channel></channel></rss>"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Id = 4,
            Name = "BookTracker",
            Url = "https://indexer.books/api",
        };

        var criteria = new TorznabSearchCriteria
        {
            Author = "Isaac Asimov",
            Isbn = "9780553293357",
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;

        query.Should().Contain("t=book");
        query.Should().Contain("author=Isaac%20Asimov");
        query.Should().Contain("isbn=9780553293357");
    }

    [Test]
    public async Task SearchAsync_ExpandsParentCategoryHierarchy_AndRespectsCustomSearchType()
    {
        HttpRequestMessage capturedRequest = null!;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"<rss version=""2.0""><channel></channel></rss>"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Id = 5,
            Name = "CustomTracker",
            Url = "https://indexer.custom/api",
        };

        var criteria = new TorznabSearchCriteria
        {
            Query = "Debian",
            CategoryId = 4000, // PC / Apps parent category
            SearchType = "custom-special",
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;

        query.Should().Contain("t=custom-special", "Explicit searchType should override automatic mode detection");
        query.Should().Contain("cat=4000%2c4010%2c4020%2c4030%2c4040%2c4050%2c4060%2c4070");
    }

    #endregion

    #region Result Pagination

    [Test]
    public void ResolveEffectiveLimit_RespectsCapabilitiesAndConfigClamp()
    {
        this.configService.TorznabDefaultPageSize.Returns(60);
        this.configService.TorznabMaxPageSize.Returns(100);

        var client = new TorznabClient(this.configService);
        var indexer = new IndexerDefinition
        {
            Url = "https://tracker.org/api",
            ApiKey = "key",
        };

        // 1. When limit <= 0, falls back to config default page size (60)
        client.ResolveEffectiveLimit(indexer, 0).Should().Be(60);

        // 2. When limit exceeds config TorznabMaxPageSize (150 > 100), clamps to 100
        client.ResolveEffectiveLimit(indexer, 150).Should().Be(100);

        // 3. When capabilities cache has max page size of 75, clamps to 75
        var capsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <limits default=""25"" max=""75""/>
</caps>";
        var caps = client.ParseCapabilitiesXml(capsXml);

        // Seed capabilities into client cache via FetchCapabilitiesAsync
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(capsXml),
        });
        using var httpClient = new HttpClient(handler);
        var clientWithHttp = new TorznabClient(null, httpClient, this.configService);
        _ = clientWithHttp.FetchCapabilitiesAsync(indexer).GetAwaiter().GetResult();

        clientWithHttp.ResolveEffectiveLimit(indexer, 90).Should().Be(75);
    }

    [Test]
    public void ParseTorznabFeedXml_ExtractsPaginationAttributes_ResponseTotalAndOffset()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>Paged Feed</title>
        <response offset=""50"" total=""250""/>
        <item>
            <title>Item.Page.Two.2024</title>
            <guid>https://indexer.local/details/200</guid>
            <link>https://indexer.local/download/200.torrent</link>
            <size>1048576</size>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        results[0].ResponseOffset.Should().Be(50);
        results[0].ResponseTotal.Should().Be(250);
    }

    #endregion

    #region Magnet vs Torrent URL Parsing

    [Test]
    public void ParseTorznabFeedXml_ExtractsTorrentUrl_FromEnclosure()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <item>
            <title>Linux.Distro.ISO</title>
            <guid>distro-1</guid>
            <link>https://tracker.org/details/distro-1</link>
            <enclosure url=""https://tracker.org/download/distro-1.torrent"" length=""2147483648"" type=""application/x-bittorrent""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        results[0].DownloadUrl.Should().Be("https://tracker.org/download/distro-1.torrent");
        results[0].Size.Should().Be(2147483648L);
    }

    [Test]
    public void ParseTorznabFeedXml_ExtractsMagnetUrl_FromTorznabAttributeAndDescriptionFallback()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Release.With.Magnet.Attr</title>
            <guid>item-1</guid>
            <link>https://tracker.org/details/1</link>
            <torznab:attr name=""magneturl"" value=""magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&amp;dn=Release1""/>
            <torznab:attr name=""infohash"" value=""0123456789abcdef0123456789abcdef01234567""/>
            <torznab:attr name=""seeders"" value=""10""/>
            <torznab:attr name=""peers"" value=""15""/>
        </item>
        <item>
            <title>Release.With.Magnet.In.Description</title>
            <guid>item-2</guid>
            <link>https://tracker.org/details/2</link>
            <description>Direct link: magnet:?xt=urn:btih:fedcba9876543210fedcba9876543210fedcba98&amp;dn=Release2 for downloading</description>
            <torznab:attr name=""seeders"" value=""5""/>
            <torznab:attr name=""peers"" value=""7""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(2);

        var first = results[0];
        first.MagnetUrl.Should().StartWith("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567");
        first.InfoHash.Should().Be("0123456789abcdef0123456789abcdef01234567");
        first.Seeders.Should().Be(10);
        first.Leechers.Should().Be(5); // peers (15) - seeders (10)

        var second = results[1];
        second.MagnetUrl.Should().StartWith("magnet:?xt=urn:btih:fedcba9876543210fedcba9876543210fedcba98");
        second.InfoHash.Should().Be("fedcba9876543210fedcba9876543210fedcba98");
    }

    [Test]
    public void ParseTorznabFeedXml_HandlesVolumeFactors_FreeleechAndMinSeedersFiltering()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Freeleech.Release.2024</title>
            <guid>fl-1</guid>
            <link>https://tracker.org/download/fl-1.torrent</link>
            <torznab:attr name=""downloadvolumefactor"" value=""0""/>
            <torznab:attr name=""uploadvolumefactor"" value=""2.0""/>
            <torznab:attr name=""seeders"" value=""20""/>
        </item>
        <item>
            <title>Normal.Release.2024</title>
            <guid>norm-1</guid>
            <link>https://tracker.org/download/norm-1.torrent</link>
            <torznab:attr name=""downloadvolumefactor"" value=""1.0""/>
            <torznab:attr name=""uploadvolumefactor"" value=""1.0""/>
            <torznab:attr name=""seeders"" value=""3""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);

        // When FreeleechOnly = true and MinSeeders = 5
        var strictIndexer = new IndexerDefinition
        {
            FreeleechOnly = true,
            MinSeeders = 5,
        };

        var filteredResults = client.ParseTorznabFeedXml(xml, strictIndexer);
        filteredResults.Should().HaveCount(1);
        filteredResults[0].Title.Should().Be("Freeleech.Release.2024");
        filteredResults[0].IsFreeleech.Should().BeTrue();
        filteredResults[0].UploadVolumeFactor.Should().Be(2.0);
    }

    #endregion

    #region Rate Limiting & HTTP Error Handling

    [Test]
    public async Task Torznab_SearchAsync_ThrowsHttpRequestException_OnHttp429TooManyRequests()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = "Rate limit exceeded",
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "ThrottledTracker",
            Url = "https://throttled.tracker.org/api",
        };

        Func<Task> act = async () => await client.SearchAsync(indexer, "test");
        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Torznab_TestConnectionAsync_ReturnsFailure_OnHttp429TooManyRequests()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = "Too Many Requests",
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "RateLimitedTracker",
            Url = "https://ratelimited.org/api",
        };

        var result = await client.TestConnectionAsync(indexer);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("429");
    }

    [Test]
    public async Task Prowlarr_SyncFromProwlarrAsync_ThrowsHttpRequestException_OnHttp429TooManyRequests()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = "Too Many Requests",
        });

        using var httpClient = new HttpClient(handler);
        var prowlarrService = new ProwlarrSyncService(this.indexerRepository, httpClient);

        Func<Task> act = async () => await prowlarrService.SyncFromProwlarrAsync("http://prowlarr.local:9696", "apikey123");
        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    #endregion

    #region Error Responses & AntiBot Challenges

    [Test]
    public void Torznab_ParseTorznabFeedXml_ThrowsTorznabException_OnErrorNode()
    {
        var errorXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<error code=""200"" description=""Missing parameter: category not found"" />";

        var client = new TorznabClient(this.configService);

        Action act = () => client.ParseTorznabFeedXml(errorXml);
        var ex = act.Should().Throw<TorznabException>().Which;

        ex.Code.Should().Be(200);
        ex.ErrorCode.Should().Be("200");
        ex.Description.Should().Be("Missing parameter: category not found");
        ex.Message.Should().Contain("200");
    }

    [Test]
    public async Task Torznab_TestConnectionAsync_ReturnsFail_WhenXmlContainsError()
    {
        var errorCapsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<error code=""100"" description=""Invalid API Key"" />";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(errorCapsXml),
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "ErrorTracker",
            Url = "https://errortracker.org/api",
            ApiKey = "badkey",
        };

        var result = await client.TestConnectionAsync(indexer);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("100");
        result.ErrorMessage.Should().Contain("Invalid API Key");
    }

    [Test]
    public async Task Torznab_TestConnectionAsync_ReturnsFail_WhenAntiBotChallengeDetected()
    {
        var challengeHtml = @"<!DOCTYPE html>
<html>
<head><title>Just a moment... Attention Required! | Cloudflare</title></head>
<body>Please enable cookies and turn off Adblock</body>
</html>";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(challengeHtml),
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "CloudflareProtectedTracker",
            Url = "https://cf-tracker.org/api",
        };

        var result = await client.TestConnectionAsync(indexer);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cloudflare");
    }

    [Test]
    public async Task Torznab_TestConnectionAsync_ReturnsFail_WhenEmptyResponseReceived()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty),
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "EmptyTracker",
            Url = "https://emptytracker.org/api",
        };

        var result = await client.TestConnectionAsync(indexer);
        result.Success.Should().BeFalse();
    }

    #endregion

    #region Authentication Failures

    [Test]
    public async Task Torznab_SearchAsync_ThrowsHttpRequestException_OnHttp401Unauthorized()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            ReasonPhrase = "Unauthorized",
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "AuthFailTracker",
            Url = "https://authfail.org/api",
            ApiKey = "wrongKey",
        };

        Func<Task> act = async () => await client.SearchAsync(indexer, "test");
        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Torznab_TestConnectionAsync_ReturnsFailure_OnHttp403Forbidden()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            ReasonPhrase = "Forbidden",
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "ForbiddenTracker",
            Url = "https://forbidden.org/api",
        };

        var result = await client.TestConnectionAsync(indexer);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("403");
    }

    [Test]
    public async Task Prowlarr_SyncFromProwlarrAsync_ThrowsHttpRequestException_OnHttp401Unauthorized()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            ReasonPhrase = "Unauthorized",
        });

        using var httpClient = new HttpClient(handler);
        var prowlarrService = new ProwlarrSyncService(this.indexerRepository, httpClient);

        Func<Task> act = async () => await prowlarrService.SyncFromProwlarrAsync("http://prowlarr.local:9696", "wrongApiKey");
        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Prowlarr_SyncFromProwlarrAsync_PassesXApiKeyHeader_AndDeserializesIndexers()
    {
        HttpRequestMessage capturedRequest = null!;
        var prowlarrJson = @"[
            {
                ""id"": 42,
                ""name"": ""Prowlarr Torrent Track"",
                ""implementation"": ""Cardigann"",
                ""enable"": true,
                ""priority"": 15,
                ""protocol"": ""torrent"",
                ""categories"": [2000, 5000]
            }
        ]";

        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(prowlarrJson),
            };
        });

        using var httpClient = new HttpClient(handler);
        var prowlarrService = new ProwlarrSyncService(this.indexerRepository, httpClient);

        this.indexerRepository.All().Returns(new List<IndexerDefinition>());

        var count = await prowlarrService.SyncFromProwlarrAsync("http://prowlarr.local:9696", "my-super-secret-api-key");

        count.Should().Be(1);
        capturedRequest.Should().NotBeNull();
        capturedRequest.Headers.Contains("X-Api-Key").Should().BeTrue();
        capturedRequest.Headers.GetValues("X-Api-Key").First().Should().Be("my-super-secret-api-key");

        this.indexerRepository.Received(1).Insert(Arg.Is<IndexerDefinition>(i =>
            i.Name == "Prowlarr Torrent Track" &&
            i.Implementation == "Cardigann" &&
            i.ProwlarrIndexerId == 42 &&
            i.Priority == 15 &&
            i.Enable == true &&
            i.Categories.Contains(2000) &&
            i.Categories.Contains(5000)));
    }

    #endregion

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

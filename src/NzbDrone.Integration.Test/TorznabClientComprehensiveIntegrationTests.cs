// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TorznabClientComprehensiveIntegrationTests : IntegrationTestBase
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> syncHandler)
        {
            this.handler = (req, _) => Task.FromResult(syncHandler(req));
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> asyncHandler)
        {
            this.handler = asyncHandler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return this.handler(request, cancellationToken);
        }
    }

    private IConfigService configService = null!;

    [SetUp]
    public void SetUp()
    {
        TorznabClient.ClearCapabilitiesCache();
        this.configService = Substitute.For<IConfigService>();
    }

    [TearDown]
    public void TearDown()
    {
        TorznabClient.ClearCapabilitiesCache();
    }

    #region 1. Capabilities Querying

    [Test]
    public async Task FetchCapabilitiesAsync_CapsEndpoint_BuildsCorrectUrlAndApiKey()
    {
        HttpRequestMessage capturedRequest = null!;
        const string sampleCaps = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <server version=""1.2.3"" title=""TestIndexer"" retention=""1500""/>
    <limits default=""40"" max=""150""/>
    <searching>
        <search available=""yes"" supportedParams=""q""/>
    </searching>
</caps>";

        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sampleCaps, Encoding.UTF8, "application/xml"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Id = 1,
            Name = "CapsTracker",
            Url = "https://tracker.example.com/torznab/api",
            ApiKey = "secret-caps-api-key",
        };

        var caps = await client.FetchCapabilitiesAsync(indexer);

        capturedRequest.Should().NotBeNull();
        capturedRequest.RequestUri!.ToString().Should().Be("https://tracker.example.com/torznab/api?t=caps&apikey=secret-caps-api-key");
        caps.DefaultPageSize.Should().Be(40);
        caps.MaxPageSize.Should().Be(150);
        caps.SupportsSearch.Should().BeTrue();
    }

    [Test]
    public void ParseCapabilitiesXml_ServerInfoLimitsAndRetention_ParsesPageSizes()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <server version=""2.0"" title=""PremiumTracker"" retention=""2500""/>
    <limits default=""35"" max=""200""/>
    <retention days=""2500""/>
</caps>";

        var client = new TorznabClient(this.configService);
        var caps = client.ParseCapabilitiesXml(xml);

        caps.DefaultPageSize.Should().Be(35);
        caps.MaxPageSize.Should().Be(200);
    }

    [Test]
    public void ParseCapabilitiesXml_ServerElementFallback_ParsesLimitsWhenLimitsTagMissing()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <server version=""1.0"" title=""OldTracker"" default=""25"" max=""75""/>
</caps>";

        var client = new TorznabClient(this.configService);
        var caps = client.ParseCapabilitiesXml(xml);

        caps.DefaultPageSize.Should().Be(25);
        caps.MaxPageSize.Should().Be(75);
    }

    [Test]
    public void ParseCapabilitiesXml_SupportedSearchModes_ParsesAllModesAndParameters()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <limits default=""50"" max=""100""/>
    <searching>
        <search available=""yes"" supportedParams=""q""/>
        <tv-search available=""yes"" supportedParams=""q,season,ep,tvdbid,rid""/>
        <movie-search available=""yes"" supportedParams=""q,imdbid,tmdbid""/>
        <music-search available=""yes"" supportedParams=""q,artist,album""/>
        <book-search available=""no"" supportedParams=""q,author,isbn""/>
    </searching>
</caps>";

        var client = new TorznabClient(this.configService);
        var caps = client.ParseCapabilitiesXml(xml);

        caps.SupportsSearch.Should().BeTrue();
        caps.SupportsTvSearch.Should().BeTrue();
        caps.SupportsMovieSearch.Should().BeTrue();
        caps.SupportsMusicSearch.Should().BeTrue();
        caps.SupportsBookSearch.Should().BeFalse();

        caps.SupportedTvParams.Should().Contain(new[] { "q", "season", "ep", "tvdbid", "rid" });
        caps.SupportedMovieParams.Should().Contain(new[] { "q", "imdbid", "tmdbid" });
        caps.SupportedMusicParams.Should().Contain(new[] { "q", "artist", "album" });
        caps.SupportedBookParams.Should().Contain(new[] { "q", "author", "isbn" });
    }

    [Test]
    public void ParseCapabilitiesXml_AlternativeModeTagNames_ParsesTvsearchMoviesearchAndAudiosearch()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <searching>
        <tvsearch available=""1"" supportedParams=""q,season,ep""/>
        <moviesearch available=""true"" supportedParams=""q,imdbid""/>
        <audiosearch available=""yes"" supportedParams=""q,artist""/>
        <booksearch available=""1"" supportedParams=""q,author""/>
    </searching>
</caps>";

        var client = new TorznabClient(this.configService);
        var caps = client.ParseCapabilitiesXml(xml);

        caps.SupportsTvSearch.Should().BeTrue();
        caps.SupportsMovieSearch.Should().BeTrue();
        caps.SupportsMusicSearch.Should().BeTrue();
        caps.SupportsBookSearch.Should().BeTrue();

        caps.SupportedTvParams.Should().Contain(new[] { "q", "season", "ep" });
        caps.SupportedMovieParams.Should().Contain(new[] { "q", "imdbid" });
        caps.SupportedMusicParams.Should().Contain(new[] { "q", "artist" });
        caps.SupportedBookParams.Should().Contain(new[] { "q", "author" });
    }

    [Test]
    public void ParseCapabilitiesXml_CategoriesHierarchy_ParsesCategoriesAndSubcategories()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <categories>
        <category id=""2000"" name=""Movies"">
            <subcat id=""2010"" name=""Movies/Foreign""/>
            <subcat id=""2040"" name=""Movies/HD""/>
            <subcat id=""2045"" name=""Movies/UHD""/>
        </category>
        <category id=""5000"" name=""TV"">
            <subcategory id=""5030"" name=""TV/SD""/>
            <subcategory id=""5040"" name=""TV/HD""/>
        </category>
        <category id=""-1"" name=""InvalidCat""/>
    </categories>
</caps>";

        var client = new TorznabClient(this.configService);
        var caps = client.ParseCapabilitiesXml(xml);

        caps.Categories.Should().HaveCount(2);

        var movieCat = caps.Categories.First(c => c.Id == 2000);
        movieCat.Name.Should().Be("Movies");
        movieCat.SubCategories.Should().HaveCount(3);
        movieCat.SubCategories.Select(s => s.Id).Should().Contain(new[] { 2010, 2040, 2045 });

        var tvCat = caps.Categories.First(c => c.Id == 5000);
        tvCat.Name.Should().Be("TV");
        tvCat.SubCategories.Should().HaveCount(2);
        tvCat.SubCategories.Select(s => s.Id).Should().Contain(new[] { 5030, 5040 });
    }

    [Test]
    public void ParseCapabilitiesXml_CategoryWithChildElements_ParsesIdAndName()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <categories>
        <category>
            <id>3000</id>
            <name>Audio</name>
            <subcat>
                <id>3010</id>
                <name>Audio/MP3</name>
            </subcat>
        </category>
    </categories>
</caps>";

        var client = new TorznabClient(this.configService);
        var caps = client.ParseCapabilitiesXml(xml);

        caps.Categories.Should().HaveCount(1);
        caps.Categories[0].Id.Should().Be(3000);
        caps.Categories[0].Name.Should().Be("Audio");
        caps.Categories[0].SubCategories.Should().HaveCount(1);
        caps.Categories[0].SubCategories[0].Id.Should().Be(3010);
        caps.Categories[0].SubCategories[0].Name.Should().Be("Audio/MP3");
    }

    [Test]
    public async Task FetchCapabilitiesAsync_CachesResult_AndEvictsOnInvalidate()
    {
        var requestCount = 0;
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <limits default=""45"" max=""90""/>
</caps>";

        var handler = new MockHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Id = 12,
            Name = "CacheTestTracker",
            Url = "https://cachetest.org/torznab",
            ApiKey = "key1",
        };

        var first = await client.FetchCapabilitiesAsync(indexer);
        var second = await client.FetchCapabilitiesAsync(indexer);

        first.DefaultPageSize.Should().Be(45);
        second.DefaultPageSize.Should().Be(45);
        requestCount.Should().Be(1);

        // Invalidate specific URL and ApiKey
        TorznabClient.InvalidateCapabilities(indexer.Url, indexer.ApiKey);

        var third = await client.FetchCapabilitiesAsync(indexer);
        third.DefaultPageSize.Should().Be(45);
        requestCount.Should().Be(2);

        // Invalidate by URL prefix only
        TorznabClient.InvalidateCapabilities(indexer.Url, null);
        var fourth = await client.FetchCapabilitiesAsync(indexer);
        fourth.DefaultPageSize.Should().Be(45);
        requestCount.Should().Be(3);
    }

    [Test]
    public async Task FetchCapabilitiesAsync_WhenIndexerOrUrlNullOrEmpty_ReturnsDefaultCapabilities()
    {
        var client = new TorznabClient(this.configService);

        var resultNull = await client.FetchCapabilitiesAsync(null!);
        resultNull.Should().NotBeNull();
        resultNull.DefaultPageSize.Should().Be(50);

        var resultEmptyUrl = await client.FetchCapabilitiesAsync(new IndexerDefinition { Url = "" });
        resultEmptyUrl.Should().NotBeNull();
        resultEmptyUrl.DefaultPageSize.Should().Be(50);
    }

    [Test]
    public async Task FetchCapabilitiesAsync_WhenHttpError_ReturnsDefaultCapabilitiesGracefully()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Url = "https://broken-caps.org/api",
            ApiKey = "any",
        };

        var caps = await client.FetchCapabilitiesAsync(indexer);
        caps.Should().NotBeNull();
        caps.Categories.Should().BeEmpty();
    }

    [Test]
    public async Task FetchCapabilitiesAsync_ConcurrentRequests_UsesLockAndDeduplicatesCall()
    {
        var requestCount = 0;
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <limits default=""60"" max=""120""/>
</caps>";

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            Interlocked.Increment(ref requestCount);
            await Task.Delay(50, ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Url = "https://concurrent-caps.org/api",
            ApiKey = "lock-test-key",
        };

        var tasks = Enumerable.Range(0, 5).Select(_ => client.FetchCapabilitiesAsync(indexer)).ToArray();
        var results = await Task.WhenAll(tasks);

        foreach (var r in results)
        {
            r.DefaultPageSize.Should().Be(60);
        }

        requestCount.Should().Be(1);
    }

    #endregion

    #region 2. Query Building

    [Test]
    public async Task SearchAsync_TermSearch_BuildsBasicQueryWithLimitAndOffset()
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
            Url = "https://tracker.org/api",
            ApiKey = "secret1",
        };

        var criteria = new TorznabSearchCriteria
        {
            Query = "Ubuntu Desktop 24.04",
            Limit = 30,
            Offset = 15,
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=search");
        query.Should().Contain("limit=30");
        query.Should().Contain("offset=15");
        query.Should().Match(q => q.Contains("q=Ubuntu+Desktop+24.04") || q.Contains("q=Ubuntu%20Desktop%2024.04"));
        query.Should().Contain("apikey=secret1");
    }

    [Test]
    public async Task SearchAsync_ImdbId_StripsTtPrefixAndSelectsMovieSearchMode()
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

        var indexer = new IndexerDefinition { Url = "https://movies.tracker/api" };

        var criteria = new TorznabSearchCriteria
        {
            ImdbId = "tt0137523",
            TmdbId = "550",
            Year = 1999,
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=movie");
        query.Should().Contain("imdbid=0137523");
        query.Should().Contain("tmdbid=550");
        query.Should().Contain("year=1999");
    }

    [Test]
    public async Task SearchAsync_TvdbIdAndRid_SelectsTvSearchMode()
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

        var indexer = new IndexerDefinition { Url = "https://tv.tracker/api" };

        var criteria = new TorznabSearchCriteria
        {
            TvdbId = "81189",
            Rid = "24493",
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=tvsearch");
        query.Should().Contain("tvdbid=81189");
        query.Should().Contain("rid=24493");
    }

    [Test]
    public async Task SearchAsync_SeasonAndEpisode_AppendsBothParametersAndSelectsTvSearch()
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

        var indexer = new IndexerDefinition { Url = "https://tv.tracker/api" };

        var criteria = new TorznabSearchCriteria
        {
            Query = "Breaking Bad",
            Season = 5,
            Ep = 14,
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=tvsearch");
        query.Should().Contain("season=5");
        query.Should().Contain("ep=14");
    }

    [Test]
    public async Task SearchAsync_SeasonOnly_AppendsSeasonParameterWithoutEp()
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

        var indexer = new IndexerDefinition { Url = "https://tv.tracker/api" };

        var criteria = new TorznabSearchCriteria
        {
            Query = "Breaking Bad",
            Season = 3,
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=tvsearch");
        query.Should().Contain("season=3");
        query.Should().NotContain("&ep=");
    }

    [Test]
    public async Task SearchAsync_MusicSearch_SelectsMusicModeWhenArtistOrAlbumSpecified()
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

        var indexer = new IndexerDefinition { Url = "https://music.tracker/api" };

        var criteria = new TorznabSearchCriteria
        {
            Artist = "Radiohead",
            Album = "OK Computer",
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=music");
        query.Should().Contain("artist=Radiohead");
        query.Should().Match(q => q.Contains("album=OK+Computer") || q.Contains("album=OK%20Computer"));
    }

    [Test]
    public async Task SearchAsync_BookSearch_SelectsBookModeWhenAuthorOrIsbnSpecified()
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

        var indexer = new IndexerDefinition { Url = "https://books.tracker/api" };

        var criteria = new TorznabSearchCriteria
        {
            Author = "Frank Herbert",
            Isbn = "9780441172719",
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=book");
        query.Should().Match(q => q.Contains("author=Frank+Herbert") || q.Contains("author=Frank%20Herbert"));
        query.Should().Contain("isbn=9780441172719");
    }

    [Test]
    public async Task SearchAsync_ExplicitSearchType_OverridesAutoDetection()
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

        var indexer = new IndexerDefinition { Url = "https://custom.tracker/api" };

        var criteria = new TorznabSearchCriteria
        {
            Query = "Some Movie",
            ImdbId = "tt1234567",
            SearchType = "special-search",
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=special-search");
    }

    [Test]
    public async Task SearchAsync_TvCategory_SwitchesImdbOrTmdbSearchToTvSearch()
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

        var indexer = new IndexerDefinition { Url = "https://tracker.org/api" };

        // Test with criteria.Categories containing a TV category (5000-5999)
        var criteriaWithCats = new TorznabSearchCriteria
        {
            ImdbId = "tt0903747",
            Categories = new List<int> { 5040 },
        };

        await client.SearchAsync(indexer, criteriaWithCats);
        capturedRequest.RequestUri!.Query.Should().Contain("t=tvsearch");

        // Test with criteria.CategoryId being a TV category
        var criteriaWithSingleCat = new TorznabSearchCriteria
        {
            ImdbId = "tt0903747",
            CategoryId = 5000,
        };

        await client.SearchAsync(indexer, criteriaWithSingleCat);
        capturedRequest.RequestUri!.Query.Should().Contain("t=tvsearch");

        // Test with indexer configured solely with TV categories
        var tvIndexer = new IndexerDefinition
        {
            Url = "https://tracker.org/api",
            Categories = new List<int> { 5000, 5040 },
        };

        var criteriaWithNoCats = new TorznabSearchCriteria
        {
            ImdbId = "tt0903747",
        };

        await client.SearchAsync(tvIndexer, criteriaWithNoCats);
        capturedRequest.RequestUri!.Query.Should().Contain("t=tvsearch");
    }

    [Test]
    public async Task SearchAsync_CategoryFiltering_ExpandsHierarchyWhenParentCategorySpecified()
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

        var indexer = new IndexerDefinition { Url = "https://tracker.org/api" };

        // 2000 is Movies parent category in CategoryHierarchy
        var criteria = new TorznabSearchCriteria
        {
            CategoryId = 2000,
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        // Should expand to 2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060, 2070, 2080, 2090
        query.Should().Contain("cat=2000%2c2010%2c2020%2c2030%2c2040%2c2045%2c2050%2c2060%2c2070%2c2080%2c2090");
    }

    [Test]
    public async Task SearchAsync_CategoryFiltering_UsesSingleCategoryWhenNotInHierarchy()
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

        var indexer = new IndexerDefinition { Url = "https://tracker.org/api" };

        var criteria = new TorznabSearchCriteria
        {
            CategoryId = 9999,
        };

        await client.SearchAsync(indexer, criteria);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("cat=9999");
    }

    [Test]
    public async Task SearchAsync_CategoryFiltering_PassesMultipleCategoriesAndFallsBackToIndexerCategories()
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
            Url = "https://tracker.org/api",
            Categories = new List<int> { 8000 },
        };

        // When criteria has explicit categories
        var criteria = new TorznabSearchCriteria
        {
            Categories = new List<int> { 2040, 2045, 2040 }, // test deduplication
        };

        await client.SearchAsync(indexer, criteria);
        capturedRequest.RequestUri!.Query.Should().Contain("cat=2040%2c2045");

        // When criteria categories are empty, falls back to indexer categories (expanded via hierarchy)
        var fallbackCriteria = new TorznabSearchCriteria();
        await client.SearchAsync(indexer, fallbackCriteria);
        // 8000 expands to 8000, 8010, 8020
        capturedRequest.RequestUri!.Query.Should().Contain("cat=8000%2c8010%2c8020");
    }

    [Test]
    public async Task SearchAsync_PreservesExistingQueryParamsInIndexerUrl()
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
            Url = "https://tracker.org/api?customAuth=true&staticTag=nzb",
            ApiKey = "key1",
        };

        await client.SearchAsync(indexer, "test-term");

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("customAuth=true");
        query.Should().Contain("staticTag=nzb");
        query.Should().Contain("apikey=key1");
        query.Should().Contain("q=test-term");
    }

    [Test]
    public async Task SearchAsync_FlatParameterOverload_MapsAllParametersToCriteria()
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

        var indexer = new IndexerDefinition { Url = "https://tracker.org/api" };

        await client.SearchAsync(
            indexer,
            query: "The Matrix",
            categoryId: 2000,
            limit: 20,
            offset: 10,
            season: null,
            ep: null,
            imdbId: "tt0133093",
            tmdbId: "603",
            searchType: null,
            tvdbId: null,
            rid: null,
            year: 1999,
            artist: null,
            album: null,
            author: null,
            isbn: null);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=movie");
        query.Should().Match(q => q.Contains("q=The+Matrix") || q.Contains("q=The%20Matrix"));
        query.Should().Contain("limit=20");
        query.Should().Contain("offset=10");
        query.Should().Contain("imdbid=0133093");
        query.Should().Contain("tmdbid=603");
        query.Should().Contain("year=1999");
    }

    [Test]
    public async Task FetchRssAsync_BuildsSearchQueryWithLimitAndCategories()
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
            Url = "https://rss.tracker/api",
            ApiKey = "rsskey",
            Categories = new List<int> { 2040 },
        };

        await client.FetchRssAsync(indexer, limit: 30);

        capturedRequest.Should().NotBeNull();
        var query = capturedRequest.RequestUri!.Query;
        query.Should().Contain("t=search");
        query.Should().Contain("limit=30");
        query.Should().Contain("apikey=rsskey");
        query.Should().Contain("cat=2040");
    }

    [Test]
    public void ResolveEffectiveLimit_RespectsCapsLimitsAndConfigService()
    {
        this.configService.TorznabDefaultPageSize.Returns(40);
        this.configService.TorznabMaxPageSize.Returns(80);

        var client = new TorznabClient(this.configService);
        var indexer = new IndexerDefinition { Url = "https://limits.tracker/api" };

        // Zero limit falls back to TorznabDefaultPageSize (40)
        client.ResolveEffectiveLimit(indexer, 0).Should().Be(40);

        // Limit exceeding config TorznabMaxPageSize (100 > 80) is clamped to 80
        client.ResolveEffectiveLimit(indexer, 100).Should().Be(80);

        // Standard limit within range
        client.ResolveEffectiveLimit(indexer, 25).Should().Be(25);
    }

    #endregion

    #region 3. XML Torznab Feed Parsing

    [Test]
    public void ParseTorznabFeedXml_ChannelTitleAndEnclosure_ParsesUrlLengthAndType()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>Tracker Feed Title</title>
        <item>
            <title>The.Great.Movie.2024.1080p</title>
            <guid>https://tracker.org/details/5001</guid>
            <link>https://tracker.org/details/5001</link>
            <comments>https://tracker.org/comments/5001</comments>
            <description>A high quality movie release</description>
            <pubDate>Sun, 15 Mar 2026 18:30:00 +0000</pubDate>
            <enclosure url=""https://tracker.org/download/5001.torrent"" length=""5368709120"" type=""application/x-bittorrent""/>
            <torznab:attr name=""seeders"" value=""42""/>
            <torznab:attr name=""peers"" value=""55""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        var item = results[0];
        item.Title.Should().Be("The.Great.Movie.2024.1080p");
        item.Guid.Should().Be("https://tracker.org/details/5001");
        item.DownloadUrl.Should().Be("https://tracker.org/download/5001.torrent");
        item.Size.Should().Be(5368709120L);
        item.Description.Should().Be("A high quality movie release");
        item.DetailsUrl.Should().Be("https://tracker.org/comments/5001");
        item.Seeders.Should().Be(42);
        item.Peers.Should().Be(55);
        item.Leechers.Should().Be(13); // 55 - 42
    }

    [Test]
    public void ParseTorznabFeedXml_LinkFallback_WhenEnclosureMissing()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <item>
            <title>Audio.Release.FLAC</title>
            <id>audio-123</id>
            <link>https://audio.tracker/get/123.torrent</link>
            <size>450000000</size>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Audio.Release.FLAC");
        results[0].Guid.Should().Be("audio-123");
        results[0].DownloadUrl.Should().Be("https://audio.tracker/get/123.torrent");
        results[0].Size.Should().Be(450000000L);
    }

    [Test]
    public void ParseTorznabFeedXml_TorznabAttrElements_ParsesAllStandardTorznabAttrs()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Multi.Attr.Release.2026</title>
            <guid>mar-1</guid>
            <link>https://tracker.org/dl/1.torrent</link>
            <torznab:attr name=""seeders"" value=""70""/>
            <torznab:attr name=""peers"" value=""85""/>
            <torznab:attr name=""infohash"" value=""aabbccddeeff00112233445566778899aabbccdd""/>
            <torznab:attr name=""size"" value=""3221225472""/>
            <torznab:attr name=""genre"" value=""Action, Sci-Fi""/>
            <torznab:attr name=""imdb"" value=""tt1234567""/>
            <torznab:attr name=""files"" value=""4""/>
            <torznab:attr name=""grabs"" value=""150""/>
            <torznab:attr name=""minimumratio"" value=""1.25""/>
            <torznab:attr name=""minimumseedtime"" value=""259200""/>
            <torznab:attr name=""downloadvolumefactor"" value=""0.5""/>
            <torznab:attr name=""uploadvolumefactor"" value=""1.5""/>
            <torznab:attr name=""category"" value=""2000,2040""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        var r = results[0];
        r.Seeders.Should().Be(70);
        r.Peers.Should().Be(85);
        r.Leechers.Should().Be(15);
        r.InfoHash.Should().Be("aabbccddeeff00112233445566778899aabbccdd");
        r.Size.Should().Be(3221225472L);
        r.MinimumRatio.Should().Be(1.25);
        r.MinimumSeedTime.Should().Be(259200L);
        r.DownloadVolumeFactor.Should().Be(0.5);
        r.UploadVolumeFactor.Should().Be(1.5);
        r.Category.Should().Contain("2000");
        r.Category.Should().Contain("2040");
    }

    [Test]
    public void ParseTorznabFeedXml_NewznabAndUnprefixedAttrElements_ParsesCorrectly()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:newznab=""http://www.newznab.com/DTD/2010/feeds/attributes/"">
    <channel>
        <item>
            <title>Newznab.Feed.Item</title>
            <guid>nzb-1</guid>
            <link>https://tracker.org/nzb/1</link>
            <newznab:attr name=""seeders"" value=""12""/>
            <newznab:attr name=""leechers"" value=""4""/>
            <attr name=""downloadvolumefactor"" value=""0""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        var r = results[0];
        r.Seeders.Should().Be(12);
        r.Leechers.Should().Be(4);
        r.Peers.Should().Be(16);
        r.IsFreeleech.Should().BeTrue();
    }

    [Test]
    public void ParseTorznabFeedXml_FreeleechFlag_SetsZeroDownloadVolumeFactor()
    {
        // Tests <freeleech>1</freeleech>, <torznab:freeleech>true</torznab:freeleech>, and <torznab:attr name="freeleech" value="yes"/>
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Item.One</title>
            <guid>1</guid>
            <link>http://tracker/1</link>
            <freeleech>1</freeleech>
        </item>
        <item>
            <title>Item.Two</title>
            <guid>2</guid>
            <link>http://tracker/2</link>
            <torznab:freeleech>true</torznab:freeleech>
        </item>
        <item>
            <title>Item.Three</title>
            <guid>3</guid>
            <link>http://tracker/3</link>
            <torznab:attr name=""freeleech"" value=""yes""/>
        </item>
        <item>
            <title>Item.Four</title>
            <guid>4</guid>
            <link>http://tracker/4</link>
            <torznab:attr name=""freeleech"" value=""free""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(4);
        results.All(r => r.IsFreeleech).Should().BeTrue();
        results.All(r => r.DownloadVolumeFactor == 0.0).Should().BeTrue();
    }

    [Test]
    public void ParseTorznabFeedXml_PeerAndSeederCalculations_ResolvesMissingPeersOrLeechers()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Only.Seeders</title>
            <guid>s-only</guid>
            <link>http://tracker/s</link>
            <torznab:attr name=""seeders"" value=""10""/>
        </item>
        <item>
            <title>Only.Leechers</title>
            <guid>l-only</guid>
            <link>http://tracker/l</link>
            <torznab:attr name=""leechers"" value=""7""/>
        </item>
        <item>
            <title>Only.Peers</title>
            <guid>p-only</guid>
            <link>http://tracker/p</link>
            <torznab:attr name=""peers"" value=""20""/>
        </item>
        <item>
            <title>Leechers.And.Peers</title>
            <guid>lp</guid>
            <link>http://tracker/lp</link>
            <torznab:attr name=""leechers"" value=""5""/>
            <torznab:attr name=""peers"" value=""15""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(4);

        // Only seeders: peers = seeders, leechers = 0
        results[0].Seeders.Should().Be(10);
        results[0].Leechers.Should().Be(0);
        results[0].Peers.Should().Be(10);

        // Only leechers: peers = leechers, seeders = 0
        results[1].Seeders.Should().Be(0);
        results[1].Leechers.Should().Be(7);
        results[1].Peers.Should().Be(7);

        // Only peers: peers = 20, seeders = 0, leechers = 20
        results[2].Seeders.Should().Be(0);
        results[2].Leechers.Should().Be(20);
        results[2].Peers.Should().Be(20);

        // Leechers (5) and peers (15): seeders = 15 - 5 = 10
        results[3].Seeders.Should().Be(10);
        results[3].Leechers.Should().Be(5);
        results[3].Peers.Should().Be(15);
    }

    [Test]
    public void ParseTorznabFeedXml_MagnetExtraction_FromAttrDownloadUrlAndDescription()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Magnet.In.DownloadUrl</title>
            <guid>m-1</guid>
            <link>magnet:?xt=urn:btih:1111111111111111111111111111111111111111&amp;dn=ReleaseOne</link>
        </item>
        <item>
            <title>Magnet.In.Description</title>
            <guid>m-2</guid>
            <link>http://tracker/details/2</link>
            <description>Find at magnet:?xt=urn:btih:2222222222222222222222222222222222222222&amp;dn=ReleaseTwo now</description>
        </item>
        <item>
            <title>Magnet.In.Encoded</title>
            <guid>m-3</guid>
            <link>http://tracker/details/3</link>
            <encoded>Source: &lt;a href=""magnet:?xt=urn:btih:3333333333333333333333333333333333333333&amp;dn=ReleaseThree""&gt;download&lt;/a&gt;</encoded>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(3);
        results[0].MagnetUrl.Should().StartWith("magnet:?xt=urn:btih:1111111111111111111111111111111111111111");
        results[0].InfoHash.Should().Be("1111111111111111111111111111111111111111");

        results[1].MagnetUrl.Should().StartWith("magnet:?xt=urn:btih:2222222222222222222222222222222222222222");
        results[1].InfoHash.Should().Be("2222222222222222222222222222222222222222");

        results[2].MagnetUrl.Should().StartWith("magnet:?xt=urn:btih:3333333333333333333333333333333333333333");
        results[2].InfoHash.Should().Be("3333333333333333333333333333333333333333");
    }

    [Test]
    public void ParseTorznabFeedXml_PublishDateFormats_ParsesUnixSecondsMsAndNamedTimeZones()
    {
        // 1767225600 = 2026-01-01 00:00:00 UTC
        // 1767225600000 = ms
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <item>
            <title>Item.UnixSec</title>
            <guid>1</guid>
            <link>http://tracker/1</link>
            <pubDate>1767225600</pubDate>
        </item>
        <item>
            <title>Item.UnixMs</title>
            <guid>2</guid>
            <link>http://tracker/2</link>
            <pubDate>1767225600000</pubDate>
        </item>
        <item>
            <title>Item.NamedTz</title>
            <guid>3</guid>
            <link>http://tracker/3</link>
            <pubDate>Thu, 01 Jan 2026 00:00:00 EST</pubDate>
        </item>
        <item>
            <title>Item.IsoDate</title>
            <guid>4</guid>
            <link>http://tracker/4</link>
            <pubDate>2026-01-01T00:00:00Z</pubDate>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(4);
        var expectedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        results[0].PublishDate.Should().Be(expectedUtc);
        results[1].PublishDate.Should().Be(expectedUtc);
        // EST is UTC-5, so 00:00:00 EST is 05:00:00 UTC
        results[2].PublishDate.Should().Be(new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc));
        results[3].PublishDate.Should().Be(expectedUtc);
    }

    [Test]
    public void ParseTorznabFeedXml_PaginationAttributes_ParsesResponseTotalAndOffset()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <response total=""1250"" offset=""100""/>
        <item>
            <title>Paginated.Item.1</title>
            <guid>p-1</guid>
            <link>http://tracker/1</link>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        results[0].ResponseTotal.Should().Be(1250);
        results[0].ResponseOffset.Should().Be(100);
    }

    [Test]
    public void ParseTorznabFeedXml_PaginationWithChildElements_ParsesResponseTotalAndOffset()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <response>
            <total>800</total>
            <offset>50</offset>
        </response>
        <item>
            <title>Paginated.Item.ChildTags</title>
            <guid>p-2</guid>
            <link>http://tracker/2</link>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        results[0].ResponseTotal.Should().Be(800);
        results[0].ResponseOffset.Should().Be(50);
    }

    [Test]
    public void ParseTorznabFeedXml_IndexerFilters_FiltersFreeleechAndMinSeeders()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Freeleech.HighSeeders</title>
            <guid>f-high</guid>
            <link>http://tracker/1</link>
            <torznab:attr name=""downloadvolumefactor"" value=""0""/>
            <torznab:attr name=""seeders"" value=""25""/>
        </item>
        <item>
            <title>Freeleech.LowSeeders</title>
            <guid>f-low</guid>
            <link>http://tracker/2</link>
            <torznab:attr name=""downloadvolumefactor"" value=""0""/>
            <torznab:attr name=""seeders"" value=""2""/>
        </item>
        <item>
            <title>Normal.HighSeeders</title>
            <guid>n-high</guid>
            <link>http://tracker/3</link>
            <torznab:attr name=""downloadvolumefactor"" value=""1.0""/>
            <torznab:attr name=""seeders"" value=""50""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);

        var filterIndexer = new IndexerDefinition
        {
            FreeleechOnly = true,
            MinSeeders = 10,
        };

        var results = client.ParseTorznabFeedXml(xml, filterIndexer);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Freeleech.HighSeeders");
    }

    [Test]
    public void ParseTorznabFeedXml_DelimitedCategoryAttributes_SplitsCategoriesCorrectly()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Multiple.Cats</title>
            <guid>c-1</guid>
            <link>http://tracker/1</link>
            <category id=""2000"" name=""Movies""/>
            <torznab:attr name=""cat"" value=""2040|2045;5040""/>
        </item>
    </channel>
</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        var cat = results[0].Category;
        cat.Should().Contain("2000");
        cat.Should().Contain("2040");
        cat.Should().Contain("2045");
        cat.Should().Contain("5040");
    }

    #endregion

    #region 4. Error Responses and Edge Cases

    [Test]
    public async Task SearchAsync_ThrowsHttpRequestException_OnHttp400BadRequest()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request",
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition { Name = "BadReqTracker", Url = "https://tracker.org/api" };

        Func<Task> act = async () => await client.SearchAsync(indexer, "test");
        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SearchAsync_ThrowsHttpRequestException_OnHttp401Unauthorized()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            ReasonPhrase = "Invalid API Key",
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition { Name = "UnauthorizedTracker", Url = "https://tracker.org/api" };

        Func<Task> act = async () => await client.SearchAsync(indexer, "test");
        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SearchAsync_ThrowsHttpRequestException_OnHttp500InternalServerError()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "Internal Error",
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition { Name = "ServerErrorTracker", Url = "https://tracker.org/api" };

        Func<Task> act = async () => await client.SearchAsync(indexer, "test");
        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Test]
    public void ParseTorznabFeedXml_ThrowsTorznabException_OnTorznabXmlErrorElement()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<error code=""100"" description=""Incorrect user credentials"" />";

        var client = new TorznabClient(this.configService);

        Action act = () => client.ParseTorznabFeedXml(xml);
        var ex = act.Should().Throw<TorznabException>();
        ex.Which.Code.Should().Be(100);
        ex.Which.Description.Should().Be("Incorrect user credentials");
    }

    [Test]
    public void ParseTorznabFeedXml_ThrowsTorznabException_OnErrorWithDescriptionInElementValue()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<error code=""200"">User account is suspended</error>";

        var client = new TorznabClient(this.configService);

        Action act = () => client.ParseTorznabFeedXml(xml);
        var ex = act.Should().Throw<TorznabException>();
        ex.Which.Code.Should().Be(200);
        ex.Which.Description.Should().Be("User account is suspended");
    }

    [Test]
    public void ParseTorznabFeedXml_HandlesEmptyAndWhitespaceFeed_ReturnsEmptyList()
    {
        var client = new TorznabClient(this.configService);

        client.ParseTorznabFeedXml(null!).Should().BeEmpty();
        client.ParseTorznabFeedXml("").Should().BeEmpty();
        client.ParseTorznabFeedXml("    \t\r\n   ").Should().BeEmpty();
    }

    [Test]
    public void ParseTorznabFeedXml_HandlesMalformedXml_ReturnsEmptyListWithoutCrashing()
    {
        var client = new TorznabClient(this.configService);

        var results = client.ParseTorznabFeedXml("<rss><channel><item><title>Unclosed");
        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [Test]
    public void ParseTorznabFeedXml_SanitizesControlCharactersAndUnescapedAmpersands()
    {
        // XML contains control char \x01 and unescaped '&' in title
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                  "<rss version=\"2.0\">\n" +
                  "    <channel>\n" +
                  "        <item>\n" +
                  "            <title>Rock \x01 & Roll &amp; Metal 2026</title>\n" +
                  "            <guid>ctrl-1</guid>\n" +
                  "            <link>http://tracker/1</link>\n" +
                  "        </item>\n" +
                  "    </channel>\n" +
                  "</rss>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(xml);

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Rock  & Roll & Metal 2026");
    }

    [Test]
    public void ParseTorznabFeedXml_DetectsAntiBotChallenge_ReturnsEmptyList()
    {
        const string challengeHtml = @"<!DOCTYPE html>
<html>
<head><title>Just a moment...</title></head>
<body>
    <div id=""challenge-running"">Checking your browser before accessing the website...</div>
    <script>cf_chl_prog=1;</script>
</body>
</html>";

        var client = new TorznabClient(this.configService);
        var results = client.ParseTorznabFeedXml(challengeHtml);

        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [Test]
    public async Task TestConnectionAsync_Success_WhenCapsEndpointReturnsValidXml()
    {
        const string capsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
    <server version=""1.0"" title=""WorkingTracker""/>
    <limits default=""50"" max=""100""/>
</caps>";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(capsXml, Encoding.UTF8, "application/xml"),
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "WorkingTracker",
            Url = "https://working.tracker/api",
            ApiKey = "workingkey",
        };

        var result = await client.TestConnectionAsync(indexer);
        result.Success.Should().BeTrue();
        result.Capabilities.Should().NotBeNull();
        result.Capabilities.DefaultPageSize.Should().Be(50);
    }

    [Test]
    public async Task TestConnectionAsync_Fails_WhenCapsReturnsTorznabError()
    {
        const string errorXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<error code=""100"" description=""Invalid API key""/>";

        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(errorXml, Encoding.UTF8, "application/xml"),
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "ErrorTracker",
            Url = "https://error.tracker/api",
            ApiKey = "wrongkey",
        };

        var result = await client.TestConnectionAsync(indexer);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("100");
        result.ErrorMessage.Should().Contain("Invalid API key");
    }

    [Test]
    public async Task TestConnectionAsync_FallsBackToSearch_WhenCapsFailsAndSearchSucceeds()
    {
        var requestCount = 0;
        const string searchXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <title>Fallback Search Tracker</title>
    </channel>
</rss>";

        var handler = new MockHttpMessageHandler(req =>
        {
            requestCount++;
            if (req.RequestUri!.Query.Contains("t=caps"))
            {
                // Caps fails with 404
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Fallback search succeeds with RSS feed
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(searchXml, Encoding.UTF8, "application/xml"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "FallbackTracker",
            Url = "https://fallback.tracker/api",
            ApiKey = "key",
        };

        var result = await client.TestConnectionAsync(indexer);
        result.Success.Should().BeTrue();
        requestCount.Should().Be(2);
    }

    [Test]
    public async Task TestConnectionAsync_Fails_WhenFallbackSearchReturnsHttp500()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Query.Contains("t=caps"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                ReasonPhrase = "Internal Error",
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        var indexer = new IndexerDefinition
        {
            Name = "FailingTracker",
            Url = "https://failing.tracker/api",
        };

        var result = await client.TestConnectionAsync(indexer);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("500");
    }

    [Test]
    public async Task TestConnectionAsync_Fails_WhenEmptyOrWhitespaceUrl()
    {
        var client = new TorznabClient(this.configService);

        var resultNull = await client.TestConnectionAsync(null!);
        resultNull.Success.Should().BeFalse();
        resultNull.ErrorMessage.Should().Contain("empty");

        var resultEmpty = await client.TestConnectionAsync(new IndexerDefinition { Url = "   " });
        resultEmpty.Success.Should().BeFalse();
        resultEmpty.ErrorMessage.Should().Contain("empty");
    }

    [Test]
    public async Task SearchAsync_EmptyIndexerOrUrl_ReturnsEmptyListGracefully()
    {
        var client = new TorznabClient(this.configService);

        var resNull = await client.SearchAsync(null!, "query");
        resNull.Should().BeEmpty();

        var resEmptyUrl = await client.SearchAsync(new IndexerDefinition { Url = "" }, "query");
        resEmptyUrl.Should().BeEmpty();

        var resRssNull = await client.FetchRssAsync(null!);
        resRssNull.Should().BeEmpty();

        var resRssEmpty = await client.FetchRssAsync(new IndexerDefinition { Url = "" });
        resRssEmpty.Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_CancellationToken_PropagatesCancellation()
    {
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            await Task.Delay(5000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler);
        var client = new TorznabClient(httpClient);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var indexer = new IndexerDefinition { Url = "https://tracker.org/api" };

        Func<Task> act = async () => await client.SearchAsync(indexer, "test", cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}

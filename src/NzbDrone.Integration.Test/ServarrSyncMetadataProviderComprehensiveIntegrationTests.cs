// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.MediaEnrichment.Providers;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ServarrSyncMetadataProviderComprehensiveIntegrationTests : IntegrationTestBase
{
    private class MockArrHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            var response = new HttpResponseMessage(HttpStatusCode.OK);

            if (uri.Contains("/api/v3/queue") && uri.Contains("sonarr"))
            {
                var json = "[{\"title\":\"Breaking.Bad.S01E01.720p\",\"downloadId\":\"s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1\",\"series\":{\"id\":101,\"title\":\"Breaking Bad\",\"year\":2008,\"overview\":\"A chemistry teacher diagnosed with lung cancer\",\"ratings\":{\"value\":9.5},\"images\":[{\"coverType\":\"poster\",\"remoteUrl\":\"https://art.tv/poster.jpg\"}]}}]";
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else if (uri.Contains("/api/v3/queue") && uri.Contains("radarr"))
            {
                var json = "[{\"title\":\"Inception.2010.1080p\",\"downloadId\":\"r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1\",\"movie\":{\"id\":202,\"title\":\"Inception\",\"year\":2010,\"overview\":\"A thief who steals corporate secrets\",\"ratings\":{\"value\":8.8},\"images\":[{\"coverType\":\"poster\",\"remoteUrl\":\"https://art.movie/poster.jpg\"}]}}]";
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else if (uri.Contains("/api/v1/queue") && uri.Contains("lidarr"))
            {
                var json = "[{\"title\":\"Daft.Punk.Discovery.FLAC\",\"downloadId\":\"l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1\",\"artist\":{\"artistName\":\"Daft Punk\"},\"album\":{\"title\":\"Discovery\",\"releaseDate\":\"2001-03-12\"}}]";
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else if (uri.Contains("/api/v3/movie/lookup"))
            {
                var json = "[{\"id\":303,\"title\":\"The Matrix\",\"year\":1999,\"overview\":\"Reality is a simulation\",\"ratings\":{\"value\":8.7}}]";
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else if (uri.Contains("/api/v3/series/lookup"))
            {
                var json = "[{\"id\":404,\"title\":\"Better Call Saul\",\"year\":2015,\"overview\":\"Trials of Jimmy McGill\",\"ratings\":{\"value\":9.0}}]";
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else if (uri.Contains("/api/v1/search"))
            {
                var json = "[{\"artist\":{\"artistName\":\"Pink Floyd\"},\"album\":{\"title\":\"The Dark Side of the Moon\",\"releaseDate\":\"1973-03-01\"}}]";
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else
            {
                response.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    [Test]
    public async Task ServarrSync_MetadataProvider_FullCapabilitiesAndCorrelation_Succeeds()
    {
        var services = GlobalSetup.Factory.Services;
        var arrRepo = services.GetService(typeof(IArrConnectionRepository)) as IArrConnectionRepository;
        arrRepo.Should().NotBeNull();

        // 1. Add mock connections to repository
        var sonarrConn = arrRepo.Insert(new ArrConnectionDefinition
        {
            Name = "TestSonarrInstance",
            Implementation = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://127.0.0.1:8989/sonarr",
            ApiKey = "sonarr-api-key",
            Enable = true,
        });

        var radarrConn = arrRepo.Insert(new ArrConnectionDefinition
        {
            Name = "TestRadarrInstance",
            Implementation = "Radarr",
            ArrType = "Radarr",
            Url = "http://127.0.0.1:7878/radarr",
            ApiKey = "radarr-api-key",
            Enable = true,
        });

        var lidarrConn = arrRepo.Insert(new ArrConnectionDefinition
        {
            Name = "TestLidarrInstance",
            Implementation = "Lidarr",
            ArrType = "Lidarr",
            Url = "http://127.0.0.1:8686/lidarr",
            ApiKey = "lidarr-api-key",
            Enable = true,
        });

        var mockClient = new HttpClient(new MockArrHttpMessageHandler());
        var provider = new ServarrSyncMetadataProvider(arrRepo, mockClient);

        try
        {
            // 2. Health and Capabilities
            var health = await provider.ProbeHealthAsync();
            health.IsHealthy.Should().BeTrue();
            health.StatusMessage.Should().Contain("Arr instances configured");

            provider.Capabilities.SupportsMovies.Should().BeTrue();
            provider.Capabilities.SupportsTvSeries.Should().BeTrue();
            provider.Capabilities.SupportsMusic.Should().BeTrue();
            provider.ProviderId.Should().Be("ServarrSync");
            provider.DisplayName.Should().NotBeNullOrWhiteSpace();

            // 3. Correlate Sonarr TV Series from Queue via InfoHash
            var tvMeta = await provider.FetchMetadataAsync(
                title: "Breaking.Bad.S01E01.720p",
                category: "tv",
                year: 2008,
                infoHash: "s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1s1");
            tvMeta.Should().NotBeNull();
            tvMeta.Title.Should().Be("Breaking Bad");
            tvMeta.Year.Should().Be(2008);
            tvMeta.MediaType.Should().Be("TV");
            tvMeta.Rating.Should().Be(9.5);
            tvMeta.PosterUrl.Should().Be("https://art.tv/poster.jpg");

            // 4. Correlate Radarr Movie from Queue via InfoHash
            var movieMeta = await provider.FetchMetadataAsync(
                title: "Inception.2010.1080p",
                category: "movies",
                year: 2010,
                infoHash: "r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1r1");
            movieMeta.Should().NotBeNull();
            movieMeta.Title.Should().Be("Inception");
            movieMeta.Year.Should().Be(2010);
            movieMeta.MediaType.Should().Be("Movie");
            movieMeta.Rating.Should().Be(8.8);
            movieMeta.PosterUrl.Should().Be("https://art.movie/poster.jpg");

            // 5. Correlate Lidarr Music from Queue via InfoHash
            var musicMeta = await provider.FetchMetadataAsync(
                title: "Daft.Punk.Discovery.FLAC",
                category: "music",
                year: 2001,
                infoHash: "l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1l1");
            musicMeta.Should().NotBeNull();
            musicMeta.Title.Should().Contain("Discovery");
            musicMeta.MediaType.Should().Be("Music");

            // 6. Radarr Movie Lookup fallback
            var lookupMovie = await provider.FetchMetadataAsync(
                title: "The.Matrix.1999.2160p",
                category: "movies",
                year: 1999,
                infoHash: null);
            lookupMovie.Should().NotBeNull();
            lookupMovie.Title.Should().Be("The Matrix");
            lookupMovie.Year.Should().Be(1999);

            // 7. Sonarr Series Lookup fallback
            var lookupSeries = await provider.FetchMetadataAsync(
                title: "Better.Call.Saul.S01E01",
                category: "tv",
                year: 2015,
                infoHash: null);
            lookupSeries.Should().NotBeNull();
            lookupSeries.Title.Should().Be("Better Call Saul");
            lookupSeries.Year.Should().Be(2015);

            // 8. Empty input returns null
            var emptyMeta = await provider.FetchMetadataAsync(string.Empty, null, null, null);
            emptyMeta.Should().BeNull();
        }
        finally
        {
            // Clean up test connections
            arrRepo.Delete(sonarrConn.Id);
            arrRepo.Delete(radarrConn.Id);
            arrRepo.Delete(lidarrConn.Id);
        }
    }
}

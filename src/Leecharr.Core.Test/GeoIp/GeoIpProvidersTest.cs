// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Network.GeoIp;

namespace Leecharr.Core.Test.GeoIp;

[TestFixture]
public class GeoIpProvidersTest
{
    private IDiskProvider diskProvider = null!;
    private IAppFolderInfo appFolderInfo = null!;

    [SetUp]
    public void SetUp()
    {
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.appFolderInfo = Substitute.For<IAppFolderInfo>();

        this.appFolderInfo.AppDataFolder.Returns("/config/appdata");
        this.appFolderInfo.StartUpFolder.Returns("/opt/leecharr");
    }

    [Test]
    public void MaxMind_Metadata_ReturnsExpectedProperties()
    {
        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);

        provider.ProviderId.Should().Be("MaxMind");
        provider.DisplayName.Should().Be("MaxMind GeoLite2 / GeoIP2 (.mmdb)");
        provider.Version.Should().Be("2.0");
        provider.Capabilities.Should().Be(
            GeoIpCapabilities.Country |
            GeoIpCapabilities.City |
            GeoIpCapabilities.Asn |
            GeoIpCapabilities.OfflineDatabase);
    }

    [Test]
    public void MaxMind_GetDatabasePath_WhenFileExistsInConfig_ReturnsPathAndAvailableTrue()
    {
        this.diskProvider.FileExists("/config/GeoIP/GeoLite2-City.mmdb").Returns(true);

        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);

        provider.GetDatabasePath().Should().Be("/config/GeoIP/GeoLite2-City.mmdb");
        provider.IsAvailable.Should().BeTrue();
    }

    [Test]
    public void MaxMind_GetDatabasePath_WhenFileExistsInAppData_ReturnsPath()
    {
        var appDataDb = Path.Combine(this.appFolderInfo.AppDataFolder, "GeoIP", "GeoLite2-Country.mmdb");
        this.diskProvider.FileExists(appDataDb).Returns(true);

        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);

        provider.GetDatabasePath().Should().Be(appDataDb);
        provider.IsAvailable.Should().BeTrue();
    }

    [Test]
    public void MaxMind_GetDatabasePath_WhenNoFilesExist_ReturnsNullAndAvailableFalse()
    {
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(false);

        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);

        provider.GetDatabasePath().Should().BeNull();
        provider.IsAvailable.Should().BeFalse();
    }

    [Test]
    public async Task MaxMind_ProbeHealthAsync_WhenDatabaseMissing_ReturnsUnhealthyWithWarning()
    {
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(false);

        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);
        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("not found");
        health.Warnings.Should().Contain("Database file missing.");
    }

    [Test]
    public async Task MaxMind_ProbeHealthAsync_WhenDatabaseFileCannotBeOpened_ReturnsUnhealthyWithException()
    {
        var dummyPath = "/config/GeoIP/GeoLite2-City.mmdb";
        this.diskProvider.FileExists(dummyPath).Returns(true);

        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);
        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("Failed to read MaxMind database");
        health.Warnings.Should().NotBeEmpty();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task MaxMind_LookupAsync_WithNullOrWhitespace_ReturnsNull(string ip)
    {
        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);
        var result = await provider.LookupAsync(ip!);

        result.Should().BeNull();
    }

    [TestCase("not-an-ip")]
    [TestCase("hostname.local")]
    [TestCase("999.999.999.999")]
    public async Task MaxMind_LookupAsync_WithInvalidIp_ReturnsUnenrichedInfo(string invalidIp)
    {
        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);
        var result = await provider.LookupAsync(invalidIp);

        result.Should().NotBeNull();
        result.IpAddress.Should().Be(invalidIp);
        result.CountryCode.Should().BeNull();
    }

    [TestCase("127.0.0.1")]
    [TestCase("127.1.2.3")]
    [TestCase("::1")]
    [TestCase("10.0.0.1")]
    [TestCase("10.255.255.254")]
    [TestCase("172.16.0.1")]
    [TestCase("172.31.255.254")]
    [TestCase("192.168.0.1")]
    [TestCase("192.168.1.100")]
    [TestCase("169.254.1.1")]
    [TestCase("0.0.0.0")]
    [TestCase("fe80::1")]
    [TestCase("fec0::1")]
    [TestCase("fc00::1")]
    [TestCase("fd12:3456::1")]
    [TestCase("::ffff:192.168.1.1")]
    public async Task MaxMind_LookupAsync_WithPrivateOrLoopbackIp_ReturnsLanInfoWithoutDatabase(string privateIp)
    {
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(false);

        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);
        var result = await provider.LookupAsync(privateIp);

        result.Should().NotBeNull();
        result.IpAddress.Should().Be(privateIp);
        result.CountryCode.Should().Be("LAN");
        result.CountryName.Should().Be("Local Network");
        result.City.Should().Be("Localhost");
    }

    [TestCase("8.8.8.8")]
    [TestCase("1.1.1.1")]
    [TestCase("172.15.255.255")]
    [TestCase("172.32.0.1")]
    public async Task MaxMind_LookupAsync_WithPublicIpWhenDatabaseMissing_ReturnsUnenrichedInfo(string publicIp)
    {
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(false);

        using var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);
        var result = await provider.LookupAsync(publicIp);

        result.Should().NotBeNull();
        result.IpAddress.Should().Be(publicIp);
        result.CountryCode.Should().BeNull();
    }

    [Test]
    public void MaxMind_ReloadAndDispose_ExecutesCleanly()
    {
        var provider = new MaxMindGeoIpProvider(this.diskProvider, this.appFolderInfo);

        var act = () =>
        {
            provider.Reload();
            provider.Dispose();
            provider.Dispose();
        };

        act.Should().NotThrow();
    }

    [Test]
    public void OnlineApi_Metadata_ReturnsExpectedProperties()
    {
        using var provider = new OnlineApiGeoIpProvider();

        provider.ProviderId.Should().Be("OnlineApi");
        provider.DisplayName.Should().Be("Zero-Disk Online HTTP Geolocation API");
        provider.Version.Should().Be("1.0");
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.Should().Be(
            GeoIpCapabilities.Country |
            GeoIpCapabilities.City |
            GeoIpCapabilities.Asn |
            GeoIpCapabilities.Isp |
            GeoIpCapabilities.InMemoryCache);
        provider.ApiEndpointTemplate.Should().Contain("ip-api.com");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task OnlineApi_LookupAsync_WithNullOrWhitespace_ReturnsNull(string ip)
    {
        var callCount = 0;
        using var handler = new TestHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var result = await provider.LookupAsync(ip!);

        result.Should().BeNull();
        callCount.Should().Be(0);
    }

    [TestCase("127.0.0.1")]
    [TestCase("::1")]
    [TestCase("10.0.0.1")]
    [TestCase("172.16.5.5")]
    [TestCase("192.168.1.1")]
    [TestCase("169.254.10.20")]
    [TestCase("0.0.0.0")]
    [TestCase("fe80::1")]
    [TestCase("fc00::1")]
    [TestCase("::ffff:10.0.0.1")]
    public async Task OnlineApi_LookupAsync_WithPrivateOrLoopbackIp_SuppressesAndReturnsLanInfoWithoutHttpRequest(string privateIp)
    {
        var callCount = 0;
        using var handler = new TestHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var result = await provider.LookupAsync(privateIp);

        result.Should().NotBeNull();
        result.IpAddress.Should().Be(privateIp);
        result.CountryCode.Should().Be("LAN");
        result.CountryName.Should().Be("Local Network");
        result.City.Should().Be("Localhost");
        callCount.Should().Be(0);
    }

    [Test]
    public async Task OnlineApi_LookupAsync_WithPublicIp_ParsesSuccessfulResponse()
    {
        var jsonResponse = """
            {
                "status": "success",
                "country": "Switzerland",
                "countryCode": "CH",
                "region": "ZH",
                "regionName": "Zurich",
                "city": "Zurich",
                "lat": 47.3769,
                "lon": 8.5417,
                "timezone": "Europe/Zurich",
                "isp": "Init7 (Switzerland) Ltd.",
                "as": "AS13030 Init7",
                "query": "185.10.10.10"
            }
            """;

        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse),
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var result = await provider.LookupAsync("185.10.10.10");

        result.Should().NotBeNull();
        result.IpAddress.Should().Be("185.10.10.10");
        result.CountryCode.Should().Be("CH");
        result.CountryName.Should().Be("Switzerland");
        result.City.Should().Be("Zurich");
        result.Region.Should().Be("Zurich");
        result.Latitude.Should().Be(47.3769);
        result.Longitude.Should().Be(8.5417);
        result.TimeZone.Should().Be("Europe/Zurich");
        result.Isp.Should().Be("Init7 (Switzerland) Ltd.");
        result.Asn.Should().Be("AS13030 Init7");
    }

    [Test]
    public async Task OnlineApi_LookupAsync_ResponseCaching_DoesNotRepeatHttpRequestForSameIp()
    {
        var callCount = 0;
        var jsonResponse = """
            {
                "status": "success",
                "country": "Germany",
                "countryCode": "DE",
                "city": "Frankfurt",
                "query": "1.2.3.4"
            }
            """;

        using var handler = new TestHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse),
            };
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var first = await provider.LookupAsync("1.2.3.4");
        var second = await provider.LookupAsync("1.2.3.4");

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first.CountryCode.Should().Be("DE");
        second.CountryCode.Should().Be("DE");
        callCount.Should().Be(1);
    }

    [Test]
    public async Task OnlineApi_LookupAsync_WhenHttpError500_ReturnsFallbackStubAndNegativeCaches()
    {
        var callCount = 0;
        using var handler = new TestHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var result1 = await provider.LookupAsync("8.8.8.8");
        var result2 = await provider.LookupAsync("8.8.8.8");

        result1.Should().NotBeNull();
        result1.IpAddress.Should().Be("8.8.8.8");
        result1.CountryCode.Should().BeNull();

        result2.Should().NotBeNull();
        result2.IpAddress.Should().Be("8.8.8.8");

        // Negative cache prevented second HTTP call
        callCount.Should().Be(1);
    }

    [Test]
    public async Task OnlineApi_LookupAsync_WhenNetworkExceptionThrown_CatchesAndReturnsFallbackStub()
    {
        using var handler = new TestHttpMessageHandler(_ => throw new HttpRequestException("DNS resolution failed."));
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var result = await provider.LookupAsync("8.8.4.4");

        result.Should().NotBeNull();
        result.IpAddress.Should().Be("8.8.4.4");
        result.CountryCode.Should().BeNull();
    }

    [Test]
    public async Task OnlineApi_LookupAsync_WhenApiReturnsFailStatus_ReturnsFallbackStub()
    {
        var failJson = """
            {
                "status": "fail",
                "message": "reserved range",
                "query": "0.0.0.0"
            }
            """;

        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(failJson),
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var result = await provider.LookupAsync("198.51.100.1");

        result.Should().NotBeNull();
        result.IpAddress.Should().Be("198.51.100.1");
        result.CountryCode.Should().BeNull();
    }

    [Test]
    public async Task OnlineApi_LookupAsync_WhenMalformedJsonReturned_ReturnsFallbackStub()
    {
        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>502 Bad Gateway</html>"),
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var result = await provider.LookupAsync("1.1.1.1");

        result.Should().NotBeNull();
        result.IpAddress.Should().Be("1.1.1.1");
        result.CountryCode.Should().BeNull();
    }

    [Test]
    public async Task OnlineApi_RateLimiting_WhenExceeding45RequestsPerMinute_SuppressesHttpAndReturnsStub()
    {
        var callCount = 0;
        var jsonResponse = """
            {
                "status": "success",
                "country": "United States",
                "countryCode": "US",
                "city": "San Jose"
            }
            """;

        using var handler = new TestHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse),
            };
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        // Make 45 successful requests
        for (var i = 1; i <= 45; i++)
        {
            var res = await provider.LookupAsync($"198.51.100.{i}");
            res.CountryCode.Should().Be("US");
        }

        callCount.Should().Be(45);

        // 46th request should hit rate limit without making an HTTP request
        var rateLimitedResult = await provider.LookupAsync("198.51.100.46");

        rateLimitedResult.Should().NotBeNull();
        rateLimitedResult.IpAddress.Should().Be("198.51.100.46");
        rateLimitedResult.CountryCode.Should().BeNull();
        callCount.Should().Be(45);
    }

    [Test]
    public async Task OnlineApi_ProbeHealthAsync_WhenReachable_ReturnsHealthy()
    {
        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeTrue();
        health.StatusMessage.Should().Be("Online Geolocation API reachable and responding.");
    }

    [Test]
    public async Task OnlineApi_ProbeHealthAsync_WhenHttpError_ReturnsUnhealthy()
    {
        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("503");
        health.Warnings.Should().Contain("HTTP request failed.");
    }

    [Test]
    public async Task OnlineApi_ProbeHealthAsync_WhenExceptionThrown_ReturnsUnhealthyWithExceptionMessage()
    {
        using var handler = new TestHttpMessageHandler(_ => throw new HttpRequestException("Host unreachable."));
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        var health = await provider.ProbeHealthAsync();

        health.IsHealthy.Should().BeFalse();
        health.Warnings.Should().Contain("Host unreachable.");
    }

    [Test]
    public async Task OnlineApi_Dispose_WhenOwnsHttpClientFalse_DoesNotDisposeInjectedClient()
    {
        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var provider = new OnlineApiGeoIpProvider(httpClient, ownsHttpClient: false);

        provider.Dispose();

        var response = await httpClient.GetAsync("http://localhost/test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.handler(request));
        }
    }
}

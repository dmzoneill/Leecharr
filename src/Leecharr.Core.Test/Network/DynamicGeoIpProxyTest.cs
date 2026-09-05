// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.GeoIp;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class DynamicGeoIpProxyTest
{
    private IGeoIpProvider maxMindProvider = null!;
    private IGeoIpProvider ip2LocationProvider = null!;
    private IGeoIpProvider onlineApiProvider = null!;
    private IConfigService configService = null!;
    private IEventAggregator eventAggregator = null!;
    private DynamicGeoIpProxy proxy = null!;

    [SetUp]
    public void SetUp()
    {
        this.maxMindProvider = Substitute.For<IGeoIpProvider>();
        this.maxMindProvider.ProviderId.Returns("MaxMind");
        this.maxMindProvider.DisplayName.Returns("MaxMind GeoLite2 / GeoIP2 (.mmdb)");
        this.maxMindProvider.Version.Returns("2.0");
        this.maxMindProvider.IsAvailable.Returns(true);
        this.maxMindProvider.Capabilities.Returns(GeoIpCapabilities.Country | GeoIpCapabilities.City | GeoIpCapabilities.OfflineDatabase);
        this.maxMindProvider.ProbeHealthAsync().Returns(Task.FromResult(new GeoIpHealthResult { IsHealthy = true, StatusMessage = "OK" }));
        this.maxMindProvider.LookupAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var ip = callInfo.Arg<string>();
            return Task.FromResult(new GeoLocationInfo
            {
                IpAddress = ip,
                CountryCode = "US",
                CountryName = "United States",
                City = "Ashburn",
                Latitude = 39.0438,
                Longitude = -77.4874,
            });
        });

        this.ip2LocationProvider = Substitute.For<IGeoIpProvider>();
        this.ip2LocationProvider.ProviderId.Returns("IP2Location");
        this.ip2LocationProvider.DisplayName.Returns("IP2Location Binary (.BIN)");
        this.ip2LocationProvider.Version.Returns("1.0");
        this.ip2LocationProvider.IsAvailable.Returns(true);
        this.ip2LocationProvider.Capabilities.Returns(GeoIpCapabilities.Country | GeoIpCapabilities.City | GeoIpCapabilities.OfflineDatabase);
        this.ip2LocationProvider.ProbeHealthAsync().Returns(Task.FromResult(new GeoIpHealthResult { IsHealthy = true, StatusMessage = "OK" }));
        this.ip2LocationProvider.LookupAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var ip = callInfo.Arg<string>();
            return Task.FromResult(new GeoLocationInfo
            {
                IpAddress = ip,
                CountryCode = "GB",
                CountryName = "United Kingdom",
                City = "London",
            });
        });

        this.onlineApiProvider = Substitute.For<IGeoIpProvider>();
        this.onlineApiProvider.ProviderId.Returns("OnlineApi");
        this.onlineApiProvider.DisplayName.Returns("Zero-Disk Online HTTP Geolocation API");
        this.onlineApiProvider.Version.Returns("1.0");
        this.onlineApiProvider.IsAvailable.Returns(true);
        this.onlineApiProvider.Capabilities.Returns(GeoIpCapabilities.All);
        this.onlineApiProvider.ProbeHealthAsync().Returns(Task.FromResult(new GeoIpHealthResult { IsHealthy = true, StatusMessage = "OK" }));

        this.configService = Substitute.For<IConfigService>();
        this.configService.GetValue("ActiveGeoIpProvider", Arg.Any<string>()).Returns("MaxMind");

        this.eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IGeoIpProvider> { this.maxMindProvider, this.ip2LocationProvider, this.onlineApiProvider };
        this.proxy = new DynamicGeoIpProxy(providers, this.configService, this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        this.proxy.ActiveProviderId.Should().Be("MaxMind");
        this.proxy.ActiveProvider.Should().BeSameAs(this.maxMindProvider);
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = this.proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "MaxMind", "IP2Location", "OnlineApi" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = this.proxy.GetProvider("IP2Location");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("IP2Location");
    }

    [Test]
    public void GetProvider_WithInvalidId_ReturnsNull()
    {
        var provider = this.proxy.GetProvider("NonExistent");
        provider.Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await this.proxy.ProbeProviderAsync("IP2Location");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeProviderAsync_WithInvalidProvider_ReturnsUnhealthy()
    {
        var probe = await this.proxy.ProbeProviderAsync("InvalidProvider");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProviderAndPublishesEvent()
    {
        var result = await this.proxy.SwitchProviderAsync("IP2Location");
        result.Should().BeTrue();
        this.proxy.ActiveProviderId.Should().Be("IP2Location");
        this.proxy.ActiveProvider.Should().BeSameAs(this.ip2LocationProvider);

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveGeoIpProvider"] == "IP2Location"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<GeoIpProviderSwitchedEvent>(e => e.PreviousProvider == "MaxMind" && e.NewProvider == "IP2Location"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsTrueWithoutWork()
    {
        var result = await this.proxy.SwitchProviderAsync("MaxMind");
        result.Should().BeTrue();
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<GeoIpProviderSwitchedEvent>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownProvider_ReturnsFalse()
    {
        var result = await this.proxy.SwitchProviderAsync("UnknownProvider");
        result.Should().BeFalse();
        this.proxy.ActiveProviderId.Should().Be("MaxMind");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        this.ip2LocationProvider.ProbeHealthAsync().Returns(Task.FromResult(new GeoIpHealthResult
        {
            IsHealthy = false,
            StatusMessage = "Database file missing",
        }));

        var result = await this.proxy.SwitchProviderAsync("IP2Location");
        result.Should().BeFalse();
        this.proxy.ActiveProviderId.Should().Be("MaxMind");
    }

    [Test]
    public async Task LookupAsync_DelegatesToActiveProvider()
    {
        var result = await this.proxy.LookupAsync("8.8.8.8");
        result.Should().NotBeNull();
        result.IpAddress.Should().Be("8.8.8.8");
        result.CountryCode.Should().Be("US");
        result.City.Should().Be("Ashburn");

        await this.maxMindProvider.Received(1).LookupAsync("8.8.8.8");
    }

    [Test]
    public void Lookup_SynchronousDelegation_Works()
    {
        var result = this.proxy.Lookup("1.1.1.1");
        result.Should().NotBeNull();
        result.IpAddress.Should().Be("1.1.1.1");
        result.CountryCode.Should().Be("US");
    }

    [Test]
    public async Task MaxMindGeoIpProvider_FileMissing_ReportsUnhealthy()
    {
        var diskProvider = Substitute.For<IDiskProvider>();
        diskProvider.FileExists(Arg.Any<string>()).Returns(false);

        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns("/tmp/leecharr-appdata");
        appFolderInfo.StartUpFolder.Returns("/tmp/leecharr-startup");

        using var provider = new MaxMindGeoIpProvider(diskProvider, appFolderInfo);
        provider.ProviderId.Should().Be("MaxMind");
        provider.DisplayName.Should().Contain("MaxMind");
        provider.IsAvailable.Should().BeFalse();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("not found");

        var lookup = await provider.LookupAsync("8.8.8.8");
        lookup.Should().NotBeNull();
        lookup.IpAddress.Should().Be("8.8.8.8");
    }

    [Test]
    public async Task IP2LocationGeoIpProvider_FileMissing_ReportsUnhealthy()
    {
        var diskProvider = Substitute.For<IDiskProvider>();
        diskProvider.FileExists(Arg.Any<string>()).Returns(false);

        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns("/tmp/leecharr-appdata");
        appFolderInfo.StartUpFolder.Returns("/tmp/leecharr-startup");

        using var provider = new IP2LocationGeoIpProvider(diskProvider, appFolderInfo);
        provider.ProviderId.Should().Be("IP2Location");
        provider.DisplayName.Should().Contain("IP2Location");
        provider.IsAvailable.Should().BeFalse();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.StatusMessage.Should().Contain("not found");

        var lookup = await provider.LookupAsync("8.8.8.8");
        lookup.Should().NotBeNull();
        lookup.IpAddress.Should().Be("8.8.8.8");
    }

    [Test]
    public async Task OnlineApiGeoIpProvider_OnlineLookupAndCaching()
    {
        var handler = new MockHttpMessageHandler(@"{
            ""status"": ""success"",
            ""country"": ""Ireland"",
            ""countryCode"": ""IE"",
            ""region"": ""L"",
            ""regionName"": ""Leinster"",
            ""city"": ""Dublin"",
            ""lat"": 53.3331,
            ""lon"": -6.2489,
            ""timezone"": ""Europe/Dublin"",
            ""isp"": ""Google LLC"",
            ""as"": ""AS15169 Google LLC"",
            ""query"": ""8.8.8.8""
        }");

        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient);

        provider.ProviderId.Should().Be("OnlineApi");
        provider.DisplayName.Should().Contain("Online HTTP");
        provider.IsAvailable.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();

        // 1. Initial lookup
        var result1 = await provider.LookupAsync("8.8.8.8");
        result1.Should().NotBeNull();
        result1.CountryCode.Should().Be("IE");
        result1.CountryName.Should().Be("Ireland");
        result1.City.Should().Be("Dublin");
        result1.Latitude.Should().Be(53.3331);
        result1.Longitude.Should().Be(-6.2489);
        result1.Isp.Should().Be("Google LLC");

        handler.RequestCount.Should().Be(2); // 1 for probe health, 1 for lookup

        // 2. Secondary lookup for same IP hits LRU cache without HTTP request
        var result2 = await provider.LookupAsync("8.8.8.8");
        result2.Should().NotBeNull();
        result2.CountryCode.Should().Be("IE");
        result2.City.Should().Be("Dublin");
        handler.RequestCount.Should().Be(2); // No additional HTTP request made

        // 3. Local/private IP is resolved immediately without hitting network
        var localResult = await provider.LookupAsync("127.0.0.1");
        localResult.CountryCode.Should().Be("LAN");
        localResult.CountryName.Should().Be("Local Network");
        handler.RequestCount.Should().Be(2);
    }

    [Test]
    public async Task OnlineApiGeoIpProvider_IPv4MappedIPv6_ResolvedAsLocalNetworkWithoutNetworkCall()
    {
        var handler = new MockHttpMessageHandler("{}");
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient);

        var mappedIps = new[]
        {
            "::ffff:192.168.1.50",
            "::ffff:10.0.0.1",
            "::ffff:172.16.1.1",
            "::ffff:127.0.0.1",
            "::ffff:169.254.1.1",
            "::1",
            "fe80::1",
        };

        foreach (var ip in mappedIps)
        {
            var result = await provider.LookupAsync(ip);
            result.Should().NotBeNull();
            result.CountryCode.Should().Be("LAN");
            result.CountryName.Should().Be("Local Network");
        }

        handler.RequestCount.Should().Be(0);
    }

    [Test]
    public async Task OnlineApiGeoIpProvider_NegativeCaching_CachesFailedHttpAndApiErrors()
    {
        var handler = new MockHttpMessageHandler(@"{""status"":""fail"",""message"":""reserved range""}", HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient);

        // 1. First lookup fails API status
        var result1 = await provider.LookupAsync("240.0.0.1");
        result1.Should().NotBeNull();
        result1.IpAddress.Should().Be("240.0.0.1");
        result1.CountryCode.Should().BeNullOrEmpty();
        handler.RequestCount.Should().Be(1);

        // 2. Second lookup hits negative cache without HTTP call
        var result2 = await provider.LookupAsync("240.0.0.1");
        result2.Should().NotBeNull();
        result2.IpAddress.Should().Be("240.0.0.1");
        handler.RequestCount.Should().Be(1);
    }

    [Test]
    public async Task OnlineApiGeoIpProvider_ConfigurableEndpoint_UsesCustomEndpoint()
    {
        var handler = new MockHttpMessageHandler(@"{""status"":""success"",""countryCode"":""FR"",""country"":""France"",""city"":""Paris""}");
        using var httpClient = new HttpClient(handler);
        using var provider = new OnlineApiGeoIpProvider(httpClient, false, "https://secure-geo.example.com/lookup/{0}");

        provider.ApiEndpointTemplate.Should().Be("https://secure-geo.example.com/lookup/{0}");

        var result = await provider.LookupAsync("1.2.3.4");
        result.CountryCode.Should().Be("FR");
        result.City.Should().Be("Paris");
        handler.RequestCount.Should().Be(1);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string responseContent;
        private readonly HttpStatusCode statusCode;

        public int RequestCount { get; private set; }

        public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            this.responseContent = responseContent;
            this.statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.RequestCount++;
            var response = new HttpResponseMessage(this.statusCode)
            {
                Content = new StringContent(this.responseContent),
            };
            return Task.FromResult(response);
        }
    }
}

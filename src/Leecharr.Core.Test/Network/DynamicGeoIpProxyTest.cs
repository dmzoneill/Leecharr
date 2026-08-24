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
    private IGeoIpProvider _maxMindProvider = null!;
    private IGeoIpProvider _ip2LocationProvider = null!;
    private IGeoIpProvider _onlineApiProvider = null!;
    private IConfigService _configService = null!;
    private IEventAggregator _eventAggregator = null!;
    private DynamicGeoIpProxy _proxy = null!;

    [SetUp]
    public void SetUp()
    {
        _maxMindProvider = Substitute.For<IGeoIpProvider>();
        _maxMindProvider.ProviderId.Returns("MaxMind");
        _maxMindProvider.DisplayName.Returns("MaxMind GeoLite2 / GeoIP2 (.mmdb)");
        _maxMindProvider.Version.Returns("2.0");
        _maxMindProvider.IsAvailable.Returns(true);
        _maxMindProvider.Capabilities.Returns(GeoIpCapabilities.Country | GeoIpCapabilities.City | GeoIpCapabilities.OfflineDatabase);
        _maxMindProvider.ProbeHealthAsync().Returns(Task.FromResult(new GeoIpHealthResult { IsHealthy = true, StatusMessage = "OK" }));
        _maxMindProvider.LookupAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var ip = callInfo.Arg<string>();
            return Task.FromResult(new GeoLocationInfo
            {
                IpAddress = ip,
                CountryCode = "US",
                CountryName = "United States",
                City = "Ashburn",
                Latitude = 39.0438,
                Longitude = -77.4874
            });
        });

        _ip2LocationProvider = Substitute.For<IGeoIpProvider>();
        _ip2LocationProvider.ProviderId.Returns("IP2Location");
        _ip2LocationProvider.DisplayName.Returns("IP2Location Binary (.BIN)");
        _ip2LocationProvider.Version.Returns("1.0");
        _ip2LocationProvider.IsAvailable.Returns(true);
        _ip2LocationProvider.Capabilities.Returns(GeoIpCapabilities.Country | GeoIpCapabilities.City | GeoIpCapabilities.OfflineDatabase);
        _ip2LocationProvider.ProbeHealthAsync().Returns(Task.FromResult(new GeoIpHealthResult { IsHealthy = true, StatusMessage = "OK" }));
        _ip2LocationProvider.LookupAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var ip = callInfo.Arg<string>();
            return Task.FromResult(new GeoLocationInfo
            {
                IpAddress = ip,
                CountryCode = "GB",
                CountryName = "United Kingdom",
                City = "London"
            });
        });

        _onlineApiProvider = Substitute.For<IGeoIpProvider>();
        _onlineApiProvider.ProviderId.Returns("OnlineApi");
        _onlineApiProvider.DisplayName.Returns("Zero-Disk Online HTTP Geolocation API");
        _onlineApiProvider.Version.Returns("1.0");
        _onlineApiProvider.IsAvailable.Returns(true);
        _onlineApiProvider.Capabilities.Returns(GeoIpCapabilities.All);
        _onlineApiProvider.ProbeHealthAsync().Returns(Task.FromResult(new GeoIpHealthResult { IsHealthy = true, StatusMessage = "OK" }));

        _configService = Substitute.For<IConfigService>();
        _configService.GetValue("ActiveGeoIpProvider", Arg.Any<string>()).Returns("MaxMind");

        _eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IGeoIpProvider> { _maxMindProvider, _ip2LocationProvider, _onlineApiProvider };
        _proxy = new DynamicGeoIpProxy(providers, _configService, _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        _proxy.ActiveProviderId.Should().Be("MaxMind");
        _proxy.ActiveProvider.Should().BeSameAs(_maxMindProvider);
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = _proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "MaxMind", "IP2Location", "OnlineApi" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = _proxy.GetProvider("IP2Location");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("IP2Location");
    }

    [Test]
    public void GetProvider_WithInvalidId_ReturnsNull()
    {
        var provider = _proxy.GetProvider("NonExistent");
        provider.Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await _proxy.ProbeProviderAsync("IP2Location");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeProviderAsync_WithInvalidProvider_ReturnsUnhealthy()
    {
        var probe = await _proxy.ProbeProviderAsync("InvalidProvider");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProviderAndPublishesEvent()
    {
        var result = await _proxy.SwitchProviderAsync("IP2Location");
        result.Should().BeTrue();
        _proxy.ActiveProviderId.Should().Be("IP2Location");
        _proxy.ActiveProvider.Should().BeSameAs(_ip2LocationProvider);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveGeoIpProvider"] == "IP2Location"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<GeoIpProviderSwitchedEvent>(e => e.PreviousProvider == "MaxMind" && e.NewProvider == "IP2Location"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsTrueWithoutWork()
    {
        var result = await _proxy.SwitchProviderAsync("MaxMind");
        result.Should().BeTrue();
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<GeoIpProviderSwitchedEvent>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownProvider_ReturnsFalse()
    {
        var result = await _proxy.SwitchProviderAsync("UnknownProvider");
        result.Should().BeFalse();
        _proxy.ActiveProviderId.Should().Be("MaxMind");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        _ip2LocationProvider.ProbeHealthAsync().Returns(Task.FromResult(new GeoIpHealthResult
        {
            IsHealthy = false,
            StatusMessage = "Database file missing"
        }));

        var result = await _proxy.SwitchProviderAsync("IP2Location");
        result.Should().BeFalse();
        _proxy.ActiveProviderId.Should().Be("MaxMind");
    }

    [Test]
    public async Task LookupAsync_DelegatesToActiveProvider()
    {
        var result = await _proxy.LookupAsync("8.8.8.8");
        result.Should().NotBeNull();
        result.IpAddress.Should().Be("8.8.8.8");
        result.CountryCode.Should().Be("US");
        result.City.Should().Be("Ashburn");

        await _maxMindProvider.Received(1).LookupAsync("8.8.8.8");
    }

    [Test]
    public void Lookup_SynchronousDelegation_Works()
    {
        var result = _proxy.Lookup("1.1.1.1");
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

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        public int RequestCount { get; private set; }

        public MockHttpMessageHandler(string responseContent)
        {
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent)
            };
            return Task.FromResult(response);
        }
    }
}

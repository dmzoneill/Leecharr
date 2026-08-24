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
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class DynamicHttpTransportProxyTest
{
    private IHttpTransportProvider _socketsProvider = null!;
    private IHttpTransportProvider _curlProvider = null!;
    private IHttpTransportProvider _flareSolverrProvider = null!;
    private IConfigService _configService = null!;
    private IEventAggregator _eventAggregator = null!;
    private DynamicHttpTransportProxy _proxy = null!;

    [SetUp]
    public void SetUp()
    {
        _socketsProvider = Substitute.For<IHttpTransportProvider>();
        _socketsProvider.ProviderId.Returns("SocketsHttpHandler");
        _socketsProvider.DisplayName.Returns("Standard SocketsHttpHandler (.NET 10 HTTP/3 QUIC)");
        _socketsProvider.IsAvailable.Returns(true);
        _socketsProvider.Capabilities.Returns(new HttpTransportCapabilities
        {
            SupportsHttp3Quic = true,
            SupportsCustomProxy = true
        });
        _socketsProvider.ProbeHealthAsync().Returns(Task.FromResult(new HttpTransportHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _socketsProvider.SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        _curlProvider = Substitute.For<IHttpTransportProvider>();
        _curlProvider.ProviderId.Returns("CurlImpersonate");
        _curlProvider.DisplayName.Returns("curl-impersonate (Chrome / Firefox TLS JA3/JA4 Fingerprint)");
        _curlProvider.IsAvailable.Returns(true);
        _curlProvider.Capabilities.Returns(new HttpTransportCapabilities
        {
            SupportsHttp3Quic = true,
            SupportsBrowserFingerprintEmulation = true,
            SupportsTlsJa3Ja4Fingerprinting = true
        });
        _curlProvider.ProbeHealthAsync().Returns(Task.FromResult(new HttpTransportHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));
        _curlProvider.SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        _flareSolverrProvider = Substitute.For<IHttpTransportProvider>();
        _flareSolverrProvider.ProviderId.Returns("FlareSolverr");
        _flareSolverrProvider.DisplayName.Returns("FlareSolverr (Cloudflare / DDoS-GUARD Challenge Solver)");
        _flareSolverrProvider.IsAvailable.Returns(true);
        _flareSolverrProvider.Capabilities.Returns(new HttpTransportCapabilities
        {
            SupportsFlareSolverr = true,
            SupportsBrowserFingerprintEmulation = true
        });
        _flareSolverrProvider.ProbeHealthAsync().Returns(Task.FromResult(new HttpTransportHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        _configService = Substitute.For<IConfigService>();
        _configService.ActiveHttpTransportProvider.Returns("SocketsHttpHandler");

        _eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IHttpTransportProvider> { _socketsProvider, _curlProvider, _flareSolverrProvider };

        _proxy = new DynamicHttpTransportProxy(
            providers,
            _configService,
            _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        _proxy.ActiveProviderId.Should().Be("SocketsHttpHandler");
        _proxy.ActiveProvider.Should().BeSameAs(_socketsProvider);
    }

    [Test]
    public void Constructor_WhenConfigEmpty_FallsBackToDefault()
    {
        var config = Substitute.For<IConfigService>();
        config.ActiveHttpTransportProvider.Returns(string.Empty);

        using var proxy = new DynamicHttpTransportProxy(
            new[] { _socketsProvider, _curlProvider },
            config,
            _eventAggregator);

        proxy.ActiveProviderId.Should().Be("SocketsHttpHandler");
    }

    [Test]
    public void Constructor_WhenNoProviders_ThrowsInvalidOperationException()
    {
        var act = () => new DynamicHttpTransportProxy(
            Enumerable.Empty<IHttpTransportProvider>(),
            _configService,
            _eventAggregator);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = _proxy.GetProviders().ToList();
        providers.Should().HaveCount(3);
        providers.Select(p => p.ProviderId).Should().Contain(new[] { "SocketsHttpHandler", "CurlImpersonate", "FlareSolverr" });
    }

    [Test]
    public void GetProvider_WithValidId_ReturnsMatchingProvider()
    {
        var provider = _proxy.GetProvider("curlimpersonate");
        provider.Should().NotBeNull();
        provider!.ProviderId.Should().Be("CurlImpersonate");
    }

    [Test]
    public void GetProvider_WithInvalidOrEmptyId_ReturnsNull()
    {
        _proxy.GetProvider("NonExistent").Should().BeNull();
        _proxy.GetProvider(string.Empty).Should().BeNull();
        _proxy.GetProvider(null).Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_WithValidProvider_ReturnsHealthResult()
    {
        var probe = await _proxy.ProbeProviderAsync("CurlImpersonate");
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
    public async Task SwitchProviderAsync_SwitchesActiveProviderAndPersistsConfig()
    {
        var result = await _proxy.SwitchProviderAsync("CurlImpersonate");

        result.Success.Should().BeTrue();
        result.PreviousProvider.Should().Be("SocketsHttpHandler");
        result.ActiveProvider.Should().Be("CurlImpersonate");

        _proxy.ActiveProviderId.Should().Be("CurlImpersonate");
        _proxy.ActiveProvider.Should().BeSameAs(_curlProvider);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveHttpTransportProvider"] == "CurlImpersonate"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<HttpTransportProviderSwitchedEvent>(e => e.PreviousProvider == "SocketsHttpHandler" && e.NewProvider == "CurlImpersonate"));
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await _proxy.SwitchProviderAsync("SocketsHttpHandler");

        result.Success.Should().BeTrue();
        result.ActiveProvider.Should().Be("SocketsHttpHandler");

        _configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public async Task SwitchProviderAsync_WithUnknownOrEmptyProvider_ReturnsFailure()
    {
        var result1 = await _proxy.SwitchProviderAsync("UnknownProvider");
        result1.Success.Should().BeFalse();
        result1.Error.Should().Contain("not registered");

        var result2 = await _proxy.SwitchProviderAsync(string.Empty);
        result2.Success.Should().BeFalse();
        result2.Error.Should().Contain("empty");

        _proxy.ActiveProviderId.Should().Be("SocketsHttpHandler");
    }

    [Test]
    public async Task SwitchProviderAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        _curlProvider.ProbeHealthAsync().Returns(Task.FromResult(new HttpTransportHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "curl binary missing"
        }));

        var result = await _proxy.SwitchProviderAsync("CurlImpersonate");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        _proxy.ActiveProviderId.Should().Be("SocketsHttpHandler");
    }

    [Test]
    public async Task Delegation_ForwardsSendAsyncToActiveProvider()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var response = await _proxy.SendAsync(request);

        response.Should().NotBeNull();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _socketsProvider.Received(1).SendAsync(request, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConcreteProviders_SocketsHttpHandlerProvider_Tests()
    {
        using var provider = new SocketsHttpHandlerProvider();
        provider.ProviderId.Should().Be("SocketsHttpHandler");
        provider.DisplayName.Should().NotBeNullOrEmpty();
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.SupportsHttp3Quic.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
    }

    [Test]
    public async Task ConcreteProviders_CurlImpersonateTransportProvider_Tests()
    {
        using var provider = new CurlImpersonateTransportProvider();
        provider.ProviderId.Should().Be("CurlImpersonate");
        provider.DisplayName.Should().NotBeNullOrEmpty();
        provider.Capabilities.SupportsBrowserFingerprintEmulation.Should().BeTrue();
        provider.Capabilities.SupportsTlsJa3Ja4Fingerprinting.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        // Test header injection
        request.Headers.Contains("User-Agent").Should().BeFalse();

        // Testing SendAsync should add browser headers
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await provider.SendAsync(request, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        request.Headers.Contains("User-Agent").Should().BeTrue();
        request.Headers.Contains("Sec-Ch-Ua").Should().BeTrue();
    }

    [Test]
    public async Task ConcreteProviders_FlareSolverrTransportProvider_Tests()
    {
        var config = Substitute.For<IConfigService>();
        config.GetValue("FlareSolverrUrl", Arg.Any<string>()).Returns("http://127.0.0.1:8191/v1");

        using var provider = new FlareSolverrTransportProvider(config);
        provider.ProviderId.Should().Be("FlareSolverr");
        provider.DisplayName.Should().NotBeNullOrEmpty();
        provider.Capabilities.SupportsFlareSolverr.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
    }
}

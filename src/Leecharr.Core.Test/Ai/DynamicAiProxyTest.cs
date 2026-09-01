using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Ai;

[TestFixture]
public class DynamicAiProxyTest
{
    private IAiEngineProvider _ruleProvider = null!;
    private IAiEngineProvider _onnxProvider = null!;
    private IAiEngineProvider _ollamaProvider = null!;
    private IAiEngineProvider _geminiProvider = null!;
    private IConfigService _configService = null!;
    private IEventAggregator _eventAggregator = null!;
    private DynamicAiProxy _proxy = null!;

    [SetUp]
    public void SetUp()
    {
        _ruleProvider = Substitute.For<IAiEngineProvider>();
        _ruleProvider.ProviderId.Returns("RuleHeuristic");
        _ruleProvider.DisplayName.Returns("Rule-Based Heuristic AI");
        _ruleProvider.Version.Returns("1.0");
        _ruleProvider.IsAvailable.Returns(true);
        _ruleProvider.Capabilities.Returns(AiCapabilities.SupportsReleaseNameParsing | AiCapabilities.SupportsDiagnosticCopilot | AiCapabilities.SupportsNaturalLanguageSearch);
        _ruleProvider.ProbeHealthAsync().Returns(Task.FromResult(new AiHealthResult { IsHealthy = true, StatusMessage = "OK", ModelName = "Deterministic" }));
        _ruleProvider.ParseReleaseAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            return Task.FromResult(new AiParsedRelease { RawTitle = name, CleanTitle = "Parsed Heuristic", Resolution = "1080p", ConfidenceScore = 0.9 });
        });
        _ruleProvider.DiagnoseTorrentHealthAsync(Arg.Any<Torrent>(), Arg.Any<IReadOnlyList<PeerInfo>>(), Arg.Any<IReadOnlyList<TrackerEntry>>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            return Task.FromResult(new AiDiagnosticReport { TorrentId = t?.Id ?? 1, OverallHealth = "Healthy", Severity = "None", HealthScore = 95.0 });
        });
        _ruleProvider.ProcessNaturalLanguageSearchAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var q = callInfo.Arg<string>();
            return Task.FromResult(new AiSearchParameters { RawQuery = q, CleanTitle = "Breaking Bad", Resolution = "1080p", MinSeeders = 5 });
        });
        _ruleProvider.AnalyzeMalwareRiskAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TorrentFile>>()).Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            return Task.FromResult(new AiMalwareRiskAssessment { TorrentName = name, RiskScore = 0.0, RiskLevel = "Safe", IsSuspicious = false });
        });
        _ruleProvider.GenerateChatResponseAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult("Rule heuristic assistant response"));

        _onnxProvider = Substitute.For<IAiEngineProvider>();
        _onnxProvider.ProviderId.Returns("OnnxLocal");
        _onnxProvider.DisplayName.Returns("Local ONNX Engine");
        _onnxProvider.Version.Returns("1.0");
        _onnxProvider.IsAvailable.Returns(true);
        _onnxProvider.Capabilities.Returns(AiCapabilities.All);
        _onnxProvider.ProbeHealthAsync().Returns(Task.FromResult(new AiHealthResult { IsHealthy = true, StatusMessage = "OK", ModelName = "ONNX-v1" }));
        _onnxProvider.ParseReleaseAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            return Task.FromResult(new AiParsedRelease { RawTitle = name, CleanTitle = "Parsed ONNX", Resolution = "2160p", ConfidenceScore = 0.95 });
        });

        _ollamaProvider = Substitute.For<IAiEngineProvider>();
        _ollamaProvider.ProviderId.Returns("Ollama");
        _ollamaProvider.DisplayName.Returns("Ollama Local LLM");
        _ollamaProvider.Version.Returns("1.0");
        _ollamaProvider.IsAvailable.Returns(true);
        _ollamaProvider.Capabilities.Returns(AiCapabilities.All);
        _ollamaProvider.ProbeHealthAsync().Returns(Task.FromResult(new AiHealthResult { IsHealthy = true, StatusMessage = "OK", ModelName = "llama3.2" }));

        _geminiProvider = Substitute.For<IAiEngineProvider>();
        _geminiProvider.ProviderId.Returns("Gemini");
        _geminiProvider.DisplayName.Returns("Google Gemini Cloud LLM");
        _geminiProvider.Version.Returns("1.0");
        _geminiProvider.IsAvailable.Returns(true);
        _geminiProvider.Capabilities.Returns(AiCapabilities.SupportsCloudLlm | AiCapabilities.SupportsReleaseNameParsing);
        _geminiProvider.ProbeHealthAsync().Returns(Task.FromResult(new AiHealthResult { IsHealthy = false, StatusMessage = "Missing API Key" }));

        _configService = Substitute.For<IConfigService>();
        _configService.GetValue("ActiveAiProvider", Arg.Any<string>()).Returns("RuleHeuristic");

        _eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IAiEngineProvider> { _ruleProvider, _onnxProvider, _ollamaProvider, _geminiProvider };
        _proxy = new DynamicAiProxy(providers, _configService, _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        _proxy.ActiveProviderId.Should().Be("RuleHeuristic");
        _proxy.ActiveProvider.Should().BeSameAs(_ruleProvider);
    }

    [Test]
    public void Constructor_ThrowsWhenNoProvidersRegistered()
    {
        var action = () => new DynamicAiProxy(new List<IAiEngineProvider>(), _configService, _eventAggregator);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*No AI providers*");
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = _proxy.GetProviders();
        providers.Should().HaveCount(4);
    }

    [Test]
    public void GetProvider_ReturnsMatchingProvider_CaseInsensitive()
    {
        var p = _proxy.GetProvider("onnxlocal");
        p.Should().NotBeNull();
        p.ProviderId.Should().Be("OnnxLocal");
    }

    [Test]
    public void GetProvider_ReturnsNullForUnknownProvider()
    {
        var p = _proxy.GetProvider("UnknownAI");
        p.Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_ReturnsHealthy_ForRegisteredProvider()
    {
        var result = await _proxy.ProbeProviderAsync("OnnxLocal");
        result.IsHealthy.Should().BeTrue();
        result.ModelName.Should().Be("ONNX-v1");
    }

    [Test]
    public async Task ProbeProviderAsync_ReturnsUnhealthy_ForUnknownProvider()
    {
        var result = await _proxy.ProbeProviderAsync("NonExistent");
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProvider_AndPublishesEvent()
    {
        var switched = await _proxy.SwitchProviderAsync("OnnxLocal");

        switched.Should().BeTrue();
        _proxy.ActiveProviderId.Should().Be("OnnxLocal");
        _proxy.ActiveProvider.Should().BeSameAs(_onnxProvider);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveAiProvider"] == "OnnxLocal"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<AiProviderSwitchedEvent>(e => e.PreviousProvider == "RuleHeuristic" && e.NewProvider == "OnnxLocal"));
    }

    [Test]
    public async Task SwitchProviderAsync_ReturnsTrue_WhenAlreadyActive()
    {
        var switched = await _proxy.SwitchProviderAsync("RuleHeuristic");
        switched.Should().BeTrue();
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<AiProviderSwitchedEvent>());
    }

    [Test]
    public async Task SwitchProviderAsync_Fails_WhenTargetUnhealthy()
    {
        var switched = await _proxy.SwitchProviderAsync("Gemini");

        switched.Should().BeFalse();
        _proxy.ActiveProviderId.Should().Be("RuleHeuristic");
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<AiProviderSwitchedEvent>());
    }

    [Test]
    public async Task SwitchProviderAsync_Fails_WhenTargetNotFound()
    {
        var switched = await _proxy.SwitchProviderAsync("ImaginaryProvider");
        switched.Should().BeFalse();
    }

    [Test]
    public async Task ParseReleaseAsync_DelegatesToActiveProvider()
    {
        var parsed = await _proxy.ParseReleaseAsync("Breaking.Bad.S01E01.1080p.mkv");
        parsed.CleanTitle.Should().Be("Parsed Heuristic");
        parsed.Resolution.Should().Be("1080p");
    }

    [Test]
    public void ParseRelease_Synchronous_DelegatesToActiveProvider()
    {
        var parsed = _proxy.ParseRelease("Breaking.Bad.S01E01.1080p.mkv");
        parsed.CleanTitle.Should().Be("Parsed Heuristic");
    }

    [Test]
    public async Task DiagnoseTorrentHealthAsync_DelegatesToActiveProvider()
    {
        var torrent = new Torrent { Id = 42, Name = "Test Torrent" };
        var report = await _proxy.DiagnoseTorrentHealthAsync(torrent, Array.Empty<PeerInfo>(), Array.Empty<TrackerEntry>());

        report.TorrentId.Should().Be(42);
        report.OverallHealth.Should().Be("Healthy");
        report.HealthScore.Should().Be(95.0);
    }

    [Test]
    public void DiagnoseTorrentHealth_Synchronous_DelegatesToActiveProvider()
    {
        var torrent = new Torrent { Id = 42, Name = "Test Torrent" };
        var report = _proxy.DiagnoseTorrentHealth(torrent, Array.Empty<PeerInfo>(), Array.Empty<TrackerEntry>());
        report.TorrentId.Should().Be(42);
    }

    [Test]
    public async Task ProcessNaturalLanguageSearchAsync_DelegatesToActiveProvider()
    {
        var searchParams = await _proxy.ProcessNaturalLanguageSearchAsync("download breaking bad season 2 in 1080p");
        searchParams.CleanTitle.Should().Be("Breaking Bad");
        searchParams.Resolution.Should().Be("1080p");
        searchParams.MinSeeders.Should().Be(5);
    }

    [Test]
    public void ProcessNaturalLanguageSearch_Synchronous_DelegatesToActiveProvider()
    {
        var searchParams = _proxy.ProcessNaturalLanguageSearch("download breaking bad");
        searchParams.CleanTitle.Should().Be("Breaking Bad");
    }

    [Test]
    public async Task AnalyzeMalwareRiskAsync_DelegatesToActiveProvider()
    {
        var assessment = await _proxy.AnalyzeMalwareRiskAsync("CleanRelease.1080p", new List<TorrentFile>());
        assessment.RiskLevel.Should().Be("Safe");
        assessment.IsSuspicious.Should().BeFalse();
    }

    [Test]
    public void AnalyzeMalwareRisk_Synchronous_DelegatesToActiveProvider()
    {
        var assessment = _proxy.AnalyzeMalwareRisk("CleanRelease.1080p", new List<TorrentFile>());
        assessment.RiskLevel.Should().Be("Safe");
    }

    [Test]
    public async Task GenerateChatResponseAsync_DelegatesToActiveProvider()
    {
        var response = await _proxy.GenerateChatResponseAsync("How do I improve seed ratio?");
        response.Should().Be("Rule heuristic assistant response");
    }

    [Test]
    public void GenerateChatResponse_Synchronous_DelegatesToActiveProvider()
    {
        var response = _proxy.GenerateChatResponse("How do I improve seed ratio?");
        response.Should().Be("Rule heuristic assistant response");
    }

    [Test]
    public async Task FallbackHandling_WhenActiveProviderThrows_FallsBackGracefully()
    {
        _ruleProvider.ParseReleaseAsync(Arg.Any<string>()).Throws(new InvalidOperationException("Simulated provider failure"));

        var result = await _proxy.ParseReleaseAsync("Inception.2010.1080p.BluRay.x264-SPARKS");
        result.Should().NotBeNull();
        result.Year.Should().Be(2010);
        result.Resolution.Should().Be("1080p");
    }
}

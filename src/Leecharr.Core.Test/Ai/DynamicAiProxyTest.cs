// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private IAiEngineProvider ruleProvider = null!;
    private IAiEngineProvider onnxProvider = null!;
    private IAiEngineProvider ollamaProvider = null!;
    private IAiEngineProvider geminiProvider = null!;
    private IConfigService configService = null!;
    private IEventAggregator eventAggregator = null!;
    private DynamicAiProxy proxy = null!;

    [SetUp]
    public void SetUp()
    {
        this.ruleProvider = Substitute.For<IAiEngineProvider>();
        this.ruleProvider.ProviderId.Returns("RuleHeuristic");
        this.ruleProvider.DisplayName.Returns("Rule-Based Heuristic AI");
        this.ruleProvider.Version.Returns("1.0");
        this.ruleProvider.IsAvailable.Returns(true);
        this.ruleProvider.Capabilities.Returns(AiCapabilities.SupportsReleaseNameParsing | AiCapabilities.SupportsDiagnosticCopilot | AiCapabilities.SupportsNaturalLanguageSearch);
        this.ruleProvider.ProbeHealthAsync().Returns(Task.FromResult(new AiHealthResult { IsHealthy = true, StatusMessage = "OK", ModelName = "Deterministic" }));
        this.ruleProvider.ParseReleaseAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            return Task.FromResult(new AiParsedRelease { RawTitle = name, CleanTitle = "Parsed Heuristic", Resolution = "1080p", ConfidenceScore = 0.9 });
        });
        this.ruleProvider.DiagnoseTorrentHealthAsync(Arg.Any<Torrent>(), Arg.Any<IReadOnlyList<PeerInfo>>(), Arg.Any<IReadOnlyList<TrackerEntry>>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Torrent>();
            return Task.FromResult(new AiDiagnosticReport { TorrentId = t?.Id ?? 1, OverallHealth = "Healthy", Severity = "None", HealthScore = 95.0 });
        });
        this.ruleProvider.ProcessNaturalLanguageSearchAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var q = callInfo.Arg<string>();
            return Task.FromResult(new AiSearchParameters { RawQuery = q, CleanTitle = "Breaking Bad", Resolution = "1080p", MinSeeders = 5 });
        });
        this.ruleProvider.AnalyzeMalwareRiskAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TorrentFile>>()).Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            return Task.FromResult(new AiMalwareRiskAssessment { TorrentName = name, RiskScore = 0.0, RiskLevel = "Safe", IsSuspicious = false });
        });
        this.ruleProvider.GenerateChatResponseAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult("Rule heuristic assistant response"));

        this.onnxProvider = Substitute.For<IAiEngineProvider>();
        this.onnxProvider.ProviderId.Returns("OnnxLocal");
        this.onnxProvider.DisplayName.Returns("Local ONNX Engine");
        this.onnxProvider.Version.Returns("1.0");
        this.onnxProvider.IsAvailable.Returns(true);
        this.onnxProvider.Capabilities.Returns(AiCapabilities.All);
        this.onnxProvider.ProbeHealthAsync().Returns(Task.FromResult(new AiHealthResult { IsHealthy = true, StatusMessage = "OK", ModelName = "ONNX-v1" }));
        this.onnxProvider.ParseReleaseAsync(Arg.Any<string>()).Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            return Task.FromResult(new AiParsedRelease { RawTitle = name, CleanTitle = "Parsed ONNX", Resolution = "2160p", ConfidenceScore = 0.95 });
        });

        this.ollamaProvider = Substitute.For<IAiEngineProvider>();
        this.ollamaProvider.ProviderId.Returns("Ollama");
        this.ollamaProvider.DisplayName.Returns("Ollama Local LLM");
        this.ollamaProvider.Version.Returns("1.0");
        this.ollamaProvider.IsAvailable.Returns(true);
        this.ollamaProvider.Capabilities.Returns(AiCapabilities.All);
        this.ollamaProvider.ProbeHealthAsync().Returns(Task.FromResult(new AiHealthResult { IsHealthy = true, StatusMessage = "OK", ModelName = "llama3.2" }));

        this.geminiProvider = Substitute.For<IAiEngineProvider>();
        this.geminiProvider.ProviderId.Returns("Gemini");
        this.geminiProvider.DisplayName.Returns("Google Gemini Cloud LLM");
        this.geminiProvider.Version.Returns("1.0");
        this.geminiProvider.IsAvailable.Returns(true);
        this.geminiProvider.Capabilities.Returns(AiCapabilities.SupportsCloudLlm | AiCapabilities.SupportsReleaseNameParsing);
        this.geminiProvider.ProbeHealthAsync().Returns(Task.FromResult(new AiHealthResult { IsHealthy = false, StatusMessage = "Missing API Key" }));

        this.configService = Substitute.For<IConfigService>();
        this.configService.GetValue("ActiveAiProvider", Arg.Any<string>()).Returns("RuleHeuristic");

        this.eventAggregator = Substitute.For<IEventAggregator>();

        var providers = new List<IAiEngineProvider> { this.ruleProvider, this.onnxProvider, this.ollamaProvider, this.geminiProvider };
        this.proxy = new DynamicAiProxy(providers, this.configService, this.eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredProvider()
    {
        this.proxy.ActiveProviderId.Should().Be("RuleHeuristic");
        this.proxy.ActiveProvider.Should().BeSameAs(this.ruleProvider);
    }

    [Test]
    public void Constructor_ThrowsWhenNoProvidersRegistered()
    {
        var action = () => new DynamicAiProxy(new List<IAiEngineProvider>(), this.configService, this.eventAggregator);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*No AI providers*");
    }

    [Test]
    public void GetProviders_ReturnsAllRegisteredProviders()
    {
        var providers = this.proxy.GetProviders();
        providers.Should().HaveCount(4);
    }

    [Test]
    public void GetProvider_ReturnsMatchingProvider_CaseInsensitive()
    {
        var p = this.proxy.GetProvider("onnxlocal");
        p.Should().NotBeNull();
        p.ProviderId.Should().Be("OnnxLocal");
    }

    [Test]
    public void GetProvider_ReturnsNullForUnknownProvider()
    {
        var p = this.proxy.GetProvider("UnknownAI");
        p.Should().BeNull();
    }

    [Test]
    public async Task ProbeProviderAsync_ReturnsHealthy_ForRegisteredProvider()
    {
        var result = await this.proxy.ProbeProviderAsync("OnnxLocal");
        result.IsHealthy.Should().BeTrue();
        result.ModelName.Should().Be("ONNX-v1");
    }

    [Test]
    public async Task ProbeProviderAsync_ReturnsUnhealthy_ForUnknownProvider()
    {
        var result = await this.proxy.ProbeProviderAsync("NonExistent");
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchProviderAsync_SwitchesActiveProvider_AndPublishesEvent()
    {
        var switched = await this.proxy.SwitchProviderAsync("OnnxLocal");

        switched.Should().BeTrue();
        this.proxy.ActiveProviderId.Should().Be("OnnxLocal");
        this.proxy.ActiveProvider.Should().BeSameAs(this.onnxProvider);

        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveAiProvider"] == "OnnxLocal"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<AiProviderSwitchedEvent>(e => e.PreviousProvider == "RuleHeuristic" && e.NewProvider == "OnnxLocal"));
    }

    [Test]
    public async Task SwitchProviderAsync_ReturnsTrue_WhenAlreadyActive()
    {
        var switched = await this.proxy.SwitchProviderAsync("RuleHeuristic");
        switched.Should().BeTrue();
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<AiProviderSwitchedEvent>());
    }

    [Test]
    public async Task SwitchProviderAsync_Fails_WhenTargetUnhealthy()
    {
        var switched = await this.proxy.SwitchProviderAsync("Gemini");

        switched.Should().BeFalse();
        this.proxy.ActiveProviderId.Should().Be("RuleHeuristic");
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<AiProviderSwitchedEvent>());
    }

    [Test]
    public async Task SwitchProviderAsync_Fails_WhenTargetNotFound()
    {
        var switched = await this.proxy.SwitchProviderAsync("ImaginaryProvider");
        switched.Should().BeFalse();
    }

    [Test]
    public async Task ParseReleaseAsync_DelegatesToActiveProvider()
    {
        var parsed = await this.proxy.ParseReleaseAsync("Breaking.Bad.S01E01.1080p.mkv");
        parsed.CleanTitle.Should().Be("Parsed Heuristic");
        parsed.Resolution.Should().Be("1080p");
    }

    [Test]
    public void ParseRelease_Synchronous_DelegatesToActiveProvider()
    {
        var parsed = this.proxy.ParseRelease("Breaking.Bad.S01E01.1080p.mkv");
        parsed.CleanTitle.Should().Be("Parsed Heuristic");
    }

    [Test]
    public async Task DiagnoseTorrentHealthAsync_DelegatesToActiveProvider()
    {
        var torrent = new Torrent { Id = 42, Name = "Test Torrent" };
        var report = await this.proxy.DiagnoseTorrentHealthAsync(torrent, Array.Empty<PeerInfo>(), Array.Empty<TrackerEntry>());

        report.TorrentId.Should().Be(42);
        report.OverallHealth.Should().Be("Healthy");
        report.HealthScore.Should().Be(95.0);
    }

    [Test]
    public void DiagnoseTorrentHealth_Synchronous_DelegatesToActiveProvider()
    {
        var torrent = new Torrent { Id = 42, Name = "Test Torrent" };
        var report = this.proxy.DiagnoseTorrentHealth(torrent, Array.Empty<PeerInfo>(), Array.Empty<TrackerEntry>());
        report.TorrentId.Should().Be(42);
    }

    [Test]
    public async Task ProcessNaturalLanguageSearchAsync_DelegatesToActiveProvider()
    {
        var searchParams = await this.proxy.ProcessNaturalLanguageSearchAsync("download breaking bad season 2 in 1080p");
        searchParams.CleanTitle.Should().Be("Breaking Bad");
        searchParams.Resolution.Should().Be("1080p");
        searchParams.MinSeeders.Should().Be(5);
    }

    [Test]
    public void ProcessNaturalLanguageSearch_Synchronous_DelegatesToActiveProvider()
    {
        var searchParams = this.proxy.ProcessNaturalLanguageSearch("download breaking bad");
        searchParams.CleanTitle.Should().Be("Breaking Bad");
    }

    [Test]
    public async Task AnalyzeMalwareRiskAsync_DelegatesToActiveProvider()
    {
        var assessment = await this.proxy.AnalyzeMalwareRiskAsync("CleanRelease.1080p", new List<TorrentFile>());
        assessment.RiskLevel.Should().Be("Safe");
        assessment.IsSuspicious.Should().BeFalse();
    }

    [Test]
    public void AnalyzeMalwareRisk_Synchronous_DelegatesToActiveProvider()
    {
        var assessment = this.proxy.AnalyzeMalwareRisk("CleanRelease.1080p", new List<TorrentFile>());
        assessment.RiskLevel.Should().Be("Safe");
    }

    [Test]
    public async Task GenerateChatResponseAsync_DelegatesToActiveProvider()
    {
        var response = await this.proxy.GenerateChatResponseAsync("How do I improve seed ratio?");
        response.Should().Be("Rule heuristic assistant response");
    }

    [Test]
    public void GenerateChatResponse_Synchronous_DelegatesToActiveProvider()
    {
        var response = this.proxy.GenerateChatResponse("How do I improve seed ratio?");
        response.Should().Be("Rule heuristic assistant response");
    }

    [Test]
    public async Task FallbackHandling_WhenActiveProviderThrows_FallsBackGracefully()
    {
        this.ruleProvider.ParseReleaseAsync(Arg.Any<string>()).Throws(new InvalidOperationException("Simulated provider failure"));

        var result = await this.proxy.ParseReleaseAsync("Inception.2010.1080p.BluRay.x264-SPARKS");
        result.Should().NotBeNull();
        result.Year.Should().Be(2010);
        result.Resolution.Should().Be("1080p");
    }
}

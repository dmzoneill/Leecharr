using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Ai;

[TestFixture]
public class OnnxLocalAiProviderTest
{
    private IConfigService _configService = null!;
    private OnnxLocalAiProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.GetValue("OnnxModelPath", Arg.Any<string>()).Returns("/nonexistent/model.onnx");
        _provider = new OnnxLocalAiProvider(_configService);
    }

    [Test]
    public void Properties_ReturnExpectedValues()
    {
        _provider.ProviderId.Should().Be("OnnxLocal");
        _provider.DisplayName.Should().Contain("ONNX");
        _provider.Version.Should().Be("1.0");
        _provider.IsAvailable.Should().BeTrue();
        _provider.Capabilities.Should().HaveFlag(AiCapabilities.SupportsLocalOfflineInference);
    }

    [Test]
    public async Task ProbeHealthAsync_WhenModelMissing_ReturnsHealthyWithFallbackWarning()
    {
        var health = await _provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.Warnings.Should().NotBeEmpty();
        health.ModelName.Should().Contain("Fallback");
    }

    [Test]
    public async Task ParseReleaseAsync_FallsBackGracefullyAndTagsEngine()
    {
        var result = await _provider.ParseReleaseAsync("Inception.2010.1080p.BluRay.x264-SPARKS");
        result.CleanTitle.Should().Be("Inception");
        result.Year.Should().Be(2010);
        result.Resolution.Should().Be("1080p");
        result.AdditionalTags.Should().ContainKey("Engine");
        result.AdditionalTags["Engine"].Should().Be("OnnxLocal");
    }

    [Test]
    public async Task AnalyzeMalwareRiskAsync_DelegatesToHeuristics()
    {
        var assessment = await _provider.AnalyzeMalwareRiskAsync("Clean.2024.1080p", new List<TorrentFile>());
        assessment.RiskLevel.Should().Be("Safe");
    }
}

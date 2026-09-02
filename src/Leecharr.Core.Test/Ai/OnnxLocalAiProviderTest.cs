// Copyright (c) PlaceholderCompany. All rights reserved.

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
    private IConfigService configService = null!;
    private OnnxLocalAiProvider provider = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.configService.GetValue("OnnxModelPath", Arg.Any<string>()).Returns("/nonexistent/model.onnx");
        this.provider = new OnnxLocalAiProvider(this.configService);
    }

    [Test]
    public void Properties_ReturnExpectedValues()
    {
        this.provider.ProviderId.Should().Be("OnnxLocal");
        this.provider.DisplayName.Should().Contain("ONNX");
        this.provider.Version.Should().Be("1.0");
        this.provider.IsAvailable.Should().BeTrue();
        this.provider.Capabilities.Should().HaveFlag(AiCapabilities.SupportsLocalOfflineInference);
    }

    [Test]
    public async Task ProbeHealthAsync_WhenModelMissing_ReturnsHealthyWithFallbackWarning()
    {
        var health = await this.provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.Warnings.Should().NotBeEmpty();
        health.ModelName.Should().Contain("Fallback");
    }

    [Test]
    public async Task ParseReleaseAsync_FallsBackGracefullyAndTagsEngine()
    {
        var result = await this.provider.ParseReleaseAsync("Inception.2010.1080p.BluRay.x264-SPARKS");
        result.CleanTitle.Should().Be("Inception");
        result.Year.Should().Be(2010);
        result.Resolution.Should().Be("1080p");
        result.AdditionalTags.Should().ContainKey("Engine");
        result.AdditionalTags["Engine"].Should().Be("OnnxLocal");
    }

    [Test]
    public async Task AnalyzeMalwareRiskAsync_DelegatesToHeuristics()
    {
        var assessment = await this.provider.AnalyzeMalwareRiskAsync("Clean.2024.1080p", new List<TorrentFile>());
        assessment.RiskLevel.Should().Be("Safe");
    }
}

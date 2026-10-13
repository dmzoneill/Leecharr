// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
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
    public void Properties_DoNotAdvertiseUnimplementedInference()
    {
        this.provider.ProviderId.Should().Be("OnnxLocal");
        this.provider.DisplayName.Should().Contain("ONNX");
        this.provider.Version.Should().Be("1.0");
        this.provider.IsAvailable.Should().BeFalse();
        this.provider.LastChatUsedFallback.Should().BeTrue();
        this.provider.Capabilities.Should().Be(AiCapabilities.None);
        this.provider.Capabilities.Should().NotHaveFlag(AiCapabilities.SupportsNaturalLanguageSearch);
        this.provider.Capabilities.Should().NotHaveFlag(AiCapabilities.SupportsReleaseNameParsing);
        this.provider.Capabilities.Should().NotHaveFlag(AiCapabilities.SupportsDiagnosticCopilot);
        this.provider.Capabilities.Should().NotHaveFlag(AiCapabilities.SupportsMalwareAnomalyDetection);
        this.provider.Capabilities.Should().NotHaveFlag(AiCapabilities.SupportsLocalOfflineInference);
    }

    [Test]
    public async Task ProbeHealthAsync_WhenModelMissing_IsUnhealthy()
    {
        var health = await this.provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.Warnings.Should().NotBeEmpty();
        health.StatusMessage.Should().NotContain("loaded successfully");
        health.ModelName.Should().Be("ONNX-Not-Implemented");
    }

    [Test]
    public async Task ProbeHealthAsync_WhenModelFileExists_DoesNotClaimTheModelLoaded()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), $"leecharr-onnx-{Guid.NewGuid():N}.onnx");
        File.WriteAllText(modelPath, "not-an-onnx-model");
        try
        {
            this.configService.GetValue("OnnxModelPath", Arg.Any<string>()).Returns(modelPath);
            var health = await this.provider.ProbeHealthAsync();
            health.IsHealthy.Should().BeFalse();
            health.StatusMessage.Should().NotContain("loaded successfully");
            health.Warnings.Should().Contain(w => w.Contains(modelPath, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(modelPath);
        }
    }

    [Test]
    public async Task ParseReleaseAsync_UsesHeuristicsWithoutStampingOnnx()
    {
        var result = await this.provider.ParseReleaseAsync("Inception.2010.1080p.BluRay.x264-SPARKS");
        result.CleanTitle.Should().Be("Inception");
        result.Year.Should().Be(2010);
        result.Resolution.Should().Be("1080p");
        result.AdditionalTags["Engine"].Should().Be("RuleHeuristic");
        result.AdditionalTags["Engine"].Should().NotBe("OnnxLocal");
    }

    [Test]
    public async Task AnalyzeMalwareRiskAsync_DelegatesToHeuristics()
    {
        var assessment = await this.provider.AnalyzeMalwareRiskAsync("Clean.2024.1080p", new List<TorrentFile>());
        assessment.RiskLevel.Should().Be("Safe");
    }
}

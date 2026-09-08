// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Ai;

[TestFixture]
public class OllamaAiProviderTest
{
    private IConfigService configService = null!;
    private string originalEnvHost;

    [SetUp]
    public void SetUp()
    {
        this.originalEnvHost = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        Environment.SetEnvironmentVariable("OLLAMA_HOST", null);

        this.configService = Substitute.For<IConfigService>();
        this.configService.OllamaHost.Returns("http://127.0.0.1:11434");
        this.configService.OllamaModel.Returns("llama3.2");
        this.configService.GetValue("OllamaUrl", Arg.Any<string>()).Returns("http://127.0.0.1:11434");
        this.configService.GetValue("OllamaHost", Arg.Any<string>()).Returns("http://127.0.0.1:11434");
        this.configService.GetValue("OllamaModel", Arg.Any<string>()).Returns("llama3.2");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("OLLAMA_HOST", this.originalEnvHost);
    }

    [Test]
    public void Properties_ReturnExpectedValues()
    {
        using var provider = new OllamaAiProvider(this.configService);
        provider.ProviderId.Should().Be("Ollama");
        provider.DisplayName.Should().Contain("Ollama");
        provider.Version.Should().Be("1.0");
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.Should().HaveFlag(AiCapabilities.SupportsLocalOfflineInference);
        provider.Capabilities.Should().NotHaveFlag(AiCapabilities.SupportsCloudLlm);
    }

    [Test]
    public async Task ProbeHealthAsync_WhenReachable_ReturnsHealthy()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"version\": \"0.3.12\"}"),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new OllamaAiProvider(this.configService, client);

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.ModelName.Should().Be("llama3.2");
    }

    [Test]
    public async Task ProbeHealthAsync_WhenUnreachable_ReturnsUnhealthy()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            throw new HttpRequestException("Connection refused");
        });

        using var client = new HttpClient(handler);
        using var provider = new OllamaAiProvider(this.configService, client);

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.Warnings.Should().NotBeEmpty();
    }

    [Test]
    public async Task GenerateChatResponseAsync_WhenOllamaSucceeds_ReturnsGeneratedText()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"model\":\"llama3.2\",\"response\":\"To fix a stalled torrent, check seeders.\",\"done\":true}"),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new OllamaAiProvider(this.configService, client);

        var response = await provider.GenerateChatResponseAsync("How do I fix stalled torrents?");
        response.Should().Be("To fix a stalled torrent, check seeders.");
    }

    [Test]
    public async Task GenerateChatResponseAsync_WhenOllamaFails_FallsBackToHeuristics()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            throw new HttpRequestException("Sidecar offline");
        });

        using var client = new HttpClient(handler);
        using var provider = new OllamaAiProvider(this.configService, client);

        var response = await provider.GenerateChatResponseAsync("Tell me about ratio");
        response.Should().Contain("Ratio");
    }

    [Test]
    public async Task ParseReleaseAsync_WhenOllamaReturnsJson_ParsesAllFields()
    {
        var responseJson = @"{
            ""model"": ""llama3.2"",
            ""response"": ""{\""cleanTitle\"":\""Interstellar\"",\""year\"":2014,\""resolution\"":\""2160p\"",\""quality\"":\""2160p UHD BluRay\"",\""source\"":\""BluRay\"",\""videoCodec\"":\""x265\"",\""audioCodec\"":\""DTS-HD MA\"",\""audioChannels\"":\""5.1\"",\""dynamicRange\"":\""HDR10\"",\""edition\"":\""IMAX\"",\""releaseGroup\"":\""SPARKS\"",\""isProper\"":false,\""isRepack\"":false,\""isRemux\"":false,\""languages\"":[\""English\"",\""German\""],\""confidenceScore\"":0.96}"",
            ""done"": true
        }";

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new OllamaAiProvider(this.configService, client);

        var parsed = await provider.ParseReleaseAsync("Interstellar.2014.2160p.UHD.BluRay.x265.HDR.DTS-HD.MA.5.1-SPARKS");
        parsed.CleanTitle.Should().Be("Interstellar");
        parsed.Year.Should().Be(2014);
        parsed.Resolution.Should().Be("2160p");
        parsed.Quality.Should().Be("2160p UHD BluRay");
        parsed.VideoCodec.Should().Be("x265");
        parsed.AudioCodec.Should().Be("DTS-HD MA");
        parsed.AudioChannels.Should().Be("5.1");
        parsed.DynamicRange.Should().Be("HDR10");
        parsed.Edition.Should().Be("IMAX");
        parsed.IsRemux.Should().BeFalse();
        parsed.Languages.Should().Contain(new[] { "English", "German" });
        parsed.ConfidenceScore.Should().Be(0.96);
    }

    [Test]
    public async Task ParseReleaseAsync_WhenBooleansAreStringsNumbersOrNull_ParsesCorrectlyWithoutException()
    {
        var innerJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            cleanTitle = "Interstellar",
            isProper = "true",
            isRepack = 0,
            isRemux = 1,
            confidenceScore = 0.95,
        });

        var responseJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            model = "llama3.2",
            response = innerJson,
            done = true,
        });

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new OllamaAiProvider(this.configService, client);

        var parsed = await provider.ParseReleaseAsync("Interstellar.2014.2160p-SPARKS");
        parsed.CleanTitle.Should().Be("Interstellar");
        parsed.IsProper.Should().BeTrue();
        parsed.IsRepack.Should().BeFalse();
        parsed.IsRemux.Should().BeTrue();
    }

    [Test]
    public async Task ParseReleaseAsync_WhenBooleansAreYesOrNull_ParsesCorrectly()
    {
        var innerJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            cleanTitle = "Interstellar",
            isProper = "yes",
            isRepack = (object)null,
            isRemux = "false",
            confidenceScore = 0.95,
        });

        var responseJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            model = "llama3.2",
            response = innerJson,
            done = true,
        });

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new OllamaAiProvider(this.configService, client);

        var parsed = await provider.ParseReleaseAsync("Interstellar.2014.2160p-SPARKS");
        parsed.CleanTitle.Should().Be("Interstellar");
        parsed.IsProper.Should().BeTrue();
        parsed.IsRepack.Should().BeFalse();
        parsed.IsRemux.Should().BeFalse();
    }

    [Test]
    public async Task AnalyzeMalwareRiskAsync_WhenIsSuspiciousIsNonBooleanOrNull_ParsesCorrectly()
    {
        var innerJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            riskLevel = "Suspicious",
            riskScore = 0.1,
            isSuspicious = "true",
            suspiciousFiles = new[] { "payload.exe" },
            threatReasons = new[] { "Executable found" },
            recommendations = new[] { "Do not run" },
        });

        var responseJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            model = "llama3.2",
            response = innerJson,
            done = true,
        });

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new OllamaAiProvider(this.configService, client);

        var assessment = await provider.AnalyzeMalwareRiskAsync("TestTorrent", Array.Empty<NzbDrone.Core.Torrents.TorrentFile>());
        assessment.IsSuspicious.Should().BeTrue();
        assessment.RiskLevel.Should().Be("Suspicious");
        assessment.SuspiciousFileNames.Should().Contain("payload.exe");
    }

    [Test]
    public async Task AnalyzeMalwareRiskAsync_WhenIsSuspiciousIsNull_FallsBackToRiskScore()
    {
        var innerJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            riskLevel = "Suspicious",
            riskScore = 0.8,
            isSuspicious = (object)null,
            suspiciousFiles = Array.Empty<string>(),
            threatReasons = Array.Empty<string>(),
            recommendations = Array.Empty<string>(),
        });

        var responseJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            model = "llama3.2",
            response = innerJson,
            done = true,
        });

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new OllamaAiProvider(this.configService, client);

        var assessment = await provider.AnalyzeMalwareRiskAsync("TestTorrent", Array.Empty<NzbDrone.Core.Torrents.TorrentFile>());
        assessment.IsSuspicious.Should().BeTrue();
        assessment.RiskScore.Should().Be(0.8);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return this.handler(request, cancellationToken);
        }
    }
}

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
public class CloudGeminiAiProviderTest
{
    private IConfigService configService = null!;
    private string originalEnvKey;

    [SetUp]
    public void SetUp()
    {
        this.originalEnvKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);

        this.configService = Substitute.For<IConfigService>();
        this.configService.GetValue("GeminiApiKey", Arg.Any<string>()).Returns("test-api-key-12345");
        this.configService.GetValue("GeminiModel", Arg.Any<string>()).Returns("gemini-2.0-flash");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", this.originalEnvKey);
    }

    [Test]
    public void Properties_ReturnExpectedValues()
    {
        using var provider = new CloudGeminiAiProvider(this.configService);
        provider.ProviderId.Should().Be("Gemini");
        provider.DisplayName.Should().Contain("Gemini");
        provider.Version.Should().Be("1.0");
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.Should().HaveFlag(AiCapabilities.SupportsCloudLlm);
    }

    [Test]
    public async Task ProbeHealthAsync_WhenKeyConfigured_ReturnsHealthy()
    {
        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);
        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.ModelName.Should().Be("gemini-2.0-flash");
    }

    [Test]
    public async Task ProbeHealthAsync_WhenKeyMissing_ReturnsUnhealthy()
    {
        this.configService.GetValue("GeminiApiKey", Arg.Any<string>()).Returns(string.Empty);
        using var provider = new CloudGeminiAiProvider(this.configService);

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.Warnings.Should().NotBeEmpty();
    }

    [Test]
    public async Task GenerateChatResponseAsync_WhenGeminiResponds_ReturnsCandidatesText()
    {
        var responseJson = @"{
            ""candidates"": [
                {
                    ""content"": {
                        ""parts"": [
                            {
                                ""text"": ""Gemini response: VPN kill switch protects against DNS and IP leaks.""
                            }
                        ]
                    }
                }
            ]
        }";

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);

        var response = await provider.GenerateChatResponseAsync("Explain the VPN kill switch");
        response.Should().Be("Gemini response: VPN kill switch protects against DNS and IP leaks.");
    }

    [Test]
    public async Task GenerateChatResponseAsync_WhenGeminiThrows_FallsBackToHeuristics()
    {
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            throw new HttpRequestException("Gemini API rate limit exceeded");
        });

        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);

        var response = await provider.GenerateChatResponseAsync("Tell me about ratio");
        response.Should().Contain("Ratio");
    }

    [Test]
    public async Task ParseReleaseAsync_WhenGeminiReturnsJson_ParsesAllFields()
    {
        var responseJson = @"{
            ""candidates"": [
                {
                    ""content"": {
                        ""parts"": [
                            {
                                ""text"": ""{\""cleanTitle\"":\""Dune Part Two\"",\""year\"":2024,\""resolution\"":\""2160p\"",\""quality\"":\""2160p Remux\"",\""source\"":\""BluRay\"",\""videoCodec\"":\""HEVC\"",\""audioCodec\"":\""TrueHD Atmos\"",\""audioChannels\"":\""7.1\"",\""dynamicRange\"":\""Dolby Vision\"",\""edition\"":\""Extended\"",\""releaseGroup\"":\""FLUX\"",\""isProper\"":true,\""isRepack\"":false,\""isRemux\"":true,\""languages\"":[\""English\"",\""French\""],\""confidenceScore\"":0.98}""
                            }
                        ]
                    }
                }
            ]
        }";

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);

        var parsed = await provider.ParseReleaseAsync("Dune.Part.Two.2024.2160p.UHD.Remux.DV.TrueHD.Atmos.7.1-FLUX");
        parsed.CleanTitle.Should().Be("Dune Part Two");
        parsed.Year.Should().Be(2024);
        parsed.Resolution.Should().Be("2160p");
        parsed.Quality.Should().Be("2160p Remux");
        parsed.VideoCodec.Should().Be("HEVC");
        parsed.AudioCodec.Should().Be("TrueHD Atmos");
        parsed.AudioChannels.Should().Be("7.1");
        parsed.DynamicRange.Should().Be("Dolby Vision");
        parsed.Edition.Should().Be("Extended");
        parsed.IsRemux.Should().BeTrue();
        parsed.Languages.Should().Contain(new[] { "English", "French" });
        parsed.ConfidenceScore.Should().Be(0.98);
    }

    [Test]
    public async Task ProbeHealthAsync_EscapesModelAndApiKeyInUri()
    {
        this.configService.GetValue("GeminiApiKey", Arg.Any<string>()).Returns("key+123/456=&");
        this.configService.GetValue("GeminiModel", Arg.Any<string>()).Returns("tunedModels/release-parser+v1");

        string requestedUrl = null;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            requestedUrl = req.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        requestedUrl.Should().NotBeNull();
        requestedUrl.Should().Contain("models/tunedModels%2Frelease-parser%2Bv1?key=key%2B123%2F456%3D%26");
    }

    [Test]
    public async Task GenerateChatResponseAsync_EscapesModelAndApiKeyInUri()
    {
        this.configService.GetValue("GeminiApiKey", Arg.Any<string>()).Returns("key+123/456=&");
        this.configService.GetValue("GeminiModel", Arg.Any<string>()).Returns("tunedModels/release-parser+v1");

        string requestedUrl = null;
        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            requestedUrl = req.RequestUri?.ToString();
            var responseJson = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                {
                                    ""text"": ""Hello!""
                                }
                            ]
                        }
                    }
                ]
            }";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);

        var response = await provider.GenerateChatResponseAsync("test");
        response.Should().Be("Hello!");
        requestedUrl.Should().NotBeNull();
        requestedUrl.Should().Contain("models/tunedModels%2Frelease-parser%2Bv1:generateContent?key=key%2B123%2F456%3D%26");
    }

    [Test]
    public async Task ParseReleaseAsync_WhenBooleansAreStringsNumbersOrNull_ParsesCorrectlyWithoutException()
    {
        var innerJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            cleanTitle = "Dune Part Two",
            isProper = "true",
            isRepack = 0,
            isRemux = 1,
            confidenceScore = 0.95,
        });

        var responseJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = innerJson },
                        },
                    },
                },
            },
        });

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);

        var parsed = await provider.ParseReleaseAsync("Dune.Part.Two.2024.2160p-FLUX");
        parsed.CleanTitle.Should().Be("Dune Part Two");
        parsed.IsProper.Should().BeTrue();
        parsed.IsRepack.Should().BeFalse();
        parsed.IsRemux.Should().BeTrue();
    }

    [Test]
    public async Task ParseReleaseAsync_WhenBooleansAreYesOrNull_ParsesCorrectly()
    {
        var innerJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            cleanTitle = "Dune Part Two",
            isProper = "yes",
            isRepack = (object)null,
            isRemux = "false",
            confidenceScore = 0.95,
        });

        var responseJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = innerJson },
                        },
                    },
                },
            },
        });

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);

        var parsed = await provider.ParseReleaseAsync("Dune.Part.Two.2024.2160p-FLUX");
        parsed.CleanTitle.Should().Be("Dune Part Two");
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
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = innerJson },
                        },
                    },
                },
            },
        });

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);

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
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = innerJson },
                        },
                    },
                },
            },
        });

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            });
        });

        using var client = new HttpClient(handler);
        using var provider = new CloudGeminiAiProvider(this.configService, client);

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

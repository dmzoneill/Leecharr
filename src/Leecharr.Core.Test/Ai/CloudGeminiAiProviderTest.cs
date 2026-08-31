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

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
        provider.Capabilities.Should().HaveFlag(AiCapabilities.SupportsCloudLlm | AiCapabilities.SupportsLocalOfflineInference);
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

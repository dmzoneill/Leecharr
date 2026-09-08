// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Config;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Config;

[TestFixture]
public class AiConfigControllerTest
{
    private IConfigService configService = null!;
    private IAiManager aiManager = null!;
    private AiConfigController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.configService.ActiveAiProvider.Returns("RuleHeuristic");
        this.configService.GeminiApiKey.Returns("AIzaSyOldSecretKey123");
        this.configService.GeminiModel.Returns("gemini-2.0-flash");
        this.configService.OllamaHost.Returns("http://localhost:11434");
        this.configService.OllamaModel.Returns("llama3.2");
        this.configService.OnnxModelPath.Returns("/models/default.onnx");
        this.configService.EnableCopilotButton.Returns(true);
        this.configService.EnableNaturalSearch.Returns(true);
        this.configService.EnableSwarmDiagnostics.Returns(true);

        this.aiManager = Substitute.For<IAiManager>();
        this.aiManager.ActiveProviderId.Returns("RuleHeuristic");

        this.controller = new AiConfigController(this.configService, this.aiManager);
    }

    [Test]
    public async Task SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var result = await this.controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task SaveConfig_WhenSwitchingToHealthyProvider_AwaitsSwitchAndReturnsAccepted()
    {
        this.aiManager.SwitchProviderAsync("OnnxLocal").Returns(x =>
        {
            this.aiManager.ActiveProviderId.Returns("OnnxLocal");
            return Task.FromResult(true);
        });

        var resource = new AiConfigResource
        {
            ActiveAiProvider = "OnnxLocal",
            OnnxModelPath = "/models/custom.onnx",
            EnableCopilotButton = true,
        };

        var result = await this.controller.SaveConfig(resource);

        _ = this.aiManager.Received(1).SwitchProviderAsync("OnnxLocal");
        result.Result.Should().BeOfType<AcceptedResult>();
        var accepted = (AcceptedResult)result.Result!;
        accepted.StatusCode.Should().Be(202);
        var res = accepted.Value as AiConfigResource;
        res.Should().NotBeNull();
        res!.ActiveAiProvider.Should().Be("OnnxLocal");
    }

    [Test]
    public async Task SaveConfig_WhenSwitchingToUnhealthyProvider_ReturnsBadRequestAndDoesNotChangeActiveProvider()
    {
        this.aiManager.SwitchProviderAsync("Gemini").Returns(Task.FromResult(false));
        this.aiManager.ProbeProviderAsync("Gemini").Returns(Task.FromResult(new AiHealthResult
        {
            IsHealthy = false,
            StatusMessage = "Google Gemini API returned HTTP 401: Invalid API Key",
        }));

        var resource = new AiConfigResource
        {
            ActiveAiProvider = "Gemini",
            GeminiApiKey = "AIzaSyInvalidKey",
            GeminiModel = "gemini-2.0-flash",
        };

        var result = await this.controller.SaveConfig(resource);

        _ = this.aiManager.Received(1).SwitchProviderAsync("Gemini");
        _ = this.aiManager.Received(1).ProbeProviderAsync("Gemini");

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Google Gemini API returned HTTP 401: Invalid API Key");

        // The active provider in database was not changed to Gemini
        this.configService.Received().SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveAiProvider"] == "RuleHeuristic"));
        this.configService.DidNotReceive().SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveAiProvider"] == "Gemini"));
    }

    [Test]
    public async Task SaveConfig_WhenTargetProviderNotFound_ReturnsBadRequestWithProbeMessage()
    {
        this.aiManager.SwitchProviderAsync("NonExistentProvider").Returns(Task.FromResult(false));
        this.aiManager.ProbeProviderAsync("NonExistentProvider").Returns(Task.FromResult(new AiHealthResult
        {
            IsHealthy = false,
            StatusMessage = "AI provider 'NonExistentProvider' is not recognized or registered.",
        }));

        var resource = new AiConfigResource
        {
            ActiveAiProvider = "NonExistentProvider",
        };

        var result = await this.controller.SaveConfig(resource);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("AI provider 'NonExistentProvider' is not recognized or registered.");
    }

    [Test]
    public async Task SaveConfig_WhenMaskedSecretProvided_RestoresExistingSecret()
    {
        var resource = new AiConfigResource
        {
            ActiveAiProvider = "RuleHeuristic",
            GeminiApiKey = "*****************y123", // Masked value matching current
        };

        var result = await this.controller.SaveConfig(resource);

        result.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received().SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["GeminiApiKey"] == "AIzaSyOldSecretKey123"));
    }

    [Test]
    public async Task SaveConfig_WhenAsterisksPlaceholderProvided_RestoresExistingSecret()
    {
        var resource = new AiConfigResource
        {
            ActiveAiProvider = "RuleHeuristic",
            GeminiApiKey = "********",
        };

        var result = await this.controller.SaveConfig(resource);

        result.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received().SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["GeminiApiKey"] == "AIzaSyOldSecretKey123"));
    }

    [Test]
    public async Task SaveConfig_WhenNewSecretWithAsteriskProvided_PreservesNewSecret()
    {
        var resource = new AiConfigResource
        {
            ActiveAiProvider = "RuleHeuristic",
            GeminiApiKey = "AIzaSy*NewKeyWithStar*99",
        };

        var result = await this.controller.SaveConfig(resource);

        result.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received().SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["GeminiApiKey"] == "AIzaSy*NewKeyWithStar*99"));
    }

    [Test]
    public async Task SaveConfig_WhenAiManagerIsNull_SavesDirectly()
    {
        var controllerWithoutManager = new AiConfigController(this.configService, null);
        var resource = new AiConfigResource
        {
            ActiveAiProvider = "Ollama",
            OllamaHost = "http://localhost:11434",
        };

        var result = await controllerWithoutManager.SaveConfig(resource);

        result.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received().SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveAiProvider"] == "Ollama"));
    }

    [Test]
    public async Task SaveConfig_WhenActiveProviderUnchanged_DoesNotCallSwitchProvider()
    {
        var resource = new AiConfigResource
        {
            ActiveAiProvider = "RuleHeuristic",
            EnableNaturalSearch = false,
        };

        var result = await this.controller.SaveConfig(resource);

        _ = this.aiManager.DidNotReceive().SwitchProviderAsync(Arg.Any<string>());
        result.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received().SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (bool)d["EnableNaturalSearch"] == false));
    }
}

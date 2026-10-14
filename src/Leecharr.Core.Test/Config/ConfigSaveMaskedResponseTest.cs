// Copyright (c) FeedItOut. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Config;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Config;

[TestFixture]
public class ConfigSaveMaskedResponseTest
{
    [Test]
    public async Task NetworkConfigController_SaveConfig_WhenMaskedProxyPasswordSubmitted_ReturnsMaskedPasswordInResponse()
    {
        const string storedPassword = "proxy-secret-password";
        var configService = Substitute.For<IConfigService>();
        configService.ProxyPassword.Returns(storedPassword);

        var controller = new NetworkConfigController(configService);
        var resource = new NetworkConfigResource
        {
            ListeningPort = 6881,
            ProxyPassword = "********",
        };

        var actionResult = await controller.SaveConfig(resource);

        var accepted = actionResult.Result as AcceptedResult;
        accepted.Should().NotBeNull();
        var response = accepted!.Value as NetworkConfigResource;
        response.Should().NotBeNull();
        response!.ProxyPassword.Should().Be("********");
        response.ProxyPassword.Should().NotBe(storedPassword);
    }

    [Test]
    public async Task AiConfigController_SaveConfig_WhenMaskedGeminiApiKeySubmitted_ReturnsMaskedKeyInResponse()
    {
        const string storedKey = "AIzaSyABCDEFGHIJKLMNOP";
        var maskedKey = new string('*', storedKey.Length - 4) + storedKey[^4..];
        var configService = Substitute.For<IConfigService>();
        configService.GeminiApiKey.Returns(storedKey);

        var controller = new AiConfigController(configService);
        var resource = new AiConfigResource
        {
            GeminiApiKey = maskedKey,
        };

        var actionResult = await controller.SaveConfig(resource);

        var accepted = actionResult.Result as AcceptedResult;
        accepted.Should().NotBeNull();
        var response = accepted!.Value as AiConfigResource;
        response.Should().NotBeNull();
        response!.GeminiApiKey.Should().Be(maskedKey);
        response.GeminiApiKey.Should().NotBe(storedKey);
    }
}

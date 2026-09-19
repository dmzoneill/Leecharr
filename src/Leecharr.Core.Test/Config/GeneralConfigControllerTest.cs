// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Config;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.Config;

[TestFixture]
public class GeneralConfigControllerTest
{
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private ICertificateManager certificateManager = null!;
    private GeneralConfigController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.certificateManager = Substitute.For<ICertificateManager>();
        this.controller = new GeneralConfigController(
            this.configService,
            this.configFileProvider,
            this.certificateManager);
    }

    [Test]
    public void GetApiKey_ReturnsUnmaskedApiKey_FromConfigFileProvider()
    {
        const string expectedKey = "authentic_unmasked_api_key_42";
        this.configFileProvider.ApiKey.Returns(expectedKey);

        var actionResult = this.controller.GetApiKey();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var apiKeyResource = okResult.Value as ApiKeyResource;
        apiKeyResource.Should().NotBeNull();
        apiKeyResource!.ApiKey.Should().Be(expectedKey);
    }

    [Test]
    public void GetApiKey_WhenApiKeyIsNull_ReturnsEmptyString()
    {
        this.configFileProvider.ApiKey.Returns((string)null!);

        var actionResult = this.controller.GetApiKey();

        var okResult = actionResult.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var apiKeyResource = okResult.Value as ApiKeyResource;
        apiKeyResource.Should().NotBeNull();
        apiKeyResource!.ApiKey.Should().Be(string.Empty);
    }

    [Test]
    public void GetConfig_ReturnsMaskedApiKey()
    {
        this.configFileProvider.ApiKey.Returns("1234567890abcdef");

        var resource = this.controller.GetConfig();

        resource.Should().NotBeNull();
        resource.ApiKey.Should().Be("************cdef");
    }

    [Test]
    public void GetConfig_ReturnsAuthenticationRequiredFromConfigFileProvider()
    {
        this.configFileProvider.AuthenticationRequired.Returns(AuthenticationRequiredType.DisabledForLocalAddresses);

        var resource = this.controller.GetConfig();

        resource.Should().NotBeNull();
        resource.AuthenticationRequired.Should().Be(AuthenticationRequiredType.DisabledForLocalAddresses);
    }

    [Test]
    public async Task SaveConfig_SavesAuthenticationRequiredToConfigFileProvider()
    {
        Dictionary<string, object> savedDict = null!;
        this.configFileProvider.When(x => x.SaveConfigDictionary(Arg.Any<Dictionary<string, object>>()))
            .Do(call => savedDict = call.Arg<Dictionary<string, object>>());

        var resource = new GeneralConfigResource
        {
            Port = 7889,
            AuthenticationRequired = AuthenticationRequiredType.DisabledForLocalhost,
        };

        var actionResult = await this.controller.SaveConfig(resource);

        savedDict.Should().NotBeNull();
        savedDict["AuthenticationRequired"].Should().Be(AuthenticationRequiredType.DisabledForLocalhost);
    }

    [Test]
    public void GetApiKey_WhenUserIsNotAdmin_ReturnsForbid()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "testuser"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "User"),
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);

        this.controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = httpContext,
        };

        this.configFileProvider.ApiKey.Returns("secret_key_123");

        var actionResult = this.controller.GetApiKey();
        actionResult.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.ForbidResult>();
    }

    [Test]
    public async Task SaveConfig_WhenApiKeyChanges_InvalidatesRpcSessionsAndDisconnectsSignalR()
    {
        var rpcStore = new RpcSessionStore();
        rpcStore.SetSession("token-session-1", TimeSpan.FromMinutes(10));
        rpcStore.IsValid("token-session-1").Should().BeTrue();

        MessageHub.AddConnectionForTesting("signalr-conn-1");
        MessageHub.IsConnected.Should().BeTrue();

        this.configFileProvider.ApiKey.Returns("old-api-key");
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var resource = new GeneralConfigResource
        {
            ApiKey = "new-api-key",
            AuthenticationEnabled = true,
        };

        await this.controller.SaveConfig(resource);

        rpcStore.IsValid("token-session-1").Should().BeFalse();
        MessageHub.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task SaveConfig_WhenAuthenticationEnabledChanges_InvalidatesRpcSessionsAndDisconnectsSignalR()
    {
        var rpcStore = new RpcSessionStore();
        rpcStore.SetSession("token-session-2", TimeSpan.FromMinutes(10));
        rpcStore.IsValid("token-session-2").Should().BeTrue();

        MessageHub.AddConnectionForTesting("signalr-conn-2");
        MessageHub.IsConnected.Should().BeTrue();

        this.configFileProvider.ApiKey.Returns("unchanged-key");
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var resource = new GeneralConfigResource
        {
            ApiKey = "unchanged-key",
            AuthenticationEnabled = true,
        };

        await this.controller.SaveConfig(resource);

        rpcStore.IsValid("token-session-2").Should().BeFalse();
        MessageHub.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task SaveConfig_WhenNeitherApiKeyNorAuthEnabledChanges_PreservesSessionsAndConnections()
    {
        var rpcStore = new RpcSessionStore();
        rpcStore.SetSession("token-session-3", TimeSpan.FromMinutes(10));
        rpcStore.IsValid("token-session-3").Should().BeTrue();

        MessageHub.AddConnectionForTesting("signalr-conn-3");
        MessageHub.IsConnected.Should().BeTrue();

        this.configFileProvider.ApiKey.Returns("stable-key");
        this.configFileProvider.AuthenticationEnabled.Returns(true);

        var resource = new GeneralConfigResource
        {
            ApiKey = "stable-key",
            AuthenticationEnabled = true,
            Port = 8999,
        };

        await this.controller.SaveConfig(resource);

        rpcStore.IsValid("token-session-3").Should().BeTrue();
        MessageHub.IsConnected.Should().BeTrue();

        MessageHub.ResetForTesting();
        RpcSessionStore.InvalidateAllSessions();
    }
}

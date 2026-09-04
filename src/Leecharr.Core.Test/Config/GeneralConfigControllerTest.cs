// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using FluentAssertions;
using Leecharr.Api.V1.Config;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;

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
}

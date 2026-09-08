// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Config;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;

namespace Leecharr.Core.Test.Config;

[TestFixture]
public class ConfigControllerNullGuardTests
{
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private ICertificateManager certificateManager = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.certificateManager = Substitute.For<ICertificateManager>();
    }

    [Test]
    public async Task GeneralConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new GeneralConfigController(this.configService, this.configFileProvider, this.certificateManager);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task SeedingConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new SeedingConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task NetworkConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new NetworkConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task BitTorrentConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new BitTorrentConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task PeerProtocolConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new PeerProtocolConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task ProtocolsConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new ProtocolsConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task SimulationConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new SimulationConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task TrackerServerConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new TrackerServerConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task SchedulerConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new SchedulerConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task AdvancedConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new AdvancedConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task AiConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new AiConfigController(this.configService);
        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().Be("Request body cannot be empty.");
    }
}

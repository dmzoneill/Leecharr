// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Transmission;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class TransmissionRpcControllerTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;
    private TransmissionRpcController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();

        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret_api_key_123");

        this.controller = new TransmissionRpcController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.configService,
            null,
            this.configFileProvider);
    }

    [Test]
    public void HandleGet_WhenUnauthenticated_Returns401Unauthorized()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = this.controller.HandleGet();

        result.Should().BeOfType<UnauthorizedResult>();
        context.Response.Headers["WWW-Authenticate"].ToString().Should().Contain("Transmission");
    }

    [Test]
    public void HandleGet_WithValidApiKeyHeader_Succeeds()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "secret_api_key_123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = this.controller.HandleGet();

        // Without transmission session header, transmission protocol responds 409 Conflict with session header
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Headers.ContainsKey("X-Transmission-Session-Id").Should().BeTrue();
    }

    [Test]
    public async Task HandleRpc_WhenUnauthenticated_Returns401Unauthorized()
    {
        var context = new DefaultHttpContext();
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest { Method = "session-get" });

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task HandleRpc_WithBasicAuthMatchingApiKey_Succeeds()
    {
        var context = new DefaultHttpContext();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret_api_key_123"));
        context.Request.Headers["Authorization"] = $"Basic {credentials}";
        context.Request.Headers["X-Transmission-Session-Id"] = "active-session-123";
        this.controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await this.controller.HandleRpc(new TransmissionRpcRequest { Method = "session-get" });

        result.Should().BeOfType<OkObjectResult>();
    }
}

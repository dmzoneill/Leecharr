// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Aria2;
using Leecharr.Api.V1.Flood;
using Leecharr.Api.V1.Freebox;
using Leecharr.Api.V1.Hadouken;
using Leecharr.Api.V1.Nzbget;
using Leecharr.Api.V1.NzbVortex;
using Leecharr.Api.V1.RTorrent;
using Leecharr.Api.V1.Sabnzbd;
using Leecharr.Api.V1.Synology;
using Leecharr.Api.V1.UTorrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class CompatibilityRpcAuthTest
{
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IConfigService configService = null!;
    private IConfigFileProvider configFileProvider = null!;

    [SetUp]
    public void SetUp()
    {
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.configService = Substitute.For<IConfigService>();
        this.configFileProvider = Substitute.For<IConfigFileProvider>();

        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("master_api_key_xyz");
    }

    [Test]
    public async Task UTorrentWebUi_WhenUnauthenticated_Returns401AndBadRequest()
    {
        var controller = new UTorrentWebUiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            configFileProvider: this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var tokenResult = controller.GetToken();
        tokenResult.Should().BeOfType<UnauthorizedResult>();

        var webUiResult = await controller.HandleWebUi(null!, "getprops", "hash1", null!, null!, null!, null!, null!, null!);
        webUiResult.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)webUiResult).Value.Should().Be("invalid request");
    }

    [Test]
    public async Task UTorrentWebUi_WhenAuthenticatedViaTokenAndCookie_GrantsAccess()
    {
        var controller = new UTorrentWebUiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            configFileProvider: this.configFileProvider);

        var tokenContext = new DefaultHttpContext();
        tokenContext.Request.Headers["X-Api-Key"] = "master_api_key_xyz";
        controller.ControllerContext = new ControllerContext { HttpContext = tokenContext };

        var tokenResult = controller.GetToken();
        tokenResult.Should().BeOfType<ContentResult>();

        var contentResult = (ContentResult)tokenResult;
        var html = contentResult.Content!;
        var startIdx = html.IndexOf("<div id=\"token\">", StringComparison.Ordinal) + "<div id=\"token\">".Length;
        var endIdx = html.IndexOf("</div>", startIdx, StringComparison.Ordinal);
        var token = html[startIdx..endIdx];
        token.Should().NotBeNullOrWhiteSpace();

        tokenContext.Response.Headers.TryGetValue("Set-Cookie", out var setCookieHeaders).Should().BeTrue();
        var setCookie = setCookieHeaders.ToString();
        var guidCookie = setCookie.Split(';')[0].Replace("GUID=", string.Empty);

        var authedContext = new DefaultHttpContext();
        authedContext.Request.Headers["Cookie"] = $"GUID={guidCookie}";
        controller.ControllerContext = new ControllerContext { HttpContext = authedContext };

        var webUiResult = await controller.HandleWebUi(null!, "getprops", "hash1", null!, null!, null!, null!, null!, token);
        webUiResult.Should().BeOfType<OkObjectResult>();

        var badAuthedContext = new DefaultHttpContext();
        badAuthedContext.Request.Headers["Cookie"] = $"GUID={guidCookie}";
        controller.ControllerContext = new ControllerContext { HttpContext = badAuthedContext };

        var badResult = await controller.HandleWebUi(null!, "getprops", "hash1", null!, null!, null!, null!, null!, "wrong_token");
        badResult.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task RTorrent_WhenUnauthenticated_Returns401()
    {
        var controller = new RTorrentController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.torrentFileService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleXmlRpc();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public async Task Sabnzbd_WhenUnauthenticated_Returns401()
    {
        var controller = new SabnzbdApiController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleApi("queue", null!, null!, null!, null!, null!);
        result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public async Task Nzbget_WhenUnauthenticated_Returns401()
    {
        var controller = new NzbgetRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleRpc(new NzbgetRequest { Method = "listgroups" });
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void Synology_WhenUnauthenticated_Returns401()
    {
        var controller = new SynologyDownloadStationController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var infoResult = controller.Info("getInfo");
        infoResult.Should().BeOfType<UnauthorizedResult>();

        var statResult = controller.Statistic("getStatistic");
        statResult.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void Synology_WhenHardcodedSessionTokenProvided_Returns401()
    {
        var controller = new SynologyDownloadStationController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?_sid=leecharr-synology-session-id");
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var infoResult = controller.Info("getInfo");
        infoResult.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void Synology_WhenAuthenticatedViaAuth_SessionTokenGrantsAccess()
    {
        var controller = new SynologyDownloadStationController(
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var loginContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = loginContext };

        var loginResult = controller.Auth(api: "SYNO.API.Auth", method: "login", passwd: "master_api_key_xyz");
        loginResult.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)loginResult;
        var dataProp = okResult.Value!.GetType().GetProperty("data")!.GetValue(okResult.Value)!;
        var sidProp = dataProp.GetType().GetProperty("sid")!.GetValue(dataProp)!.ToString();
        sidProp.Should().NotBeNullOrWhiteSpace();
        sidProp.Should().NotBe("leecharr-synology-session-id");

        var authedContext = new DefaultHttpContext();
        authedContext.Request.QueryString = new QueryString($"?_sid={sidProp}");
        controller.ControllerContext = new ControllerContext { HttpContext = authedContext };

        var infoResult = controller.Info("getInfo");
        infoResult.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task Hadouken_WhenUnauthenticated_Returns401()
    {
        var controller = new HadoukenRpcController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.torrentFileService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.HandleRpc(new HadoukenRpcRequest { Method = "webui.list" });
        result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public void Freebox_WhenUnauthenticated_Returns401()
    {
        var controller = new FreeboxDownloadController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.GetDownloads();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void Freebox_WhenHardcodedSessionTokenProvided_Returns401()
    {
        var controller = new FreeboxDownloadController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Fbx-App-Auth"] = "freebox-session-token";
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.GetDownloads();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void Freebox_WhenAuthenticatedViaLogin_SessionTokenGrantsAccess()
    {
        var controller = new FreeboxDownloadController(
            this.torrentService,
            this.torrentFileParser,
            this.configService,
            this.configFileProvider);

        var loginContext = new DefaultHttpContext();
        loginContext.Request.Headers["X-Api-Key"] = "master_api_key_xyz";
        controller.ControllerContext = new ControllerContext { HttpContext = loginContext };

        var loginResult = controller.LoginSession();
        loginResult.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)loginResult;
        var resultProp = okResult.Value!.GetType().GetProperty("result")!.GetValue(okResult.Value)!;
        var sessionToken = resultProp.GetType().GetProperty("session_token")!.GetValue(resultProp)!.ToString();
        sessionToken.Should().NotBeNullOrWhiteSpace();
        sessionToken.Should().NotBe("freebox-session-token");

        var authedContext = new DefaultHttpContext();
        authedContext.Request.Headers["X-Fbx-App-Auth"] = sessionToken;
        controller.ControllerContext = new ControllerContext { HttpContext = authedContext };

        var downloadsResult = controller.GetDownloads();
        downloadsResult.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public void NzbVortex_WhenUnauthenticated_Returns401()
    {
        var controller = new NzbVortexApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.GetAppVersion();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void NzbVortex_WhenHardcodedSessionTokenProvided_Returns401()
    {
        var controller = new NzbVortexApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?session=leecharr-session-token");
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = controller.GetAppVersion();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Test]
    public void NzbVortex_WhenAuthenticatedViaLogin_SessionTokenGrantsAccess()
    {
        var controller = new NzbVortexApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var loginContext = new DefaultHttpContext();
        loginContext.Request.Headers["X-Api-Key"] = "master_api_key_xyz";
        controller.ControllerContext = new ControllerContext { HttpContext = loginContext };

        var loginResult = controller.Login();
        loginResult.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)loginResult;
        var sessionProp = okResult.Value!.GetType().GetProperty("session")!.GetValue(okResult.Value)!.ToString();
        sessionProp.Should().NotBeNullOrWhiteSpace();
        sessionProp.Should().NotBe("leecharr-session-token");

        var authedContext = new DefaultHttpContext();
        authedContext.Request.QueryString = new QueryString($"?session={sessionProp}");
        controller.ControllerContext = new ControllerContext { HttpContext = authedContext };

        var appVersionResult = controller.GetAppVersion();
        appVersionResult.Should().BeOfType<OkObjectResult>();
    }

    private static ActionExecutingContext CreateFloodActionExecutingContext(FloodApiController controller, HttpContext httpContext, string actionName)
    {
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(new RouteValueDictionary { { "action", actionName } }),
            new ControllerActionDescriptor { ActionName = actionName, ControllerName = "FloodApi" });

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            controller);
    }

    [Test]
    public void Flood_WhenUnauthenticated_GetTorrents_Returns401ViaActionFilter()
    {
        var controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var actionExecutingContext = CreateFloodActionExecutingContext(controller, context, "GetTorrents");
        controller.OnActionExecuting(actionExecutingContext);

        actionExecutingContext.Result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)actionExecutingContext.Result!;
        objResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public void Flood_WhenUnauthenticated_DeleteTorrents_Returns401ViaActionFilter()
    {
        var controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var actionExecutingContext = CreateFloodActionExecutingContext(controller, context, "DeleteTorrents");
        controller.OnActionExecuting(actionExecutingContext);

        actionExecutingContext.Result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)actionExecutingContext.Result!;
        objResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public void Flood_WhenUnauthenticated_AddUrls_Returns401ViaActionFilter()
    {
        var controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var actionExecutingContext = CreateFloodActionExecutingContext(controller, context, "AddUrls");
        controller.OnActionExecuting(actionExecutingContext);

        actionExecutingContext.Result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)actionExecutingContext.Result!;
        objResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public void Flood_WhenUnauthenticated_AuthenticateAndVerify_ActionFilterAllowsExecution()
    {
        var controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var authContext = CreateFloodActionExecutingContext(controller, context, "Authenticate");
        controller.OnActionExecuting(authContext);
        authContext.Result.Should().BeNull();

        var verifyContext = CreateFloodActionExecutingContext(controller, context, "Verify");
        controller.OnActionExecuting(verifyContext);
        verifyContext.Result.Should().BeNull();

        var verifyResult = controller.Verify();
        verifyResult.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)verifyResult;
        var isAllowedProp = okResult.Value!.GetType().GetProperty("isAllowed")!.GetValue(okResult.Value);
        isAllowedProp.Should().Be(false);
    }

    [Test]
    public void Flood_WhenAuthenticatedViaApiKeyHeader_ActionFilterAllowsExecution()
    {
        var controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "master_api_key_xyz";
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var actionExecutingContext = CreateFloodActionExecutingContext(controller, context, "GetTorrents");
        controller.OnActionExecuting(actionExecutingContext);
        actionExecutingContext.Result.Should().BeNull();

        var verifyResult = controller.Verify();
        verifyResult.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)verifyResult;
        var isAllowedProp = okResult.Value!.GetType().GetProperty("isAllowed")!.GetValue(okResult.Value);
        isAllowedProp.Should().Be(true);
    }

    [Test]
    public void Flood_WhenAuthenticatedViaLogin_SessionCookiesGrantAccess()
    {
        var controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            this.configFileProvider);

        var loginContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = loginContext };

        var loginResult = controller.Authenticate(new FloodAuthRequest { Password = "master_api_key_xyz" });
        loginResult.Should().BeOfType<OkObjectResult>();

        loginContext.Response.Headers.TryGetValue("Set-Cookie", out var setCookieHeaders).Should().BeTrue();
        var setCookies = setCookieHeaders.ToArray();
        string floodToken = null!;
        foreach (var cookie in setCookies)
        {
            if (cookie.StartsWith("flood-auth="))
            {
                floodToken = cookie.Split(';')[0]["flood-auth=".Length..];
                break;
            }
        }

        floodToken.Should().NotBeNullOrWhiteSpace();

        // 1. Verify access with flood-auth cookie
        var floodAuthContext = new DefaultHttpContext();
        floodAuthContext.Request.Headers["Cookie"] = $"flood-auth={floodToken}";
        controller.ControllerContext = new ControllerContext { HttpContext = floodAuthContext };

        var floodExecContext = CreateFloodActionExecutingContext(controller, floodAuthContext, "GetTorrents");
        controller.OnActionExecuting(floodExecContext);
        floodExecContext.Result.Should().BeNull();

        // 2. Verify access with jwt cookie
        var jwtAuthContext = new DefaultHttpContext();
        jwtAuthContext.Request.Headers["Cookie"] = $"jwt={floodToken}";
        controller.ControllerContext = new ControllerContext { HttpContext = jwtAuthContext };

        var jwtExecContext = CreateFloodActionExecutingContext(controller, jwtAuthContext, "DeleteTorrents");
        controller.OnActionExecuting(jwtExecContext);
        jwtExecContext.Result.Should().BeNull();

        // 3. Verify access with token cookie
        var tokenAuthContext = new DefaultHttpContext();
        tokenAuthContext.Request.Headers["Cookie"] = $"token={floodToken}";
        controller.ControllerContext = new ControllerContext { HttpContext = tokenAuthContext };

        var tokenExecContext = CreateFloodActionExecutingContext(controller, tokenAuthContext, "AddUrls");
        controller.OnActionExecuting(tokenExecContext);
        tokenExecContext.Result.Should().BeNull();

        // 4. Verify access with X-Flood-Auth header
        var headerAuthContext = new DefaultHttpContext();
        headerAuthContext.Request.Headers["X-Flood-Auth"] = floodToken;
        controller.ControllerContext = new ControllerContext { HttpContext = headerAuthContext };

        var headerExecContext = CreateFloodActionExecutingContext(controller, headerAuthContext, "StartTorrents");
        controller.OnActionExecuting(headerExecContext);
        headerExecContext.Result.Should().BeNull();

        // 5. Verify invalid token returns 401
        var badContext = new DefaultHttpContext();
        badContext.Request.Headers["Cookie"] = "flood-auth=invalid_session_token";
        controller.ControllerContext = new ControllerContext { HttpContext = badContext };

        var badExecContext = CreateFloodActionExecutingContext(controller, badContext, "GetTorrents");
        controller.OnActionExecuting(badExecContext);
        badExecContext.Result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)badExecContext.Result!).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Test]
    public void Flood_WhenAuthenticationDisabledGlobally_ActionFilterAllowsExecution()
    {
        var disabledConfigProvider = Substitute.For<IConfigFileProvider>();
        disabledConfigProvider.AuthenticationEnabled.Returns(false);

        var controller = new FloodApiController(
            this.torrentService,
            this.torrentFileService,
            this.torrentFileParser,
            this.categoryService,
            this.configService,
            disabledConfigProvider);

        var context = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var actionExecutingContext = CreateFloodActionExecutingContext(controller, context, "DeleteTorrents");
        controller.OnActionExecuting(actionExecutingContext);
        actionExecutingContext.Result.Should().BeNull();

        var verifyResult = controller.Verify();
        verifyResult.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)verifyResult;
        var isAllowedProp = okResult.Value!.GetType().GetProperty("isAllowed")!.GetValue(okResult.Value);
        isAllowedProp.Should().Be(true);
    }
}

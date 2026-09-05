// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.SignalR;

[TestFixture]
public class MessageHubTest
{
    private IConfigFileProvider configFileProvider;
    private HubCallerContext hubCallerContext;
    private IHubCallerClients clients;
    private ISingleClientProxy callerProxy;
    private DefaultHttpContext httpContext;
    private FeatureCollection featureCollection;

    [SetUp]
    public void SetUp()
    {
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.hubCallerContext = Substitute.For<HubCallerContext>();
        this.clients = Substitute.For<IHubCallerClients>();
        this.callerProxy = Substitute.For<ISingleClientProxy>();

        this.clients.Caller.Returns(this.callerProxy);
        this.hubCallerContext.ConnectionId.Returns("conn-12345");

        this.httpContext = new DefaultHttpContext();
        var httpContextFeature = Substitute.For<IHttpContextFeature>();
        httpContextFeature.HttpContext.Returns(this.httpContext);

        this.featureCollection = new FeatureCollection();
        this.featureCollection.Set<IHttpContextFeature>(httpContextFeature);
        this.hubCallerContext.Features.Returns(this.featureCollection);
    }

    [Test]
    public async Task OnConnectedAsync_WhenAuthDisabled_AcceptsConnectionAndSendsVersion()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(false);

        var hub = new MessageHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
        };

        await hub.OnConnectedAsync();

        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "receiveMessage",
            Arg.Is<object[]>(args => args.Length == 1 && ((SignalRMessage)args[0]).Name == "version"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnConnectedAsync_WhenAuthEnabledAndUserAuthenticated_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-key");

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Admin") }, "Cookie");
        var user = new ClaimsPrincipal(identity);
        this.hubCallerContext.User.Returns(user);

        var hub = new MessageHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
        };

        await hub.OnConnectedAsync();

        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "receiveMessage",
            Arg.Is<object[]>(args => args.Length == 1 && ((SignalRMessage)args[0]).Name == "version"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnConnectedAsync_WhenAuthEnabledAndAccessTokenQueryParam_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-key");
        this.httpContext.Request.QueryString = new QueryString("?access_token=secret-key");

        var hub = new MessageHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
        };

        await hub.OnConnectedAsync();

        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "receiveMessage",
            Arg.Is<object[]>(args => args.Length == 1 && ((SignalRMessage)args[0]).Name == "version"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnConnectedAsync_WhenAuthEnabledAndApiKeyQueryParam_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-key");
        this.httpContext.Request.QueryString = new QueryString("?apikey=secret-key");

        var hub = new MessageHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
        };

        await hub.OnConnectedAsync();

        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "receiveMessage",
            Arg.Is<object[]>(args => args.Length == 1 && ((SignalRMessage)args[0]).Name == "version"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnConnectedAsync_WhenAuthEnabledAndHeaderApiKey_AcceptsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-key");
        this.httpContext.Request.Headers["X-Api-Key"] = "secret-key";

        var hub = new MessageHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
        };

        await hub.OnConnectedAsync();

        this.hubCallerContext.DidNotReceive().Abort();
        await this.callerProxy.Received(1).SendCoreAsync(
            "receiveMessage",
            Arg.Is<object[]>(args => args.Length == 1 && ((SignalRMessage)args[0]).Name == "version"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnConnectedAsync_WhenAuthEnabledAndUnauthenticated_AbortsConnection()
    {
        this.configFileProvider.AuthenticationEnabled.Returns(true);
        this.configFileProvider.ApiKey.Returns("secret-key");
        this.httpContext.Request.QueryString = new QueryString("?access_token=wrong-key");

        var hub = new MessageHub(this.configFileProvider)
        {
            Context = this.hubCallerContext,
            Clients = this.clients,
        };

        await hub.OnConnectedAsync();

        this.hubCallerContext.Received(1).Abort();
        await this.callerProxy.DidNotReceive().SendCoreAsync(
            Arg.Any<string>(),
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }
}

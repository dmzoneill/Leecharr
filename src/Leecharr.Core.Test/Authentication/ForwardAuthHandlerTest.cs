// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Authentication;

[TestFixture]
public class ForwardAuthHandlerTest
{
    private ITrustedNetworkService trustedNetworkService;
    private IJitUserProvisioningService jitProvisioningService;
    private IConfigService configService;

    [SetUp]
    public void SetUp()
    {
        this.trustedNetworkService = new TrustedNetworkService();
        this.jitProvisioningService = Substitute.For<IJitUserProvisioningService>();
        this.configService = Substitute.For<IConfigService>();
    }

    private ForwardAuthHandler CreateHandler(HttpContext context)
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<ForwardAuthOptions>>();
        optionsMonitor.Get(ForwardAuthOptions.DefaultScheme).Returns(new ForwardAuthOptions());

        var handler = new ForwardAuthHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            this.trustedNetworkService,
            this.jitProvisioningService,
            this.configService);

        var scheme = new AuthenticationScheme(ForwardAuthOptions.DefaultScheme, null, typeof(ForwardAuthHandler));
        handler.InitializeAsync(scheme, context).GetAwaiter().GetResult();
        return handler;
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenTrustedProxiesEmpty_ReturnsNoResultEvenForLoopback()
    {
        this.configService.GetValue("ForwardAuthTrustedProxies", string.Empty).Returns(string.Empty);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["Remote-User"] = "admin";

        var handler = this.CreateHandler(context);
        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue();
        this.jitProvisioningService.DidNotReceiveWithAnyArgs().ProvisionOrUpdateUser(default!);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpNotTrusted_ReturnsNoResult()
    {
        this.configService.GetValue("ForwardAuthTrustedProxies", string.Empty).Returns("10.0.0.0/8");

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        context.Request.Headers["Remote-User"] = "admin";

        var handler = this.CreateHandler(context);
        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue();
        this.jitProvisioningService.DidNotReceiveWithAnyArgs().ProvisionOrUpdateUser(default!);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpIsExplicitlyTrustedProxy_AuthenticatesUser()
    {
        this.configService.GetValue("ForwardAuthTrustedProxies", string.Empty).Returns("127.0.0.1");

        var user = new User { Id = 1, Username = "admin", Roles = "[\"Admin\"]" };
        this.jitProvisioningService.ProvisionOrUpdateUser(Arg.Any<ExternalUserProfile>()).Returns(user);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["Remote-User"] = "admin";

        var handler = this.CreateHandler(context);
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal?.Identity?.Name.Should().Be("admin");
        this.jitProvisioningService.Received(1).ProvisionOrUpdateUser(Arg.Is<ExternalUserProfile>(p => p.Username == "admin"));
    }
}

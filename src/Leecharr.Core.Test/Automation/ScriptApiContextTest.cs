// Copyright (c) FeedItOut. All rights reserved.

#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Automation;

[TestFixture]
public class ScriptApiContextTest
{
    [Test]
    public void Get_UsesConfiguredBindAddressInsteadOfLoopback()
    {
        var config = CreateConfig(bindAddress: "192.168.50.10", port: 7889, enableSsl: false);

        Uri? requestUri = null;
        using var http = new ScriptHttpContext(new CapturingHandler(uri => requestUri = uri));
        using var api = new ScriptApiContext(config, http);

        api.get("system/status");

        requestUri.Should().NotBeNull();
        requestUri!.ToString().Should().Be("http://192.168.50.10:7889/api/v1/system/status");
    }

    [Test]
    public void Get_WhenSslEnabled_UsesHttpsOnSslPortWithoutHttpRedirect()
    {
        var config = CreateConfig(bindAddress: "*", port: 7889, enableSsl: true, sslPort: 7890, urlBase: "/leecharr");

        Uri? requestUri = null;
        using var http = new ScriptHttpContext(new CapturingHandler(uri => requestUri = uri));
        using var api = new ScriptApiContext(config, http);

        api.get("health");

        requestUri.Should().NotBeNull();
        requestUri!.ToString().Should().Be("https://127.0.0.1:7890/leecharr/api/v1/health");
    }

    [Test]
    public void AttachAuthHeaders_WhenSslEnabled_AllowsInsecureTlsForLocalApiCalls()
    {
        var config = CreateConfig(bindAddress: "*", port: 7889, enableSsl: true, sslPort: 7890);
        using var api = new ScriptApiContext(config);

        var method = typeof(ScriptApiContext).GetMethod("AttachAuthHeaders", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var opts = (IDictionary<string, object>)method!.Invoke(api, new object?[] { null })!;
        opts.TryGetValue("allowInsecureTls", out var allowInsecure).Should().BeTrue();
        allowInsecure.Should().Be(true);
    }

    private static IConfigFileProvider CreateConfig(
        string bindAddress,
        int port,
        bool enableSsl,
        int sslPort = 7890,
        string urlBase = "")
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.BindAddress.Returns(bindAddress);
        config.Port.Returns(port);
        config.EnableSsl.Returns(enableSsl);
        config.SslPort.Returns(sslPort);
        config.UrlBase.Returns(urlBase);
        return config;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Action<Uri?> capture;

        public CapturingHandler(Action<Uri?> capture)
        {
            this.capture = capture;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.capture(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}

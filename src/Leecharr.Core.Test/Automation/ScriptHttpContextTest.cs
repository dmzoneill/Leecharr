// Copyright (c) PlaceholderCompany. All rights reserved.

#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Automation;

namespace Leecharr.Core.Test.Automation;

[TestFixture]
public class ScriptHttpContextTest
{
    [Test]
    public void ScriptHttpContext_UsesIsolatedCookieContainersAcrossInstances()
    {
        using var context1 = new ScriptHttpContext();
        using var context2 = new ScriptHttpContext();

        context1.CookieContainer.Should().NotBeSameAs(context2.CookieContainer);

        var cookie = new Cookie("sessionId", "test-12345", "/", "example.com");
        context1.CookieContainer.Add(cookie);

        var context1Cookies = context1.CookieContainer.GetCookies(new Uri("https://example.com"));
        context1Cookies["sessionId"]?.Value.Should().Be("test-12345");

        var context2Cookies = context2.CookieContainer.GetCookies(new Uri("https://example.com"));
        context2Cookies["sessionId"].Should().BeNull();
    }

    [Test]
    public void ScriptHttpContext_EnforcesTlsValidationByDefault()
    {
        using var context = new ScriptHttpContext();
        context.AllowInsecureTls.Should().BeFalse();

        var handler = ScriptHttpContext.CreateHandler(context.CookieContainer, allowInsecureTls: false);
        handler.ServerCertificateCustomValidationCallback.Should().BeNull();
    }

    [Test]
    public void ScriptHttpContext_PermitsInsecureTls_WhenExplicitlyConfigured()
    {
        using var context = new ScriptHttpContext(allowInsecureTls: true);
        context.AllowInsecureTls.Should().BeTrue();

        var handler = ScriptHttpContext.CreateHandler(context.CookieContainer, allowInsecureTls: true);
        handler.ServerCertificateCustomValidationCallback.Should().NotBeNull();

        var callbackResult = handler.ServerCertificateCustomValidationCallback!(
            new HttpRequestMessage(),
            null,
            null,
            SslPolicyErrors.RemoteCertificateChainErrors);

        callbackResult.Should().BeTrue();
    }

    [Test]
    public async Task ScriptHttpContext_GetAsync_SendsRequestAndParsesJsonResponse()
    {
        var testHandler = new TestHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Get);
            req.RequestUri.Should().Be(new Uri("https://api.example.com/status"));

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"running\": true, \"version\": \"1.0\"}", System.Text.Encoding.UTF8, "application/json"),
            };
            resp.Headers.Add("X-Test-Header", "Leecharr");
            return resp;
        });

        using var context = new ScriptHttpContext(testHandler);
        var result = await context.GetAsync("https://api.example.com/status");

        result["status"].Should().Be(200);
        result["ok"].Should().Be(true);
        result["body"].Should().Be("{\"running\": true, \"version\": \"1.0\"}");
        result["json"].Should().NotBeNull();

        var json = result["json"] as Dictionary<string, object>;
        json.Should().NotBeNull();

        var headers = result["headers"] as Dictionary<string, string>;
        headers.Should().NotBeNull();
        headers!["X-Test-Header"].Should().Be("Leecharr");
    }

    [Test]
    public async Task ScriptHttpContext_PostAsync_SendsJsonBodyAndCustomHeaders()
    {
        string? receivedContent = null;
        string? receivedContentType = null;
        string? receivedAuth = null;
        string? receivedCookie = null;

        var testHandler = new TestHttpMessageHandler(req =>
        {
            req.Method.Should().Be(HttpMethod.Post);
            receivedContent = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            receivedContentType = req.Content?.Headers.ContentType?.MediaType;

            if (req.Headers.TryGetValues("Authorization", out var authVals))
            {
                receivedAuth = string.Join(", ", authVals);
            }

            if (req.Headers.TryGetValues("Cookie", out var cookieVals))
            {
                receivedCookie = string.Join(", ", cookieVals);
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\": 42}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        using var context = new ScriptHttpContext(testHandler);
        var options = new Dictionary<string, object>
        {
            ["headers"] = new Dictionary<string, object> { ["Authorization"] = "Bearer token-abc" },
            ["cookies"] = new Dictionary<string, object> { ["user"] = "admin" },
        };

        var result = await context.PostAsync("https://api.example.com/items", "{\"name\":\"leech\"}", options);

        result["status"].Should().Be(201);
        result["ok"].Should().Be(true);
        receivedContent.Should().Be("{\"name\":\"leech\"}");
        receivedContentType.Should().Be("application/json");
        receivedAuth.Should().Be("Bearer token-abc");
        receivedCookie.Should().Be("user=admin");
    }

    [Test]
    public void ScriptHttpContext_SynchronousGetAndPost_ExecuteSuccessfullyWithoutDeadlock()
    {
        var testHandler = new TestHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\": true}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        using var context = new ScriptHttpContext(testHandler);

        var getResult = context.get("https://api.example.com/ping");
        getResult.Should().NotBeNull();
        var getDict = getResult as Dictionary<string, object?>;
        getDict!["status"].Should().Be(200);

        var postResult = context.post("https://api.example.com/ping", "body");
        postResult.Should().NotBeNull();
        var postDict = postResult as Dictionary<string, object?>;
        postDict!["status"].Should().Be(200);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(this.handler(request));
        }
    }
}

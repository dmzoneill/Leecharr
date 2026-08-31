// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Network;

namespace Leecharr.Core.Test.Network;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> responseFunc;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFunc)
    {
        this.responseFunc = responseFunc;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(this.responseFunc(request));
    }
}

[TestFixture]
public class ExternalIpServiceTest
{
    private static void SetCache(ExternalIpService subject, string ip, DateTime lastFetch)
    {
        var fields = typeof(ExternalIpService).GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var ipField = fields.FirstOrDefault(f => f.Name.Contains("cachedIp", StringComparison.OrdinalIgnoreCase));
        var fetchField = fields.FirstOrDefault(f => f.Name.Contains("lastFetch", StringComparison.OrdinalIgnoreCase));
        ipField?.SetValue(subject, ip);
        fetchField?.SetValue(subject, lastFetch);
    }

    [Test]
    public void CachedIp_should_be_empty_by_default()
    {
        var subject = new ExternalIpService();
        Assert.That(subject.CachedIp, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task GetExternalIpAsync_should_return_cached_ip_when_cache_is_valid()
    {
        var subject = new ExternalIpService();
        SetCache(subject, "203.0.113.5", DateTime.UtcNow.AddMinutes(-5));

        var result = await subject.GetExternalIpAsync();
        Assert.That(result, Is.EqualTo("203.0.113.5"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_fetch_from_primary_endpoint_when_cache_empty()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri.Host.Contains("leecharr.net"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ip\": \"198.51.100.42\"}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var subject = new ExternalIpService(new HttpClient(handler));
        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("198.51.100.42"));
        Assert.That(subject.CachedIp, Is.EqualTo("198.51.100.42"));
    }

    [TestCase("{\"status\":\"success\",\"action\":\"inserted\",\"message\":\"Client entry inserted successfully.\",\"data\":{\"uuid\":\"f4acf080-fc83-4fc0-916c-603638964ed9\",\"ip\":\"161.230.102.224\",\"last_used\":1788187356}}", "161.230.102.224")]
    [TestCase("{\"ip\": \"146.70.231.15\"}", "146.70.231.15")]
    [TestCase("{\"ip_address\": \"146.70.231.15\"}", "146.70.231.15")]
    [TestCase("146.70.231.15", "146.70.231.15")]
    [TestCase("  146.70.231.15  \n", "146.70.231.15")]
    public void TryExtractIpFromResponse_should_parse_valid_formats(string input, string expected)
    {
        var success = ExternalIpService.TryExtractIpFromResponse(input, out var ip);

        Assert.That(success, Is.True);
        Assert.That(ip, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("invalid-ip")]
    [TestCase("{\"error\": \"not_found\"}")]
    public void TryExtractIpFromResponse_should_fail_for_invalid_formats(string input)
    {
        var success = ExternalIpService.TryExtractIpFromResponse(input, out var ip);

        Assert.That(success, Is.False);
        Assert.That(ip, Is.Empty);
    }
}

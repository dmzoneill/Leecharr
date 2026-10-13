// Copyright (c) FeedItOut. All rights reserved.

using System.Linq;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Developer;

namespace Leecharr.Core.Test.Developer;

[TestFixture]
public class DeveloperHttpTrafficStoreTest
{
    [Test]
    public void Record_RedactsSensitiveHeadersAndUrlSecrets()
    {
        var store = new DeveloperHttpTrafficStore();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://user:secret@indexer.example/api?apikey=abc&foo=bar");
        request.Headers.Add("Authorization", "Bearer token123");
        request.Headers.Add("X-Api-Key", "key-a");
        request.Headers.Add("X-Prowlarr-Api-Key", "key-b");
        request.Headers.Add("x-goog-api-key", "key-c");
        request.Headers.Add("Cookie", "session=abc123");
        request.Headers.Add("Proxy-Authorization", "Basic abc");
        request.Headers.Add("User-Agent", "Leecharr");

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("Set-Cookie", "sid=secret; HttpOnly");

        store.Record(request, response, 12.5, "Default");

        var entry = store.GetRecent(1).Single();
        entry.Url.Should().NotContain("apikey=abc");
        entry.Url.Should().Contain("foo=bar");
        entry.Url.Should().NotContain("secret@");
        entry.RequestHeaders["Authorization"].Should().Be("***REDACTED***");
        entry.RequestHeaders["X-Api-Key"].Should().Be("***REDACTED***");
        entry.RequestHeaders["X-Prowlarr-Api-Key"].Should().Be("***REDACTED***");
        entry.RequestHeaders["x-goog-api-key"].Should().Be("***REDACTED***");
        entry.RequestHeaders["Cookie"].Should().Be("***REDACTED***");
        entry.RequestHeaders["Proxy-Authorization"].Should().Be("***REDACTED***");
        entry.RequestHeaders["User-Agent"].Should().Be("Leecharr");
        entry.ResponseHeaders["Set-Cookie"].Should().Be("***REDACTED***");
    }
}

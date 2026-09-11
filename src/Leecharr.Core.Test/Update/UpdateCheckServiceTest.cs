// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Update;

namespace Leecharr.Core.Test.Update;

[TestFixture]
public class UpdateCheckServiceTest
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public Func<HttpRequestMessage, HttpResponseMessage> ResponseHandler { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        public Func<HttpRequestMessage, Exception> ExceptionHandler { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.CallCount++;

            if (this.ExceptionHandler != null)
            {
                var ex = this.ExceptionHandler(request);
                if (ex != null)
                {
                    throw ex;
                }
            }

            return Task.FromResult(this.ResponseHandler(request));
        }
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenGitHubReturnsValidReleases_ParsesPackagesCorrectly()
    {
        var jsonResponse = @"[
  {
    ""tag_name"": ""v1.4.3"",
    ""published_at"": ""2026-09-10T22:35:18Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v1.4.3"",
    ""body"": ""## New Features\n* Added advanced network proxy\n## Bug Fixes\n* Fixed crash in download engine"",
    ""assets"": [
      {
        ""name"": ""Leecharr.1.4.3.linux-x64.tar.gz""
      }
    ]
  },
  {
    ""tag_name"": ""v1.4.2"",
    ""published_at"": ""2026-09-08T10:00:00Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v1.4.2"",
    ""body"": ""* Minor improvements"",
    ""assets"": []
  }
]";

        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json"),
            },
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().NotBeNull();
        updates.Should().HaveCount(2);

        var latest = updates[0];
        latest.Version.Should().Be("1.4.3");
        latest.Latest.Should().BeTrue();
        latest.FileName.Should().Be("Leecharr.1.4.3.linux-x64.tar.gz");
        latest.Changes.New.Should().Contain("Added advanced network proxy");
        latest.Changes.Fixed.Should().Contain("Fixed crash in download engine");

        var older = updates[1];
        older.Version.Should().Be("1.4.2");
        older.Latest.Should().BeFalse();
        older.Changes.New.Should().Contain("Minor improvements");
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenGitHubFailsOrReturnsError_FallsBackToDefaultList()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().NotBeNull();
        updates.Should().NotBeEmpty();
        updates[0].Installed.Should().BeTrue();
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenHttpThrowsException_GracefullyFallsBackToDefault()
    {
        var handler = new MockHttpMessageHandler
        {
            ExceptionHandler = _ => new HttpRequestException("Network failure"),
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var updates = await service.GetAvailableUpdatesAsync();

        updates.Should().NotBeNull();
        updates.Should().NotBeEmpty();
        updates[0].Installed.Should().BeTrue();
    }

    [Test]
    public async Task GetAvailableUpdatesAsync_WhenCalledMultipleTimes_UsesCache()
    {
        var jsonResponse = @"[
  {
    ""tag_name"": ""v1.4.3"",
    ""published_at"": ""2026-09-10T22:35:18Z"",
    ""html_url"": ""https://github.com/dmzoneill/Leecharr/releases/tag/v1.4.3"",
    ""body"": ""* Initial release"",
    ""assets"": []
  }
]";

        var handler = new MockHttpMessageHandler
        {
            ResponseHandler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json"),
            },
        };
        var httpClient = new HttpClient(handler);
        var service = new UpdateCheckService(httpClient);

        var firstCall = await service.GetAvailableUpdatesAsync();
        var secondCall = await service.GetAvailableUpdatesAsync();

        firstCall.Should().BeEquivalentTo(secondCall);
        handler.CallCount.Should().Be(1);
    }
}

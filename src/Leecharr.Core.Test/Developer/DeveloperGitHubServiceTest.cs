// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Developer.GitHub;

namespace Leecharr.Core.Test.Developer;

[TestFixture]
public class DeveloperGitHubServiceTest
{
    [SetUp]
    public void SetUp()
    {
        DeveloperGitHubService.ClearCache();
    }

    [Test]
    public async Task GetPullRequestsAsync_when_api_succeeds_parses_prs_correctly()
    {
        var json = @"[
            {
                ""id"": 1001,
                ""number"": 42,
                ""title"": ""feat: add new feature"",
                ""state"": ""open"",
                ""html_url"": ""https://github.com/dmzoneill/Leecharr/pull/42"",
                ""created_at"": ""2026-10-01T12:00:00Z"",
                ""draft"": false,
                ""user"": { ""login"": ""alice"", ""avatar_url"": ""https://avatar.test/alice"" },
                ""labels"": [{ ""name"": ""enhancement"" }]
            }
        ]";

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var client = new HttpClient(handler);
        var service = new DeveloperGitHubService(client);

        var result = await service.GetPullRequestsAsync("open", CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalCount, Is.EqualTo(1));
        var item = result.Items[0];
        Assert.That(item.Number, Is.EqualTo(42));
        Assert.That(item.Title, Is.EqualTo("feat: add new feature"));
        Assert.That(item.Author, Is.EqualTo("alice"));
        Assert.That(item.Labels, Contains.Item("enhancement"));
    }

    [Test]
    public async Task GetPullRequestsAsync_when_api_fails_returns_fallback_snapshot()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "Error");
        var client = new HttpClient(handler);
        var service = new DeveloperGitHubService(client);

        var result = await service.GetPullRequestsAsync("all", CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Items, Is.Not.Empty);
        Assert.That(result.Message, Does.Contain("fallback").Or.Contain("500"));
    }

    [Test]
    public async Task GetIssuesAsync_when_api_succeeds_parses_issues_and_filters_prs()
    {
        var json = @"[
            {
                ""id"": 2001,
                ""number"": 99,
                ""title"": ""bug: resolve memory leak"",
                ""state"": ""open"",
                ""html_url"": ""https://github.com/dmzoneill/Leecharr/issues/99"",
                ""created_at"": ""2026-10-02T14:00:00Z"",
                ""comments"": 2,
                ""user"": { ""login"": ""bob"" },
                ""labels"": [{ ""name"": ""bug"" }]
            },
            {
                ""id"": 2002,
                ""number"": 100,
                ""title"": ""PR masquerading as issue"",
                ""state"": ""open"",
                ""pull_request"": {}
            }
        ]";

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, json);
        var client = new HttpClient(handler);
        var service = new DeveloperGitHubService(client);

        var result = await service.GetIssuesAsync("open", CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalCount, Is.EqualTo(1));
        var item = result.Items[0];
        Assert.That(item.Number, Is.EqualTo(99));
        Assert.That(item.Title, Is.EqualTo("bug: resolve memory leak"));
        Assert.That(item.CommentsCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetIssuesAsync_when_rate_limited_sets_flag()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, "Rate limited");
        var client = new HttpClient(handler);
        var service = new DeveloperGitHubService(client);

        var result = await service.GetIssuesAsync("all", CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsRateLimited, Is.True);
        Assert.That(result.Items, Is.Not.Empty);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new();

        public void Enqueue(HttpStatusCode statusCode, string content)
        {
            this.responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (this.responses.Count > 0)
            {
                return Task.FromResult(this.responses.Dequeue());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}

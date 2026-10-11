// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Developer.Quality;

namespace Leecharr.Core.Test.Developer;

[TestFixture]
public class DeveloperQualityServiceTest
{
    [SetUp]
    public void SetUp()
    {
        DeveloperQualityService.ResetCache();
    }

    [Test]
    public async Task GetQualityReportAsync_when_apis_respond_parses_quality_gate_and_metrics()
    {
        var qualityGateJson = @"{
            ""projectStatus"": {
                ""status"": ""OK"",
                ""conditions"": [
                    { ""metricKey"": ""new_coverage"", ""status"": ""OK"", ""actualValue"": ""83.2"", ""errorThreshold"": ""80"" },
                    { ""metricKey"": ""new_duplicated_lines_density"", ""status"": ""OK"", ""actualValue"": ""2.0"", ""errorThreshold"": ""3"" }
                ]
            }
        }";

        var measuresJson = @"{
            ""component"": {
                ""measures"": [
                    { ""metric"": ""coverage"", ""value"": ""75.5"" },
                    { ""metric"": ""bugs"", ""value"": ""0"" },
                    { ""metric"": ""vulnerabilities"", ""value"": ""0"" },
                    { ""metric"": ""code_smells"", ""value"": ""0"" },
                    { ""metric"": ""security_hotspots_reviewed"", ""value"": ""100.0"" },
                    { ""metric"": ""reliability_rating"", ""value"": ""1.0"" },
                    { ""metric"": ""security_rating"", ""value"": ""1.0"" },
                    { ""metric"": ""sqale_rating"", ""value"": ""1.0"" }
                ]
            }
        }";

        var workflowsListJson = @"{
            ""workflows"": [
                { ""id"": 101, ""name"": ""SonarCloud Analysis"" },
                { ""id"": 999, ""name"": ""AI Issue and PR Responder"" }
            ]
        }";

        var sonarRunJson = @"{
            ""workflow_runs"": [
                {
                    ""name"": ""SonarCloud Analysis"",
                    ""status"": ""completed"",
                    ""conclusion"": ""success"",
                    ""run_number"": 105,
                    ""html_url"": ""https://github.com/dmzoneill/Leecharr/actions/runs/123"",
                    ""created_at"": ""2026-10-04T08:00:00Z""
                }
            ]
        }";

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, qualityGateJson);
        handler.Enqueue(HttpStatusCode.OK, measuresJson);
        handler.Enqueue(HttpStatusCode.OK, workflowsListJson);
        handler.Enqueue(HttpStatusCode.OK, sonarRunJson);

        var client = new HttpClient(handler);
        var service = new DeveloperQualityService(client);

        var report = await service.GetQualityReportAsync(CancellationToken.None);

        Assert.That(report, Is.Not.Null);
        Assert.That(report.QualityGate.Status, Is.EqualTo("OK"));
        Assert.That(report.Metrics.NewCodeCoverage, Is.EqualTo(83.2));
        Assert.That(report.Metrics.DuplicationDensity, Is.EqualTo(2.0));
        Assert.That(report.Metrics.Coverage, Is.EqualTo(75.5));
        Assert.That(report.Metrics.Bugs, Is.EqualTo(0));
        Assert.That(report.Metrics.ReliabilityRating, Is.EqualTo("A"));
        Assert.That(report.RecentPipelines, Has.Count.EqualTo(1));
        Assert.That(report.RecentPipelines[0].Name, Is.EqualTo("SonarCloud Analysis"));
    }

    [Test]
    public async Task GetQualityReportAsync_uses_per_workflow_latest_runs_not_global_feed()
    {
        var qualityGateJson = @"{ ""projectStatus"": { ""status"": ""OK"", ""conditions"": [] } }";
        var measuresJson = @"{ ""component"": { ""measures"": [] } }";
        var workflowsListJson = @"{
            ""workflows"": [
                { ""id"": 101, ""name"": ""SonarCloud Analysis"" },
                { ""id"": 202, ""name"": ""CICD"" },
                { ""id"": 999, ""name"": ""AI Issue and PR Responder"" }
            ]
        }";
        var sonarRunJson = @"{
            ""workflow_runs"": [
                {
                    ""name"": ""SonarCloud Analysis"",
                    ""status"": ""completed"",
                    ""conclusion"": ""success"",
                    ""run_number"": 10,
                    ""html_url"": ""https://github.com/dmzoneill/Leecharr/actions/runs/1"",
                    ""created_at"": ""2026-10-04T08:00:00Z""
                }
            ]
        }";
        var cicdRunJson = @"{
            ""workflow_runs"": [
                {
                    ""name"": ""CICD"",
                    ""status"": ""completed"",
                    ""conclusion"": ""failure"",
                    ""run_number"": 1100,
                    ""html_url"": ""https://github.com/dmzoneill/Leecharr/actions/runs/2"",
                    ""created_at"": ""2026-10-04T09:00:00Z""
                }
            ]
        }";

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, qualityGateJson);
        handler.Enqueue(HttpStatusCode.OK, measuresJson);
        handler.Enqueue(HttpStatusCode.OK, workflowsListJson);
        handler.Enqueue(HttpStatusCode.OK, sonarRunJson);
        handler.Enqueue(HttpStatusCode.OK, cicdRunJson);

        var client = new HttpClient(handler);
        var service = new DeveloperQualityService(client);

        var report = await service.GetQualityReportAsync(CancellationToken.None);

        Assert.That(report.RecentPipelines, Has.Count.EqualTo(2));
        Assert.That(report.RecentPipelines[0].Name, Is.EqualTo("SonarCloud Analysis"));
        Assert.That(report.RecentPipelines[1].Name, Is.EqualTo("CICD"));
        Assert.That(report.RecentPipelines[1].Conclusion, Is.EqualTo("failure"));
    }

    [Test]
    public async Task GetQualityReportAsync_slower_fallback_does_not_overwrite_live_cached_report()
    {
        var liveGateJson = @"{ ""projectStatus"": { ""status"": ""ERROR"", ""conditions"": [ { ""metricKey"": ""new_coverage"", ""status"": ""ERROR"", ""actualValue"": ""50.0"", ""errorThreshold"": ""80"" } ] } }";
        var measuresJson = @"{ ""component"": { ""measures"": [ { ""metric"": ""coverage"", ""value"": ""75.5"" } ] } }";
        var workflowsListJson = @"{ ""workflows"": [ { ""id"": 101, ""name"": ""SonarCloud Analysis"" } ] }";
        var sonarRunJson = @"{ ""workflow_runs"": [ { ""name"": ""SonarCloud Analysis"", ""status"": ""completed"", ""conclusion"": ""failure"", ""run_number"": 105, ""html_url"": ""https://github.com/dmzoneill/Leecharr/actions/runs/123"", ""created_at"": ""2026-10-04T08:00:00Z"" } ] }";

        var fastHandler = new MockHttpMessageHandler();
        fastHandler.Enqueue(HttpStatusCode.OK, liveGateJson);
        fastHandler.Enqueue(HttpStatusCode.OK, measuresJson);
        fastHandler.Enqueue(HttpStatusCode.OK, workflowsListJson);
        fastHandler.Enqueue(HttpStatusCode.OK, sonarRunJson);

        var slowHandler = new DelayedHttpMessageHandler(TimeSpan.FromMilliseconds(200));
        slowHandler.Enqueue(HttpStatusCode.InternalServerError, "Error");
        slowHandler.Enqueue(HttpStatusCode.InternalServerError, "Error");
        slowHandler.Enqueue(HttpStatusCode.InternalServerError, "Error");

        var fastService = new DeveloperQualityService(new HttpClient(fastHandler));
        var slowService = new DeveloperQualityService(new HttpClient(slowHandler));

        var slowTask = slowService.GetQualityReportAsync(CancellationToken.None);
        var liveReport = await fastService.GetQualityReportAsync(CancellationToken.None);
        var slowReport = await slowTask;

        Assert.That(liveReport.QualityGate.Status, Is.EqualTo("ERROR"));
        Assert.That(liveReport.UsedFallbackQualityGate, Is.False);
        Assert.That(liveReport.UsedFallbackPipelines, Is.False);
        Assert.That(slowReport.QualityGate.Status, Is.EqualTo("ERROR"));
        Assert.That(slowReport.UsedFallbackQualityGate, Is.False);
    }

    [Test]
    public async Task GetQualityReportAsync_when_apis_fail_returns_fallback_report()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "Error");
        handler.Enqueue(HttpStatusCode.InternalServerError, "Error");
        handler.Enqueue(HttpStatusCode.InternalServerError, "Error");

        var client = new HttpClient(handler);
        var service = new DeveloperQualityService(client);

        var report = await service.GetQualityReportAsync(CancellationToken.None);

        Assert.That(report, Is.Not.Null);
        Assert.That(report.QualityGate.Status, Is.EqualTo("OK"));
        Assert.That(report.Metrics.ReliabilityRating, Is.EqualTo("A"));
        Assert.That(report.RecentPipelines, Is.Not.Empty);
    }

    private class DelayedHttpMessageHandler : MockHttpMessageHandler
    {
        private readonly TimeSpan delay;

        public DelayedHttpMessageHandler(TimeSpan delay)
        {
            this.delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(this.delay, cancellationToken);
            return await base.SendAsync(request, cancellationToken);
        }
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

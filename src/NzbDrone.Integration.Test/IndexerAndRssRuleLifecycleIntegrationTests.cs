// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class IndexerAndRssRuleLifecycleIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Indexer_CrudAndTest_LifecycleSucceeds()
    {
        // 1. GET /api/v1/indexer
        var listResp = await this.Client.GetAsync("/api/v1/indexer");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. POST /api/v1/indexer (Create new indexer)
        var createResp = await this.PostJsonAsync("/api/v1/indexer", new
        {
            name = "Test Torznab Indexer",
            url = "http://torznab.internal.local:5075/api",
            apiKey = "torznab_api_key_123",
            enableRss = true,
            enableSearch = true,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var indexerId = createdDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 3. GET /api/v1/indexer/{id}
            var getResp = await this.Client.GetAsync($"/api/v1/indexer/{indexerId}");
            getResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. PUT /api/v1/indexer/{id}
            var updateResp = await this.PutJsonAsync($"/api/v1/indexer/{indexerId}", new
            {
                id = indexerId,
                name = "Updated Torznab Indexer",
                url = "http://torznab.internal.local:5075/api",
                apiKey = "updated_key_456",
                enableRss = false,
                enableSearch = true,
            });
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. Test endpoint (exercises test connection logic; may fail connection to dummy URL but returns handled response)
            var testResp = await this.PostJsonAsync($"/api/v1/indexer/{indexerId}/test", new { });
            testResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);

            // 6. Test all indexers
            var testAllResp = await this.PostJsonAsync("/api/v1/indexer/testall", new { });
            testAllResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);

            // 7. Prowlarr sync endpoint
            var syncResp = await this.PostJsonAsync("/api/v1/indexer/sync-prowlarr", new { });
            syncResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
        }
        finally
        {
            // 8. DELETE /api/v1/indexer/{id}
            var delResp = await this.Client.DeleteAsync($"/api/v1/indexer/{indexerId}");
            delResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task RssRule_CrudAndRegexValidation_Succeeds()
    {
        // 1. GET /api/v1/rssrule
        var listResp = await this.Client.GetAsync("/api/v1/rssrule");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Invalid regex pattern returns BadRequest
        var invalidReq = new
        {
            name = "Invalid Regex Rule",
            mustContain = "[unclosed_regex",
        };
        var invalidResp = await this.PostJsonAsync("/api/v1/rssrule", invalidReq);
        invalidResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. POST /api/v1/rssrule (Create valid rule)
        var createReq = new
        {
            name = "Anime 1080p Rule",
            mustContain = @"(?i)\b(1080p|FHD)\b",
            mustNotContain = @"(?i)\b(CAM|TS)\b",
            minSizeBytes = 500_000_000,
            maxSizeBytes = 5_000_000_000,
            minSeeders = 5,
            maxAgeDays = 30,
            enabled = true,
        };
        var createResp = await this.PostJsonAsync("/api/v1/rssrule", createReq);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var ruleId = createdDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 4. GET /api/v1/rssrule/{id}
            var getResp = await this.Client.GetAsync($"/api/v1/rssrule/{ruleId}");
            getResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. PUT /api/v1/rssrule/{id} (Update rule)
            var updateReq = new
            {
                id = ruleId,
                name = "Updated Anime 1080p Rule",
                mustContain = @"(?i)\b(1080p|FHD)\b",
                mustNotContain = @"(?i)\b(CAM|TS)\b",
                minSizeBytes = 600_000_000,
                maxSizeBytes = 6_000_000_000,
                minSeeders = 10,
                maxAgeDays = 60,
                enabled = true,
            };
            var updateResp = await this.PutJsonAsync($"/api/v1/rssrule/{ruleId}", updateReq);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // 6. DELETE /api/v1/rssrule/{id}
            var delResp = await this.Client.DeleteAsync($"/api/v1/rssrule/{ruleId}");
            delResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}

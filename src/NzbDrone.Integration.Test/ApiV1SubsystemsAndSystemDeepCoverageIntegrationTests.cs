// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ApiV1SubsystemsAndSystemDeepCoverageIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task SystemResourcesAndLogs_GetEndpoints_ReturnsSuccessResponses()
    {
        // 1. System resources full snapshot
        var resResp = await this.Client.GetAsync("/api/v1/system/resources");
        resResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var resJson = await resResp.Content.ReadAsStringAsync();
        using var resDoc = JsonDocument.Parse(resJson);
        resDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);

        // 2. System resources host metrics
        var hostMetricsResp = await this.Client.GetAsync("/api/v1/system/resources/host");
        hostMetricsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. System resources engine metrics
        var engineMetricsResp = await this.Client.GetAsync("/api/v1/system/resources/engine");
        engineMetricsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Scheduled tasks list
        var taskResp = await this.Client.GetAsync("/api/v1/system/task");
        taskResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. System log entries
        var logResp = await this.Client.GetAsync("/api/v1/log?level=Info&count=50");
        logResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. System log files
        var logFileResp = await this.Client.GetAsync("/api/v1/logfile");
        logFileResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ExtendedConfigControllers_GetRequests_ReturnsConfigurationPayloads()
    {
        var configRoutes = new[]
        {
            "/api/v1/config/peerprotocol",
            "/api/v1/config/protocols",
            "/api/v1/config/scheduler",
            "/api/v1/config/seeding",
            "/api/v1/config/simulation",
            "/api/v1/config/trackerserver",
            "/api/v1/config/ai",
        };

        foreach (var route in configRoutes)
        {
            var response = await this.Client.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"route {route} should return OK");
            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrWhiteSpace();
            using var doc = JsonDocument.Parse(content);
            doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        }
    }

    [Test]
    public async Task CategoriesAndTags_GetEndpoints_ReturnsCollections()
    {
        // 1. Categories endpoints
        var categoriesResp = await this.Client.GetAsync("/api/v1/categories");
        categoriesResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var categoryResp = await this.Client.GetAsync("/api/v1/category");
        categoryResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Tags endpoints
        var tagsResp = await this.Client.GetAsync("/api/v1/tags");
        tagsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var tagResp = await this.Client.GetAsync("/api/v1/tag");
        tagResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Speed schedule endpoint
        var speedResp = await this.Client.GetAsync("/api/v1/speedschedule");
        speedResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task CategoryController_CreateUpdateGetDelete_OperatesCorrectly()
    {
        // 1. Create a category
        var newCat = new
        {
            name = "TestCategoryIntegration",
            path = "/downloads/testcat",
            savePath = "/downloads/testcat",
        };

        var postResp = await this.Client.PostAsJsonAsync("/api/v1/categories", newCat);
        postResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var createdJson = await postResp.Content.ReadAsStringAsync();
        using var createdDoc = JsonDocument.Parse(createdJson);
        var createdId = createdDoc.RootElement.GetProperty("id").GetInt32();
        createdId.Should().BeGreaterThan(0);

        // 2. Get by Id
        var getByIdResp = await this.Client.GetAsync($"/api/v1/categories/{createdId}");
        getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Delete category
        var delResp = await this.Client.DeleteAsync($"/api/v1/categories/{createdId}");
        delResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Test]
    public async Task TagController_CreateAndManage_OperatesCorrectly()
    {
        // 1. Create tag
        var newTag = new
        {
            label = "IntegrationTagAlpha",
        };

        var postResp = await this.Client.PostAsJsonAsync("/api/v1/tag", newTag);
        postResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var createdJson = await postResp.Content.ReadAsStringAsync();
        using var createdDoc = JsonDocument.Parse(createdJson);
        var createdId = createdDoc.RootElement.GetProperty("id").GetInt32();
        createdId.Should().BeGreaterThan(0);

        // 2. Get all tags and verify presence
        var allResp = await this.Client.GetAsync("/api/v1/tag");
        allResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var allJson = await allResp.Content.ReadAsStringAsync();
        allJson.Should().Contain("IntegrationTagAlpha");

        // 3. Delete tag
        var delResp = await this.Client.DeleteAsync($"/api/v1/tag/{createdId}");
        delResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}

// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TagAndCategoryManagementIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Tags_FullCrudAndValidationLifecycle_Succeeds()
    {
        // 1. GET /api/v1/tags initially
        var getAllResp = await this.Client.GetAsync("/api/v1/tags");
        getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. POST /api/v1/tags with valid tag
        var createPayload = new { label = "integration-tag-1" };
        var createResp = await this.PostJsonAsync("/api/v1/tags", createPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var tagId = createDoc.RootElement.GetProperty("id").GetInt32();
        tagId.Should().BeGreaterThan(0);
        createDoc.RootElement.GetProperty("label").GetString().Should().Be("integration-tag-1");

        // 3. POST /api/v1/tags idempotency (returns existing tag)
        var duplicateCreateResp = await this.PostJsonAsync("/api/v1/tags", createPayload);
        duplicateCreateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var duplicateDoc = JsonDocument.Parse(await duplicateCreateResp.Content.ReadAsStringAsync());
        duplicateDoc.RootElement.GetProperty("id").GetInt32().Should().Be(tagId);

        // 4. POST /api/v1/tags validations
        var emptyTagResp = await this.PostJsonAsync("/api/v1/tags", new { label = "   " });
        emptyTagResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var commaTagResp = await this.PostJsonAsync("/api/v1/tags", new { label = "tag,with,comma" });
        commaTagResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var nullCharTagResp = await this.PostJsonAsync("/api/v1/tags", new { label = "tag\0bad" });
        nullCharTagResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var longTagResp = await this.PostJsonAsync("/api/v1/tags", new { label = new string('x', 101) });
        longTagResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 5. GET /api/v1/tags/{id}
        var getByIdResp = await this.Client.GetAsync($"/api/v1/tags/{tagId}");
        getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getDoc = JsonDocument.Parse(await getByIdResp.Content.ReadAsStringAsync());
        getDoc.RootElement.GetProperty("label").GetString().Should().Be("integration-tag-1");

        var notFoundResp = await this.Client.GetAsync("/api/v1/tags/999999");
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 6. PUT /api/v1/tags/{id}
        var updatePayload = new { id = tagId, label = "integration-tag-updated" };
        var updateResp = await this.PutJsonAsync($"/api/v1/tags/{tagId}", updatePayload);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // PUT validation: invalid characters
        var badUpdateResp = await this.PutJsonAsync($"/api/v1/tags/{tagId}", new { id = tagId, label = "bad,comma" });
        badUpdateResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // PUT non-existent tag
        var notFoundUpdateResp = await this.PutJsonAsync("/api/v1/tags/999999", new { id = 999999, label = "not-found" });
        notFoundUpdateResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 7. DELETE /api/v1/tags/{id}
        var delResp = await this.DeleteAsync($"/api/v1/tags/{tagId}");
        delResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify deleted
        var getDeletedResp = await this.Client.GetAsync($"/api/v1/tags/{tagId}");
        getDeletedResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Categories_FullCrudAndValidationLifecycle_Succeeds()
    {
        // 1. GET /api/v1/category
        var getAllResp = await this.Client.GetAsync("/api/v1/category");
        getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. POST /api/v1/category with valid data
        var categoryPayload = new
        {
            name = "integration-category-1",
            savePath = "/downloads/integration-cat-1",
            defaultDownloadLimit = 5242880,
            defaultUploadLimit = 2097152,
            targetRatio = 2.5,
            targetSeedTimeMinutes = 1440,
        };
        var createResp = await this.PostJsonAsync("/api/v1/category", categoryPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var catId = createDoc.RootElement.GetProperty("id").GetInt32();
        catId.Should().BeGreaterThan(0);
        createDoc.RootElement.GetProperty("name").GetString().Should().Be("integration-category-1");

        // 3. POST /api/v1/category validations
        var emptyNameResp = await this.PostJsonAsync("/api/v1/category", new { name = "" });
        emptyNameResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var nullCharResp = await this.PostJsonAsync("/api/v1/category", new { name = "cat\0bad" });
        nullCharResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var negLimitResp = await this.PostJsonAsync("/api/v1/category", new { name = "cat-neg", defaultDownloadLimit = -10 });
        negLimitResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var negRatioResp = await this.PostJsonAsync("/api/v1/category", new { name = "cat-ratio", targetRatio = -1.0 });
        negRatioResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Duplicate name rejected
        var duplicateResp = await this.PostJsonAsync("/api/v1/category", categoryPayload);
        duplicateResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 4. GET /api/v1/category/{id}
        var getByIdResp = await this.Client.GetAsync($"/api/v1/category/{catId}");
        getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getDoc = JsonDocument.Parse(await getByIdResp.Content.ReadAsStringAsync());
        getDoc.RootElement.GetProperty("name").GetString().Should().Be("integration-category-1");

        var notFoundResp = await this.Client.GetAsync("/api/v1/category/999999");
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 5. PUT /api/v1/category/{id}
        var updatePayload = new
        {
            id = catId,
            name = "integration-category-updated",
            savePath = "/downloads/integration-cat-updated",
            defaultDownloadLimit = 10485760,
            defaultUploadLimit = 5242880,
            targetRatio = 3.0,
            targetSeedTimeMinutes = 2880,
        };
        var updateResp = await this.PutJsonAsync($"/api/v1/category/{catId}", updatePayload);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateNotFoundResp = await this.PutJsonAsync("/api/v1/category/999999", new { id = 999999, name = "missing" });
        updateNotFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 6. DELETE /api/v1/category/{id}
        var delResp = await this.DeleteAsync($"/api/v1/category/{catId}");
        delResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Verify deleted
        var getDeletedResp = await this.Client.GetAsync($"/api/v1/category/{catId}");
        getDeletedResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

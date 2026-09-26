// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ConfigurationAndCategoryIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Category_FullCrudLifecycle_ExecutesSuccessfully()
    {
        const string catName = "cat-integ-test";
        const string catEdited = "cat-integ-edited";

        // 1. Create Category
        var createResp = await this.PostJsonAsync("/api/v1/category", new
        {
            name = catName,
            savePath = "/downloads/" + catName,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var catId = createDoc.RootElement.GetProperty("id").GetInt32();
        catId.Should().BeGreaterThan(0);

        try
        {
            // 2. GET all categories
            var getAllResp = await this.GetAsync("/api/v1/category");
            getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var allJson = await getAllResp.Content.ReadAsStringAsync();
            allJson.Should().Contain(catName);

            // 3. GET by ID
            var getByIdResp = await this.GetAsync($"/api/v1/category/{catId}");
            getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getByIdDoc = JsonDocument.Parse(await getByIdResp.Content.ReadAsStringAsync());
            getByIdDoc.RootElement.GetProperty("name").GetString().Should().Be(catName);

            // 4. PUT update category
            var updateResp = await this.PutJsonAsync($"/api/v1/category/{catId}", new
            {
                id = catId,
                name = catEdited,
                savePath = "/downloads/" + catEdited,
            });
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updateDoc = JsonDocument.Parse(await updateResp.Content.ReadAsStringAsync());
            updateDoc.RootElement.GetProperty("name").GetString().Should().Be(catEdited);
        }
        finally
        {
            // 5. DELETE category
            var deleteResp = await this.DeleteAsync($"/api/v1/category/{catId}");
            deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var verifyDeleted = await this.GetAsync($"/api/v1/category/{catId}");
            verifyDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task Tag_FullCrudLifecycle_ExecutesSuccessfully()
    {
        const string tagLabel = "tag-integ-test";
        const string tagEdited = "tag-integ-edited";

        // 1. Create Tag
        var createResp = await this.PostJsonAsync("/api/v1/tags", new
        {
            label = tagLabel,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var tagId = createDoc.RootElement.GetProperty("id").GetInt32();
        tagId.Should().BeGreaterThan(0);

        try
        {
            // 2. GET all tags
            var getAllResp = await this.GetAsync("/api/v1/tags");
            getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var allJson = await getAllResp.Content.ReadAsStringAsync();
            allJson.Should().Contain(tagLabel);

            // 3. GET by ID
            var getByIdResp = await this.GetAsync($"/api/v1/tags/{tagId}");
            getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getByIdDoc = JsonDocument.Parse(await getByIdResp.Content.ReadAsStringAsync());
            getByIdDoc.RootElement.GetProperty("label").GetString().Should().Be(tagLabel);

            // 4. PUT update tag
            var updateResp = await this.PutJsonAsync($"/api/v1/tags/{tagId}", new
            {
                id = tagId,
                label = tagEdited,
            });
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updateDoc = JsonDocument.Parse(await updateResp.Content.ReadAsStringAsync());
            updateDoc.RootElement.GetProperty("label").GetString().Should().Be(tagEdited);
        }
        finally
        {
            // 5. DELETE tag
            var deleteResp = await this.DeleteAsync($"/api/v1/tags/{tagId}");
            deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var verifyDeleted = await this.GetAsync($"/api/v1/tags/{tagId}");
            verifyDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task SystemConfigAndDiagnostics_AllEndpoints_ReturnValidConfigurations()
    {
        // 1. General Config
        var generalResp = await this.GetAsync("/api/v1/config/general");
        generalResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. BitTorrent Config
        var bittorrentResp = await this.GetAsync("/api/v1/config/bittorrent");
        bittorrentResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var btJson = await bittorrentResp.Content.ReadAsStringAsync();
        using var btDoc = JsonDocument.Parse(btJson);
        btDoc.RootElement.TryGetProperty("downloadDir", out _).Should().BeTrue();

        // 3. Network Config (GET and PUT)
        var networkResp = await this.GetAsync("/api/v1/config/network");
        networkResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var netJson = await networkResp.Content.ReadAsStringAsync();
        var netResource = JsonSerializer.Deserialize<Leecharr.Api.V1.Config.NetworkConfigResource>(netJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var updateNetResp = await this.PutJsonAsync("/api/v1/config/network", netResource);
        updateNetResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted);

        // 5. Seeding Config
        var seedingResp = await this.GetAsync("/api/v1/config/seeding");
        seedingResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Peer Protocol Config
        var peerProtoResp = await this.GetAsync("/api/v1/config/peerprotocol");
        peerProtoResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. Advanced Config
        var advResp = await this.GetAsync("/api/v1/config/advanced");
        advResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. DiskSpace
        var diskSpaceResp = await this.GetAsync("/api/v1/diskspace");
        diskSpaceResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var diskSpaceJson = await diskSpaceResp.Content.ReadAsStringAsync();
        using var diskSpaceDoc = JsonDocument.Parse(diskSpaceJson);
        diskSpaceDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        // 9. SpeedSchedule
        var speedScheduleResp = await this.GetAsync("/api/v1/speedschedule");
        speedScheduleResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

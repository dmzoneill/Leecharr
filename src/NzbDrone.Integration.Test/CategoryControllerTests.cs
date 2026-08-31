// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Categories;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class CategoryControllerTests : IntegrationTestBase
{
    [Test]
    public async Task CategoryCrud_EndToEndFlow_Succeeds()
    {
        // 1. Get initial categories
        var listResponse = await this.GetJsonAsync<List<CategoryResource>>("/api/v1/categories");
        listResponse.Should().NotBeNull();

        // 2. Create new category
        var newCategory = new CategoryResource
        {
            Name = "anime-integration-test",
            SavePath = "/downloads/anime",
            DefaultDownloadLimit = 5000,
            DefaultUploadLimit = 2000,
            TargetRatio = 2.5,
            AutoStop = true,
        };

        var postResponse = await this.PostJsonAsync("/api/v1/categories", newCategory);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = Deserialize<CategoryResource>(await postResponse.Content.ReadAsStringAsync());
        created.Id.Should().BeGreaterThan(0);
        created.Name.Should().Be("anime-integration-test");
        created.SavePath.Should().Be("/downloads/anime");

        // 3. Get category by ID
        var getResponse = await this.GetJsonAsync<CategoryResource>($"/api/v1/categories/{created.Id}");
        getResponse.Should().NotBeNull();
        getResponse.Name.Should().Be("anime-integration-test");

        // 4. Update category
        created.SavePath = "/downloads/anime-updated";
        var putResponse = await this.PutJsonAsync($"/api/v1/categories/{created.Id}", created);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = Deserialize<CategoryResource>(await putResponse.Content.ReadAsStringAsync());
        updated.SavePath.Should().Be("/downloads/anime-updated");

        // 5. Delete category
        var deleteResponse = await this.DeleteAsync($"/api/v1/categories/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 6. Verify deleted returns 404
        var getDeletedResponse = await this.GetAsync($"/api/v1/categories/{created.Id}");
        getDeletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

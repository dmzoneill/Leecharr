// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ArrConnectionAndSyncLifecycleIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task ArrConnection_FullCrudAndTest_LifecycleSucceeds()
    {
        // 1. GET /api/v1/arrconnections
        var listResp = await this.Client.GetAsync("/api/v1/arrconnections");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. POST /api/v1/arrconnections (Create Sonarr connection)
        var createReq = new
        {
            name = "Test Sonarr Server",
            arrType = "Sonarr",
            url = "http://sonarr.integration.test:8989",
            apiKey = "sonarr_api_key_12345",
            enable = true,
            syncInterval = 15,
        };
        var createResp = await this.PostJsonAsync("/api/v1/arrconnections", createReq);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var connectionId = createdDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // 3. GET /api/v1/arrconnections/{id}
            var getResp = await this.Client.GetAsync($"/api/v1/arrconnections/{connectionId}");
            getResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. PUT /api/v1/arrconnections/{id} (Update Radarr connection)
            var updateReq = new
            {
                id = connectionId,
                name = "Updated Radarr Server",
                arrType = "Radarr",
                url = "http://radarr.integration.test:7878",
                apiKey = "radarr_api_key_67890",
                enable = false,
                syncInterval = 30,
            };
            var updateResp = await this.PutJsonAsync($"/api/v1/arrconnections/{connectionId}", updateReq);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 5. POST /api/v1/arrconnections/test (Validates controller test logic; returns handled response)
            var testResp = await this.PostJsonAsync("/api/v1/arrconnections/test", updateReq);
            testResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);

            // 6. POST /api/v1/arrsync/sync (Trigger manual Arr sync)
            var syncResp = await this.PostJsonAsync("/api/v1/arrsync/sync", new { });
            syncResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // 7. DELETE /api/v1/arrconnections/{id}
            var delResp = await this.Client.DeleteAsync($"/api/v1/arrconnections/{connectionId}");
            delResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}

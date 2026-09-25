// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.ArrIntegration;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class ArrConnectionAndWebhookIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task ArrConnection_Crud_EndToEndFlow_WithWebhookRegistration_Succeeds()
    {
        using var mockArr = new MockArrServer();

        // 1. Create a new Arr Connection for Sonarr pointing to mock Arr server
        var newConnection = new ArrConnectionResource
        {
            Name = "Integration Sonarr",
            ArrType = "Sonarr",
            Url = mockArr.Url,
            ApiKey = "sonarr-mock-key",
            Enabled = true,
            SyncEnabled = true,
            EnableAutomaticAdd = true,
            WebhookEnabled = true,
            WebhookHost = "leecharr.local:7889"
        };

        var postResponse = await this.PostJsonAsync("/api/v1/arrconnections", newConnection);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = Deserialize<ArrConnectionResource>(await postResponse.Content.ReadAsStringAsync());
        created.Id.Should().BeGreaterThan(0);
        created.Name.Should().Be("Integration Sonarr");
        created.ArrType.Should().Be("Sonarr");

        // Verify webhook was registered in the mock Arr instance
        mockArr.Notifications.Should().HaveCount(1);
        mockArr.Notifications[0]["name"].ToString().Should().Be("Leecharr");

        // 2. Query connection by ID
        var getResponse = await this.GetJsonAsync<ArrConnectionResource>($"/api/v1/arrconnections/{created.Id}");
        getResponse.Should().NotBeNull();
        getResponse.Id.Should().Be(created.Id);

        // 3. Update connection
        created.Name = "Integration Sonarr Updated";
        var putResponse = await this.PutJsonAsync($"/api/v1/arrconnections/{created.Id}", created);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = Deserialize<ArrConnectionResource>(await putResponse.Content.ReadAsStringAsync());
        updated.Name.Should().Be("Integration Sonarr Updated");

        // 4. Delete connection
        var deleteResponse = await this.DeleteAsync($"/api/v1/arrconnections/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify webhook was unregistered from mock Arr instance
        mockArr.Notifications.Should().BeEmpty();

        // Verify 404 on get
        var getDeleted = await this.GetAsync($"/api/v1/arrconnections/{created.Id}");
        getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ArrConnection_WhenSeedarrWebhookExists_DoesNotOverwriteSeedarr_AndRegistersLeecharrAlongside()
    {
        using var mockArr = new MockArrServer();

        // Pre-register an existing Seedarr webhook notification in Sonarr
        var seedarrWebhookUrl = "http://seedarr.local:9898/api/v1/webhook/arr";
        mockArr.AddExistingNotification("Seedarr", seedarrWebhookUrl);
        mockArr.Notifications.Should().HaveCount(1);

        // Register Leecharr connection to the same Sonarr instance
        var leecharrConnection = new ArrConnectionResource
        {
            Name = "Shared Sonarr",
            ArrType = "Sonarr",
            Url = mockArr.Url,
            ApiKey = "sonarr-mock-key",
            Enabled = true,
            SyncEnabled = true,
            EnableAutomaticAdd = true,
            WebhookEnabled = true,
            WebhookHost = "leecharr.local:7889"
        };

        var postResponse = await this.PostJsonAsync("/api/v1/arrconnections", leecharrConnection);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = Deserialize<ArrConnectionResource>(await postResponse.Content.ReadAsStringAsync());

        // CRITICAL CHECK: Both Seedarr and Leecharr must exist concurrently!
        // Leecharr must NOT have clobbered or overwritten Seedarr's webhook.
        mockArr.Notifications.Should().HaveCount(2);

        var seedarrNotif = mockArr.Notifications.Find(n => n["name"].ToString() == "Seedarr");
        seedarrNotif.Should().NotBeNull();

        var leecharrNotif = mockArr.Notifications.Find(n => n["name"].ToString() == "Leecharr");
        leecharrNotif.Should().NotBeNull();

        // Verify updating Leecharr's connection only updates Leecharr's notification and leaves Seedarr intact
        created.Name = "Shared Sonarr Reconfigured";
        var putResponse = await this.PutJsonAsync($"/api/v1/arrconnections/{created.Id}", created);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        mockArr.Notifications.Should().HaveCount(2);
        mockArr.Notifications.Find(n => n["name"].ToString() == "Seedarr").Should().NotBeNull();

        // Cleanup
        await this.DeleteAsync($"/api/v1/arrconnections/{created.Id}");
    }

    [Test]
    public async Task ArrConnection_Prowlarr_SkipsWebhookRegistration_WithoutErrors()
    {
        using var mockArr = new MockArrServer();

        var prowlarrConnection = new ArrConnectionResource
        {
            Name = "Integration Prowlarr",
            ArrType = "Prowlarr",
            Url = mockArr.Url,
            ApiKey = "prowlarr-mock-key",
            Enabled = true,
            SyncEnabled = true,
            WebhookEnabled = true
        };

        var postResponse = await this.PostJsonAsync("/api/v1/arrconnections", prowlarrConnection);
        postResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = Deserialize<ArrConnectionResource>(await postResponse.Content.ReadAsStringAsync());
        created.Id.Should().BeGreaterThan(0);

        // Prowlarr has no notification endpoint: webhook registration must have been skipped
        mockArr.Notifications.Should().BeEmpty();

        // Cleanup
        await this.DeleteAsync($"/api/v1/arrconnections/{created.Id}");
    }

    [Test]
    public async Task WebhookReceiver_RoutesAndAuth_SingularAndPlural_HandleEvents()
    {
        var testPayload = new
        {
            eventType = "Test",
            instanceName = "Sonarr"
        };

        var grabPayload = new
        {
            eventType = "Grab",
            instanceName = "Sonarr",
            downloadId = "40charinfohash12345678901234567890123456"
        };

        // 1. Anonymous request without API key to /api/v1/webhook/arr must be rejected (401 or 403)
        using var anonClient = new HttpClient { BaseAddress = this.Client.BaseAddress };
        var anonSingularContent = new StringContent(JsonSerializer.Serialize(testPayload), Encoding.UTF8, "application/json");
        var anonSingularResponse = await anonClient.PostAsync("/api/v1/webhook/arr", anonSingularContent);
        anonSingularResponse.StatusCode.Should().Match(s => s == HttpStatusCode.Unauthorized || s == HttpStatusCode.Forbidden);

        // 2. Anonymous request without API key to /api/v1/webhooks/arr (plural alias) must also be rejected (401 or 403)
        var anonPluralContent = new StringContent(JsonSerializer.Serialize(testPayload), Encoding.UTF8, "application/json");
        var anonPluralResponse = await anonClient.PostAsync("/api/v1/webhooks/arr", anonPluralContent);
        anonPluralResponse.StatusCode.Should().Match(s => s == HttpStatusCode.Unauthorized || s == HttpStatusCode.Forbidden);

        // 3. Authenticated request to /api/v1/webhook/arr with 'Test' event returns 200 OK
        var authSingularResponse = await this.PostJsonAsync("/api/v1/webhook/arr", testPayload);
        authSingularResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var singularResult = Deserialize<Dictionary<string, object>>(await authSingularResponse.Content.ReadAsStringAsync());
        singularResult.Should().ContainKey("success");
        singularResult["success"].ToString().Should().Be("True");

        // 4. Authenticated request to /api/v1/webhooks/arr (plural alias) with 'Test' event returns 200 OK
        var authPluralResponse = await this.PostJsonAsync("/api/v1/webhooks/arr", testPayload);
        authPluralResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pluralResult = Deserialize<Dictionary<string, object>>(await authPluralResponse.Content.ReadAsStringAsync());
        pluralResult.Should().ContainKey("success");
        pluralResult["success"].ToString().Should().Be("True");

        // 5. Authenticated request to /api/v1/webhooks/arr with 'Grab' event returns 200 OK
        var authGrabResponse = await this.PostJsonAsync("/api/v1/webhooks/arr", grabPayload);
        authGrabResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

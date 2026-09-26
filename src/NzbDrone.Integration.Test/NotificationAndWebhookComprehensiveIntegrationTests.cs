// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class NotificationAndWebhookComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Notification_FullCrudLifecycle_And_DirectTest_Succeeds()
    {
        const string notifName = "IntegWebhookNotif";
        const string notifEdited = "IntegWebhookNotifEdited";

        // 1. Create Notification
        var createPayload = new
        {
            name = notifName,
            implementation = "Webhook",
            configContract = "WebhookSettings",
            onGrab = true,
            onDownload = true,
            onUpgrade = false,
            onRename = false,
            settings = "{\"url\": \"https://example.com/webhook\", \"method\": \"POST\"}",
        };

        var createResp = await this.PostJsonAsync("/api/v1/notification", createPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var notifId = createDoc.RootElement.GetProperty("id").GetInt32();
        notifId.Should().BeGreaterThan(0);

        try
        {
            // 2. GET all notifications
            var getAllResp = await this.GetAsync("/api/v1/notification");
            getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var allJson = await getAllResp.Content.ReadAsStringAsync();
            allJson.Should().Contain(notifName);

            // 3. GET by ID
            var getByIdResp = await this.GetAsync($"/api/v1/notification/{notifId}");
            getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getByIdDoc = JsonDocument.Parse(await getByIdResp.Content.ReadAsStringAsync());
            getByIdDoc.RootElement.GetProperty("name").GetString().Should().Be(notifName);

            // 4. PUT update notification
            var updatePayload = new
            {
                id = notifId,
                name = notifEdited,
                implementation = "Webhook",
                configContract = "WebhookSettings",
                onGrab = true,
                onDownload = true,
                onUpgrade = true,
                settings = "{\"url\": \"https://example.com/webhook-updated\", \"method\": \"POST\"}",
            };

            var updateResp = await this.PutJsonAsync($"/api/v1/notification/{notifId}", updatePayload);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updateDoc = JsonDocument.Parse(await updateResp.Content.ReadAsStringAsync());
            updateDoc.RootElement.GetProperty("name").GetString().Should().Be(notifEdited);

            // 5. Test notification directly
            var testDirectResp = await this.PostJsonAsync("/api/v1/notification/test", updatePayload);
            testDirectResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // 6. DELETE notification
            var deleteResp = await this.DeleteAsync($"/api/v1/notification/{notifId}");
            deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var verifyDeleted = await this.GetAsync($"/api/v1/notification/{notifId}");
            verifyDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task ArrWebhooks_RadarrAndSonarr_ProcessPayloadsCorrectly()
    {
        // 1. Radarr Test event
        var radarrTestPayload = new
        {
            eventType = "Test",
            instanceName = "Radarr-4k",
        };
        var radarrTestResp = await this.PostJsonAsync("/api/v1/webhooks/radarr", radarrTestPayload);
        radarrTestResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var radarrTestJson = await radarrTestResp.Content.ReadAsStringAsync();
        using var rTestDoc = JsonDocument.Parse(radarrTestJson);
        rTestDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // 2. Radarr Grab event with movie payload
        var radarrGrabPayload = new
        {
            eventType = "Grab",
            instanceName = "Radarr-Prod",
            downloadId = "4444555566667777888899990000111122223333",
            movie = new
            {
                id = 101,
                title = "Inception",
                year = 2010,
                tmdbId = 27205,
                imdbId = "tt1375666",
            },
            release = new
            {
                releaseTitle = "Inception.2010.1080p.BluRay.x264",
                indexer = "ProwlarrIndex",
            },
        };
        var radarrGrabResp = await this.PostJsonAsync("/api/v1/webhooks/radarr", radarrGrabPayload);
        radarrGrabResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Sonarr Test event
        var sonarrTestPayload = new
        {
            eventType = "Test",
            instanceName = "Sonarr-TV",
        };
        var sonarrTestResp = await this.PostJsonAsync("/api/v1/webhooks/sonarr", sonarrTestPayload);
        sonarrTestResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Sonarr Download event with series payload
        var sonarrDownloadPayload = new
        {
            eventType = "Download",
            instanceName = "Sonarr-TV",
            downloadId = "5555666677778888999900001111222233334444",
            series = new
            {
                id = 202,
                title = "Breaking Bad",
                tvdbId = 81189,
            },
            episodeFile = new
            {
                relativePath = "Season 01/Breaking Bad - S01E01.mkv",
                path = "/downloads/Breaking Bad - S01E01.mkv",
            },
        };
        var sonarrDownloadResp = await this.PostJsonAsync("/api/v1/webhooks/sonarr", sonarrDownloadPayload);
        sonarrDownloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

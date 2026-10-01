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
public class AutomationAndMarketplaceComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task AutomationScripts_FullCrudAndExecutionLifecycle_Succeeds()
    {
        // 1. GET /api/v1/automation
        var getAllResp = await this.Client.GetAsync("/api/v1/automation");
        getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. POST /api/v1/automation - Create script
        var createPayload = new
        {
            name = "IntegrationTestYamlRule",
            description = "Test automation description",
            trigger = "torrentCompleted",
            language = "yaml",
            code = "rules:\n  - name: Tag finished movies\n    conditions:\n      category: movies\n    actions:\n      addTag: CompletedMovie\n",
            isEnabled = true,
            targetCategories = new List<string> { "movies" },
        };

        var createResp = await this.PostJsonAsync("/api/v1/automation", createPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var scriptId = createDoc.RootElement.GetProperty("id").GetInt32();
        createDoc.RootElement.GetProperty("name").GetString().Should().Be("IntegrationTestYamlRule");

        try
        {
            // 3. GET /api/v1/automation/{id}
            var getResp = await this.Client.GetAsync($"/api/v1/automation/{scriptId}");
            getResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getDoc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
            getDoc.RootElement.GetProperty("id").GetInt32().Should().Be(scriptId);

            // 4. PUT /api/v1/automation - Update script
            var updatePayload = new
            {
                id = scriptId,
                name = "UpdatedIntegrationTestYamlRule",
                description = "Updated description",
                trigger = "torrentCompleted",
                language = "yaml",
                code = "rules:\n  - name: Tag finished movies\n    conditions:\n      category: movies\n    actions:\n      addTag: UpdatedTag\n",
                isEnabled = true,
                targetCategories = new List<string> { "movies" },
            };

            var putResp = await this.PutJsonAsync("/api/v1/automation", updatePayload);
            putResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var putDoc = JsonDocument.Parse(await putResp.Content.ReadAsStringAsync());
            putDoc.RootElement.GetProperty("name").GetString().Should().Be("UpdatedIntegrationTestYamlRule");

            // 5. POST /api/v1/automation/{id}/run - Execute script
            var runResp = await this.Client.PostAsync($"/api/v1/automation/{scriptId}/run", null);
            runResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var runDoc = JsonDocument.Parse(await runResp.Content.ReadAsStringAsync());
            runDoc.RootElement.TryGetProperty("success", out _).Should().BeTrue();

            // 6. POST /api/v1/automation/test - Test definition with custom inputs
            var testPayload = new
            {
                script = new
                {
                    name = "EphemeralTestScript",
                    trigger = "torrentAdded",
                    language = "yaml",
                    code = "rules:\n  - name: Test rule\n    conditions:\n      ratio: \"> 1.0\"\n    actions:\n      setCategory: HighRatio\n",
                    isEnabled = true,
                },
                customInputs = new Dictionary<string, object>
                {
                    ["ratio"] = 2.5,
                },
            };

            var testResp = await this.PostJsonAsync("/api/v1/automation/test", testPayload);
            testResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var testDoc = JsonDocument.Parse(await testResp.Content.ReadAsStringAsync());
            testDoc.RootElement.TryGetProperty("success", out _).Should().BeTrue();
        }
        finally
        {
            // 7. DELETE /api/v1/automation/{id}
            var delResp = await this.DeleteAsync($"/api/v1/automation/{scriptId}");
            delResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var verifyNotFound = await this.Client.GetAsync($"/api/v1/automation/{scriptId}");
            verifyNotFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task AutomationMarketplace_GetTemplatesAndInstall_InstallsScriptSuccessfully()
    {
        // 1. GET /api/v1/automation/marketplace
        var marketResp = await this.Client.GetAsync("/api/v1/automation/marketplace");
        marketResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var marketDoc = JsonDocument.Parse(await marketResp.Content.ReadAsStringAsync());
        marketDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

        // 2. POST /api/v1/automation/marketplace/install
        var installPayload = new
        {
            templateId = "pt-auto-zap-tokens",
            customName = "Installed Auto Zap Script",
            customInputs = new Dictionary<string, string>
            {
                ["tracker_domain"] = "mytracker.example.org",
                ["session_cookie"] = "secret_session_token",
            },
        };

        var installResp = await this.PostJsonAsync("/api/v1/automation/marketplace/install", installPayload);
        installResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var installDoc = JsonDocument.Parse(await installResp.Content.ReadAsStringAsync());
        var installedId = installDoc.RootElement.GetProperty("id").GetInt32();
        installDoc.RootElement.GetProperty("name").GetString().Should().Be("Installed Auto Zap Script");

        try
        {
            // Verify installed script is listed
            var getInstalled = await this.Client.GetAsync($"/api/v1/automation/{installedId}");
            getInstalled.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/automation/{installedId}");
        }
    }
}

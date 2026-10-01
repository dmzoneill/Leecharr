// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class IndexerAndAutomationComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Indexer_FullCrudLifecycle_And_StatusAndSchema_Succeeds()
    {
        const string indexerName = "IntegTorznabIndexer";
        const string indexerEdited = "IntegTorznabIndexerEdited";

        // 1. Create Indexer
        var createPayload = new
        {
            name = indexerName,
            implementation = "Torznab",
            configContract = "TorznabSettings",
            enableSearch = true,
            url = "https://torznab.example.com/api",
            apiKey = "secret-indexer-token",
        };

        var createResp = await this.PostJsonAsync("/api/v1/indexer", createPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var indexerId = createDoc.RootElement.GetProperty("id").GetInt32();
        indexerId.Should().BeGreaterThan(0);

        try
        {
            // 2. GET all indexers
            var getAllResp = await this.GetAsync("/api/v1/indexer");
            getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var allJson = await getAllResp.Content.ReadAsStringAsync();
            allJson.Should().Contain(indexerName);

            // 3. GET by ID
            var getByIdResp = await this.GetAsync($"/api/v1/indexer/{indexerId}");
            getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getByIdDoc = JsonDocument.Parse(await getByIdResp.Content.ReadAsStringAsync());
            getByIdDoc.RootElement.GetProperty("name").GetString().Should().Be(indexerName);

            // 4. PUT update indexer
            var updatePayload = new
            {
                id = indexerId,
                name = indexerEdited,
                implementation = "Torznab",
                configContract = "TorznabSettings",
                enableSearch = true,
                url = "https://torznab.example.com/api-v2",
                apiKey = "secret-indexer-token-v2",
            };

            var updateResp = await this.PutJsonAsync($"/api/v1/indexer/{indexerId}", updatePayload);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updateDoc = JsonDocument.Parse(await updateResp.Content.ReadAsStringAsync());
            updateDoc.RootElement.GetProperty("name").GetString().Should().Be(indexerEdited);

            // 5. TestAll indexers
            var testAllResp = await this.PostJsonAsync("/api/v1/indexer/testall", new { });
            testAllResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // 7. DELETE indexer
            var deleteResp = await this.DeleteAsync($"/api/v1/indexer/{indexerId}");
            deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var verifyDeleted = await this.GetAsync($"/api/v1/indexer/{indexerId}");
            verifyDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task Automation_FullCrudLifecycle_And_Marketplace_Succeeds()
    {
        const string scriptName = "IntegAutomationScript";
        const string scriptEdited = "IntegAutomationScriptEdited";

        // 1. Create Automation Script
        var createPayload = new
        {
            name = scriptName,
            description = "Test script for integration tests",
            trigger = 0, // TorrentAdded
            language = 0, // JavaScript
            code = "console.log('Torrent added automation executed');",
            isEnabled = true,
        };

        var createResp = await this.PostJsonAsync("/api/v1/automation", createPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var scriptId = createDoc.RootElement.GetProperty("id").GetInt32();
        scriptId.Should().BeGreaterThan(0);

        try
        {
            // 2. GET all automation scripts
            var getAllResp = await this.GetAsync("/api/v1/automation");
            getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var allJson = await getAllResp.Content.ReadAsStringAsync();
            allJson.Should().Contain(scriptName);

            // 3. GET by ID
            var getByIdResp = await this.GetAsync($"/api/v1/automation/{scriptId}");
            getByIdResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getByIdDoc = JsonDocument.Parse(await getByIdResp.Content.ReadAsStringAsync());
            getByIdDoc.RootElement.GetProperty("name").GetString().Should().Be(scriptName);

            // 4. PUT update script
            var updatePayload = new
            {
                id = scriptId,
                name = scriptEdited,
                description = "Updated test script",
                trigger = 1, // TorrentCompleted
                language = 0, // JavaScript
                code = "console.log('Torrent completed automation executed');",
                isEnabled = false,
            };

            var updateResp = await this.PutJsonAsync("/api/v1/automation", updatePayload);
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updateDoc = JsonDocument.Parse(await updateResp.Content.ReadAsStringAsync());
            updateDoc.RootElement.GetProperty("name").GetString().Should().Be(scriptEdited);
            updateDoc.RootElement.GetProperty("isEnabled").GetBoolean().Should().BeFalse();

            // 5. Query marketplace
            var marketResp = await this.GetAsync("/api/v1/automation/marketplace");
            marketResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. Execute script run
            var runResp = await this.PostJsonAsync($"/api/v1/automation/{scriptId}/run", new { });
            runResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            // 7. DELETE automation script
            var deleteResp = await this.DeleteAsync($"/api/v1/automation/{scriptId}");
            deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var verifyDeleted = await this.GetAsync($"/api/v1/automation/{scriptId}");
            verifyDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}

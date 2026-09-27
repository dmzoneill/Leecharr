// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class IndexerManagementDeepComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string GrabHash = "f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1";

    [Test]
    public async Task Indexer_FullCrudAndSearchAndDownload_OperateSuccessfully()
    {
        // 1. GET /api/v1/indexer
        var getAllResp = await this.Client.GetAsync("/api/v1/indexer");
        getAllResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. GET /api/v1/indexer/search
        var searchGetResp = await this.Client.GetAsync("/api/v1/indexer/search?query=Avatar");
        searchGetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. POST /api/v1/indexer/search
        var searchPostResp = await this.PostJsonAsync("/api/v1/indexer/search", new
        {
            query = "Matrix",
            limit = 10,
        });
        searchPostResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. POST /api/v1/indexer/testall
        var testAllResp = await this.Client.PostAsync("/api/v1/indexer/testall", null);
        testAllResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. POST /api/v1/indexer - Create indexer
        var createPayload = new
        {
            name = "TestTorznabIndexer",
            implementation = "Torznab",
            configContract = "TorznabSettings",
            enableRss = true,
            enableAutomaticSearch = true,
            enableInteractiveSearch = true,
            url = "http://127.0.0.1:9696/1/api",
            apiKey = "sample_torznab_key_abcdef",
            categories = new List<int> { 2000, 5000 },
        };

        var createResp = await this.PostJsonAsync("/api/v1/indexer", createPayload);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var indexerId = createDoc.RootElement.GetProperty("id").GetInt32();
        createDoc.RootElement.GetProperty("name").GetString().Should().Be("TestTorznabIndexer");

        try
        {
            // 6. GET /api/v1/indexer/{id}
            var getResp = await this.Client.GetAsync($"/api/v1/indexer/{indexerId}");
            getResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var getDoc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
            getDoc.RootElement.GetProperty("id").GetInt32().Should().Be(indexerId);

            // 7. PUT /api/v1/indexer/{id} - Update indexer
            var updatePayload = new
            {
                id = indexerId,
                name = "UpdatedTorznabIndexer",
                implementation = "Torznab",
                configContract = "TorznabSettings",
                enableRss = true,
                enableAutomaticSearch = false,
                enableInteractiveSearch = true,
                url = "http://127.0.0.1:9696/1/api",
                apiKey = "sample_torznab_key_abcdef",
                categories = new List<int> { 2000 },
            };

            var putResp = await this.PutJsonAsync($"/api/v1/indexer/{indexerId}", updatePayload);
            putResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var putDoc = JsonDocument.Parse(await putResp.Content.ReadAsStringAsync());
            putDoc.RootElement.GetProperty("name").GetString().Should().Be("UpdatedTorznabIndexer");

            // 8. POST /api/v1/indexer/download - Grab release via fallback magnet
            var downloadPayload = new
            {
                title = "IndexerDownloadedMovie",
                infoHash = GrabHash,
                category = "movies",
                minimumRatio = 1.5,
                minimumSeedTime = 3600,
                indexerId = indexerId,
                indexerName = "UpdatedTorznabIndexer",
            };

            var dlResp = await this.PostJsonAsync("/api/v1/indexer/download", downloadPayload);
            dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dlDoc = JsonDocument.Parse(await dlResp.Content.ReadAsStringAsync());
            dlDoc.RootElement.GetProperty("infoHash").GetString().ToLowerInvariant().Should().Be(GrabHash);
        }
        finally
        {
            // 9. Cleanup downloaded torrent
            await this.DeleteAsync($"/api/v1/torrents/{GrabHash}?deleteFiles=true");

            // 10. DELETE /api/v1/indexer/{id}
            var delResp = await this.DeleteAsync($"/api/v1/indexer/{indexerId}");
            delResp.StatusCode.Should().Be(HttpStatusCode.OK);

            var verifyNotFound = await this.Client.GetAsync($"/api/v1/indexer/{indexerId}");
            verifyNotFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}

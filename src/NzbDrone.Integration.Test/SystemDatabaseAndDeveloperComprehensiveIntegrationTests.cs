// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SystemDatabaseAndDeveloperComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task SystemDatabase_TablesAndStorageAndDiagnostics_ReturnValidData()
    {
        // 1. GET /api/v1/system/database/tables
        var tablesResp = await this.Client.GetAsync("/api/v1/system/database/tables");
        tablesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tablesJson = await tablesResp.Content.ReadAsStringAsync();
        using var tablesDoc = JsonDocument.Parse(tablesJson);
        tablesDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

        // 2. GET /api/v1/system/database/storage
        var storageResp = await this.Client.GetAsync("/api/v1/system/database/storage");
        storageResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var storageJson = await storageResp.Content.ReadAsStringAsync();
        using var storageDoc = JsonDocument.Parse(storageJson);
        storageDoc.RootElement.GetProperty("pageSize").GetInt64().Should().BeGreaterThan(0);

        // 3. GET /api/v1/system/database/diagnostics
        var diagResp = await this.Client.GetAsync("/api/v1/system/database/diagnostics");
        diagResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var diagJson = await diagResp.Content.ReadAsStringAsync();
        using var diagDoc = JsonDocument.Parse(diagJson);
        diagDoc.RootElement.GetProperty("integrityCheck").GetString().Should().Be("ok");

        // 4. POST /api/v1/system/database/query - ReadOnly query
        var queryPayload = new
        {
            query = "SELECT 'SystemDbTest' AS label, 42 AS value;",
            readOnly = true,
        };

        var queryResp = await this.PostJsonAsync("/api/v1/system/database/query", queryPayload);
        queryResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var queryJson = await queryResp.Content.ReadAsStringAsync();
        using var queryDoc = JsonDocument.Parse(queryJson);
        queryDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        queryDoc.RootElement.GetProperty("rows").GetArrayLength().Should().Be(1);

        // 5. POST /api/v1/system/database/query - Mutating query in read-only mode should fail
        var writeInReadOnlyPayload = new
        {
            query = "DROP TABLE non_existent_table;",
            readOnly = true,
        };

        var writeFailResp = await this.PostJsonAsync("/api/v1/system/database/query", writeInReadOnlyPayload);
        writeFailResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SystemDeveloper_CommandsAndPragmaOptimizeAndWebhooks_OperateSuccessfully()
    {
        // 1. GET /api/v1/system/developer/commands
        var cmdResp = await this.Client.GetAsync("/api/v1/system/developer/commands");
        cmdResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cmdJson = await cmdResp.Content.ReadAsStringAsync();
        using var cmdDoc = JsonDocument.Parse(cmdJson);
        cmdDoc.RootElement.TryGetProperty("commands", out var cmdList).Should().BeTrue();
        cmdList.GetArrayLength().Should().BeGreaterThan(0);

        // 2. GET /api/v1/system/developer/webhooks/templates
        var tmplResp = await this.Client.GetAsync("/api/v1/system/developer/webhooks/templates");
        tmplResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tmplJson = await tmplResp.Content.ReadAsStringAsync();
        using var tmplDoc = JsonDocument.Parse(tmplJson);
        tmplDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

        // 3. POST /api/v1/system/developer/events/publish
        var pubPayload = new
        {
            eventName = "TestSyntheticEvent",
            payloadJson = "{\"test\": 123}",
        };
        var pubResp = await this.PostJsonAsync("/api/v1/system/developer/events/publish", pubPayload);
        pubResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. GET /api/v1/system/developer/events
        var getEventsResp = await this.Client.GetAsync("/api/v1/system/developer/events");
        getEventsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. DELETE /api/v1/system/developer/events
        var clearEventsResp = await this.Client.DeleteAsync("/api/v1/system/developer/events");
        clearEventsResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

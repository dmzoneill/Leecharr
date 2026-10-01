// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SystemDeveloperAndNetworkDiagnosticsIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task SystemDeveloper_EventsPublishQueryAndClear_Succeeds()
    {
        // 1. Publish synthetic developer event
        var pubResp = await this.PostJsonAsync("/api/v1/system/developer/events/publish", new
        {
            eventName = "IntegrationTestHeartbeat",
            payloadJson = "{\"status\":\"healthy\",\"source\":\"nunit\"}",
        });
        pubResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Query developer events
        var getResp = await this.Client.GetAsync("/api/v1/system/developer/events");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("events", out var eventsArray).Should().BeTrue();
        eventsArray.GetArrayLength().Should().Be(1);
        eventsArray[0].GetProperty("eventName").GetString().Should().Be("IntegrationTestHeartbeat");
        eventsArray[0].GetProperty("payloadJson").GetString().Should().Be("{\"status\":\"healthy\",\"source\":\"nunit\"}");

        // 3. Clear developer events
        var delResp = await this.Client.DeleteAsync("/api/v1/system/developer/events");
        delResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Empty eventName returns BadRequest
        var emptyResp = await this.PostJsonAsync("/api/v1/system/developer/events/publish", new { eventName = "" });
        emptyResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SystemResourcesAndDatabase_DiagnosticsEndpoints_Succeed()
    {
        // 1. System resources endpoints
        var resResp = await this.Client.GetAsync("/api/v1/system/resources");
        resResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var hostResp = await this.Client.GetAsync("/api/v1/system/resources/host");
        hostResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var engineResp = await this.Client.GetAsync("/api/v1/system/resources/engine");
        engineResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. System database endpoints
        var tablesResp = await this.Client.GetAsync("/api/v1/system/database/tables");
        tablesResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var schemaResp = await this.Client.GetAsync("/api/v1/system/database/schema");
        schemaResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var storageResp = await this.Client.GetAsync("/api/v1/system/database/storage");
        storageResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Health & Update endpoints
        var healthResp = await this.Client.GetAsync("/api/v1/health");
        healthResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateResp = await this.Client.GetAsync("/api/v1/update");
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Logs endpoints
        var logResp = await this.Client.GetAsync("/api/v1/log");
        logResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var logFileResp = await this.Client.GetAsync("/api/v1/logfile");
        logFileResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Network_StatusAndDiagnostics_EndpointsSucceed()
    {
        // 1. GET /api/v1/network/status
        var statusResp = await this.Client.GetAsync("/api/v1/network/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. GET /api/v1/network/addresses
        var addrResp = await this.Client.GetAsync("/api/v1/network/addresses");
        addrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. GET /api/v1/network/diagnostics
        var diagResp = await this.Client.GetAsync("/api/v1/network/diagnostics");
        diagResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

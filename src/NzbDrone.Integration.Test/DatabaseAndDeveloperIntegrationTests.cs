// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class DatabaseAndDeveloperIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task SystemDatabase_Tables_ReturnsAllTablesAndColumnDefinitions()
    {
        var response = await this.GetAsync("/api/v1/system/database/tables");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Array);

        var tableNames = root.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        tableNames.Should().Contain("Torrents");
        tableNames.Should().Contain("Categories");
        tableNames.Should().Contain("Config");
    }

    [Test]
    public async Task SystemDatabase_Schema_ReturnsErdStructure()
    {
        var response = await this.GetAsync("/api/v1/system/database/schema");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("tables", out var tables).Should().BeTrue();
        tables.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task SystemDatabase_Storage_ReturnsPageCountAndSize()
    {
        var response = await this.GetAsync("/api/v1/system/database/storage");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("pageSize", out _).Should().BeTrue();
        root.TryGetProperty("pageCount", out _).Should().BeTrue();
    }

    [Test]
    public async Task SystemDatabase_ReadOnlyQuery_ExecutesSuccessfully()
    {
        var queryBody = new
        {
            query = "SELECT COUNT(*) as count FROM Torrents",
            readOnly = true,
        };
        var response = await this.PostJsonAsync("/api/v1/system/database/query", queryBody);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("rows", out var rows).Should().BeTrue();
        rows.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task SystemDeveloper_Events_ReturnsEventBusStatistics()
    {
        var response = await this.GetAsync("/api/v1/system/developer/events");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("events", out var events).Should().BeTrue();
        events.ValueKind.Should().Be(JsonValueKind.Array);
    }
}

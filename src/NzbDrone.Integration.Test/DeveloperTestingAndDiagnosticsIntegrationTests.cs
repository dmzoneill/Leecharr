// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Developer.Debugger;
using NzbDrone.Core.Developer.Diagnostics;
using NzbDrone.Core.Developer.Repl;
using NzbDrone.Core.Developer.Testing;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class DeveloperTestingAndDiagnosticsIntegrationTests : IntegrationTestBase
{
    // ==========================================
    // 1. IN-APP TEST RUNNER ENDPOINTS
    // ==========================================

    [Test]
    public async Task Testing_GetTests_DiscoversBuiltInDiagnosticSmokeTests()
    {
        var response = await this.Client.GetAsync("/api/v1/system/developer/testing/tests");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var tests = JsonSerializer.Deserialize<List<DeveloperTestItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        tests.Should().NotBeNull();
        tests.Should().NotBeEmpty();
        tests.Should().Contain(t => t.Id == "db-integrity");
        tests.Should().Contain(t => t.Id == "disk-io");
        tests.Should().Contain(t => t.Id == "memory-health");
    }

    [Test]
    public async Task Testing_RunSingleTest_ExecutesAndReturnsPassedResult()
    {
        var response = await this.Client.PostAsync("/api/v1/system/developer/testing/run/disk-io", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<DeveloperTestResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        result.Should().NotBeNull();
        result!.TestId.Should().Be("disk-io");
        result.Status.Should().Be("Passed");
        result.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        result.Output.Should().Contain("Successfully wrote and read");
    }

    [Test]
    public async Task Testing_RunBatchTests_FiltersByCategoryAndReturnsSummary()
    {
        var request = new TestExecutionRequest
        {
            Category = "Database",
        };

        var response = await this.PostJsonAsync("/api/v1/system/developer/testing/run", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var execResponse = JsonSerializer.Deserialize<TestExecutionResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        execResponse.Should().NotBeNull();
        execResponse!.TotalTests.Should().BeGreaterThanOrEqualTo(1);
        execResponse.Passed.Should().BeGreaterThanOrEqualTo(1);
        execResponse.Results.Should().NotBeEmpty();
        execResponse.Results.Should().OnlyContain(r => r.Category == "Database");
    }

    [Test]
    public async Task Testing_HistoryAndClear_ManagesExecutionLogCorrectly()
    {
        // 1. Run a test to ensure history is populated
        await this.Client.PostAsync("/api/v1/system/developer/testing/run/memory-health", null);

        // 2. Fetch history
        var historyResp = await this.Client.GetAsync("/api/v1/system/developer/testing/history?limit=10");
        historyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var historyJson = await historyResp.Content.ReadAsStringAsync();
        var history = JsonSerializer.Deserialize<List<DeveloperTestResult>>(historyJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        history.Should().NotBeNull();
        history.Should().NotBeEmpty();

        // 3. Clear history
        var clearResp = await this.Client.DeleteAsync("/api/v1/system/developer/testing/history");
        clearResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4. Verify cleared
        var checkResp = await this.Client.GetAsync("/api/v1/system/developer/testing/history");
        var checkJson = await checkResp.Content.ReadAsStringAsync();
        var cleared = JsonSerializer.Deserialize<List<DeveloperTestResult>>(checkJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        cleared.Should().BeEmpty();
    }

    // ==========================================
    // 2. REAL-TIME DIAGNOSTICS & TELEMETRY
    // ==========================================

    [Test]
    public async Task Diagnostics_GetThreads_ReturnsActiveManagedThreads()
    {
        var response = await this.Client.GetAsync("/api/v1/system/developer/diagnostics/threads");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var threads = JsonSerializer.Deserialize<List<ThreadDiagnosticItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        threads.Should().NotBeNull();
        threads.Should().NotBeEmpty();
        threads![0].ThreadId.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Diagnostics_GetMemory_ReturnsGcAndHeapMetrics()
    {
        var response = await this.Client.GetAsync("/api/v1/system/developer/diagnostics/memory");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var report = JsonSerializer.Deserialize<MemoryDiagnosticReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        report.Should().NotBeNull();
        report!.TotalAllocatedBytes.Should().BeGreaterThan(0);
        report.TotalMemoryBytes.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Diagnostics_GetEnvironment_ReturnsProcessAndHostDetails()
    {
        var response = await this.Client.GetAsync("/api/v1/system/developer/diagnostics/environment");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var env = JsonSerializer.Deserialize<EnvironmentDiagnosticReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        env.Should().NotBeNull();
        env!.ProcessId.Should().BeGreaterThan(0);
        env.ProcessorCount.Should().BeGreaterThan(0);
        env.OsDescription.Should().NotBeNullOrWhiteSpace();
        env.FrameworkDescription.Should().NotBeNullOrWhiteSpace();
        env.ProcessUptime.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Diagnostics_CollectGarbage_TriggersCollectionSuccessfully()
    {
        var request = new GcCollectionRequest
        {
            Generation = 2,
            Compact = true,
            Blocking = true,
        };

        var response = await this.PostJsonAsync("/api/v1/system/developer/diagnostics/memory/gc", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var gcResp = JsonSerializer.Deserialize<GcCollectionResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        gcResp.Should().NotBeNull();
        gcResp!.Success.Should().BeTrue();
        gcResp.Message.Should().Contain("GC Gen2 collection finished");
    }

    // ==========================================
    // 3. INTERACTIVE C# / JS REPL EVALUATOR
    // ==========================================

    [Test]
    public async Task Repl_EvaluateExpression_ReturnsComputedResult()
    {
        var request = new ReplExecutionRequest
        {
            Code = "100 + 42",
            Language = "javascript",
            TimeoutSeconds = 5,
        };

        var response = await this.PostJsonAsync("/api/v1/system/developer/repl/eval", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var replResp = JsonSerializer.Deserialize<ReplExecutionResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        replResp.Should().NotBeNull();
        replResp!.Success.Should().BeTrue();
        replResp.ResultJson.Should().Be("142");
        replResp.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task Repl_ConsoleOutputAndHistory_CapturesLogsAndManagesHistory()
    {
        var request = new ReplExecutionRequest
        {
            Code = "console.log('Testing REPL output line'); 5 * 5",
            Language = "javascript",
        };

        var response = await this.PostJsonAsync("/api/v1/system/developer/repl/eval", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var replResp = JsonSerializer.Deserialize<ReplExecutionResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        replResp.Should().NotBeNull();
        replResp!.Success.Should().BeTrue();
        replResp.Output.Should().Contain("Testing REPL output line");
        replResp.ResultJson.Should().Be("25");

        // Verify history contains execution
        var historyResp = await this.Client.GetAsync("/api/v1/system/developer/repl/history");
        historyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var histJson = await historyResp.Content.ReadAsStringAsync();
        var history = JsonSerializer.Deserialize<List<ReplHistoryEntry>>(histJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        history.Should().NotBeNull();
        history.Should().Contain(h => h.Code.Contains("Testing REPL output line"));

        // Clear history
        var clearResp = await this.Client.DeleteAsync("/api/v1/system/developer/repl/history");
        clearResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Reset session
        var resetResp = await this.Client.DeleteAsync("/api/v1/system/developer/repl/session");
        resetResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ==========================================
    // 4. WEB DEBUGGER & TRACEPOINT FLIGHT RECORDER
    // ==========================================

    [Test]
    public async Task Debugger_StatusAndTracepointsLifecycle_OperatesCorrectly()
    {
        // 1. Get status
        var statusResp = await this.Client.GetAsync("/api/v1/system/developer/debugger/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusJson = await statusResp.Content.ReadAsStringAsync();
        var status = JsonSerializer.Deserialize<DebuggerStatusReport>(statusJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        status.Should().NotBeNull();

        // 2. Add tracepoint
        var tracepoint = new TracepointDefinition
        {
            FilePath = "src/NzbDrone.Core/Torrents/TorrentService.cs",
            LineNumber = 125,
            Condition = "torrent.Id > 0",
        };

        var addResp = await this.PostJsonAsync("/api/v1/system/developer/debugger/tracepoints", tracepoint);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var createdTp = Deserialize<TracepointDefinition>(await addResp.Content.ReadAsStringAsync());
        createdTp.Id.Should().NotBeNullOrWhiteSpace();
        createdTp.LineNumber.Should().Be(125);

        // 3. List tracepoints
        var listResp = await this.Client.GetAsync("/api/v1/system/developer/debugger/tracepoints");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var tps = Deserialize<List<TracepointDefinition>>(await listResp.Content.ReadAsStringAsync());
        tps.Should().Contain(t => t.Id == createdTp.Id);

        // 4. Record snapshot
        var snapshot = new TracepointSnapshot
        {
            TracepointId = createdTp.Id,
            FilePath = createdTp.FilePath,
            LineNumber = createdTp.LineNumber,
            ThreadId = 1,
            CallStack = "at TorrentService.Get(Int32 id)",
            VariablesJson = "{\"id\": 42, \"name\": \"TestTorrent\"}",
        };

        var snapResp = await this.PostJsonAsync("/api/v1/system/developer/debugger/snapshots", snapshot);
        snapResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Get snapshots
        var getSnapsResp = await this.Client.GetAsync("/api/v1/system/developer/debugger/snapshots");
        getSnapsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var snaps = Deserialize<List<TracepointSnapshot>>(await getSnapsResp.Content.ReadAsStringAsync());
        snaps.Should().Contain(s => s.TracepointId == createdTp.Id);

        // 6. Delete single tracepoint
        var delTpResp = await this.Client.DeleteAsync($"/api/v1/system/developer/debugger/tracepoints/{createdTp.Id}");
        delTpResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 7. Clear snapshots
        var clearSnapsResp = await this.Client.DeleteAsync("/api/v1/system/developer/debugger/snapshots");
        clearSnapsResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Debugger_GetFiles_DiscoversKnownSourceFiles()
    {
        var response = await this.Client.GetAsync("/api/v1/system/developer/debugger/files");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var files = JsonSerializer.Deserialize<List<DebuggerSourceFileItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        files.Should().NotBeNull();
        files!.Count.Should().BeGreaterThanOrEqualTo(10);
        files.Should().Contain(f => f.FilePath.EndsWith(".cs"));
    }

    [Test]
    public async Task Debugger_GetSource_ReturnsSourcePreviewOrMetadata()
    {
        var testPath = "src/NzbDrone.Core/Torrents/TorrentService.cs";
        var response = await this.Client.GetAsync($"/api/v1/system/developer/debugger/source?path={testPath}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var sourceResp = JsonSerializer.Deserialize<DebuggerSourceCodeResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        sourceResp.Should().NotBeNull();
        sourceResp!.Exists.Should().BeTrue();
        sourceResp.Content.Should().Contain("TorrentService");
        sourceResp.LineCount.Should().BeGreaterThan(0);
    }

    // ==========================================
    // 5. COMPREHENSIVE WEBHOOKS & TEST EXPANSIONS
    // ==========================================

    [Test]
    public async Task Testing_GetTests_DiscoversComprehensive56TestLibrary()
    {
        var response = await this.Client.GetAsync("/api/v1/system/developer/testing/tests");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var tests = JsonSerializer.Deserialize<List<DeveloperTestItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        tests.Should().NotBeNull();
        tests!.Count.Should().BeGreaterThanOrEqualTo(56);

        var categories = tests.Select(t => t.Category).Distinct().ToList();
        categories.Should().Contain(new[] { "Database", "Storage", "BitTorrent", "Network", "Scheduler", "Messaging", "System", "Configuration" });
    }

    [Test]
    public async Task Testing_RunVariousDiagnosticCategories_ExecutesAndPasses()
    {
        var testCategories = new[] { "Database", "Storage", "BitTorrent", "Network", "Scheduler", "Messaging", "System", "Configuration" };

        foreach (var category in testCategories)
        {
            var request = new TestExecutionRequest { Category = category };
            var response = await this.PostJsonAsync("/api/v1/system/developer/testing/run", request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var json = await response.Content.ReadAsStringAsync();
            var execResponse = JsonSerializer.Deserialize<TestExecutionResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            execResponse.Should().NotBeNull();
            execResponse!.Passed.Should().BeGreaterThanOrEqualTo(1);
            execResponse.Failed.Should().Be(0);
        }
    }

    [Test]
    public async Task Webhooks_GetTemplates_ReturnsComprehensiveLibrary()
    {
        var response = await this.Client.GetAsync("/api/v1/system/developer/webhooks/templates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(30);

        var templateIds = new List<string>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            templateIds.Add(el.GetProperty("id").GetString()!);
        }

        templateIds.Should().Contain(new[]
        {
            "sonarr-grab", "sonarr-download", "sonarr-rename",
            "radarr-grab", "radarr-download", "radarr-delete",
            "lidarr-grab", "lidarr-download",
            "readarr-grab",
            "prowlarr-health",
            "jellyseerr-request",
            "overseerr-approved",
            "discord-webhook",
            "slack-webhook",
            "generic-torrent-completed",
            "whisparr-grab",
            "bazarr-subtitle",
            "jellyfin-playback",
            "ntfy-publish",
            "gotify-push",
        });
    }

    [Test]
    public async Task Webhooks_SimulateNewTemplates_ExecutesAndValidates()
    {
        // 1. Simulate Lidarr grab
        var lidarrPayload = new
        {
            eventType = "Grab",
            artist = new { id = 12, name = "Daft Punk" },
            album = new { id = 305, title = "Random Access Memories" },
            downloadClient = "Leecharr",
        };

        var simRequest1 = new
        {
            eventType = "Grab",
            payloadJson = JsonSerializer.Serialize(lidarrPayload),
        };

        var resp1 = await this.PostJsonAsync("/api/v1/system/developer/webhooks/simulate", simRequest1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Simulate Discord webhook notification
        var discordPayload = new
        {
            username = "Leecharr Bot",
            content = "Download Completed",
        };

        var simRequest2 = new
        {
            eventType = "Notification",
            payloadJson = JsonSerializer.Serialize(discordPayload),
        };

        var resp2 = await this.PostJsonAsync("/api/v1/system/developer/webhooks/simulate", simRequest2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Simulate Whisparr grab
        var whisparrPayload = new
        {
            eventType = "Grab",
            movie = new { id = 401, title = "Summer Vacation" },
            downloadClient = "Leecharr",
        };

        var simRequest3 = new
        {
            eventType = "Grab",
            payloadJson = JsonSerializer.Serialize(whisparrPayload),
        };

        var resp3 = await this.PostJsonAsync("/api/v1/system/developer/webhooks/simulate", simRequest3);
        resp3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Commands_GetCatalog_DiscoversExpandedCommandLibrary()
    {
        var response = await this.Client.GetAsync("/api/v1/system/developer/commands");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var commandsEl = doc.RootElement.GetProperty("commands");
        commandsEl.ValueKind.Should().Be(JsonValueKind.Array);
        commandsEl.GetArrayLength().Should().BeGreaterThanOrEqualTo(20);

        var commandNames = new List<string>();
        foreach (var cmd in commandsEl.EnumerateArray())
        {
            commandNames.Add(cmd.GetProperty("name").GetString()!);
        }

        commandNames.Should().Contain(new[]
        {
            "BackupCommand",
            "ForceGarbageCollectionCommand",
            "VacuumDatabaseCommand",
            "RescanTorrentsCommand",
            "CheckDiskSpaceCommand",
            "RotateLogFilesCommand",
            "ClearDhtCacheCommand",
        });
    }

    [Test]
    public async Task Commands_ExecuteExpandedCommand_QueuesSuccessfully()
    {
        var request = new
        {
            commandName = "ForceGarbageCollectionCommand",
            parameters = new Dictionary<string, object>
            {
                { "Generation", 2 },
                { "CompactLargeObjectHeap", true },
            },
        };

        var response = await this.PostJsonAsync("/api/v1/system/developer/commands/execute", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("name").GetString().Should().Be("ForceGarbageCollectionCommand");
        doc.RootElement.GetProperty("status").GetString().Should().Be("Queued");
    }
}

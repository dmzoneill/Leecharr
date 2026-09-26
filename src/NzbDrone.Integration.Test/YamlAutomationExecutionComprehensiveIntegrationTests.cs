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
public class YamlAutomationExecutionComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string TestHash = "b222222222222222222222222222222222222222";

    [Test]
    public async Task YamlWorkflow_VariablesAndLogging_ExecutesSuccessfully()
    {
        var yamlCode = string.Join("\n", new[]
        {
            "name: Basic Variable Test",
            "steps:",
            "  - name: Step 1 Set Variables",
            "    actions:",
            "      - setVariable:",
            "          key: customKey",
            "          value: customValue123",
            "      - log:",
            "          message: 'Initialized with ${customKey}'",
            "          level: info",
            "  - name: Step 2 Check Condition",
            "    condition: '${customKey} == customValue123'",
            "    actions:",
            "      - log: 'Condition matched successfully'",
        });

        var testReq = new
        {
            script = new
            {
                name = "Test Yaml Variables",
                language = 1, // AutomationLanguage.Yaml
                trigger = 0,
                code = yamlCode,
                inputsJson = "{\"apiKey\":\"secret_123\"}",
            },
            customInputs = new Dictionary<string, object>
            {
                { "environment", "test" },
            },
        };

        var resp = await this.PostJsonAsync("/api/v1/automation/test", testReq);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        var outputLog = root.GetProperty("outputLog").GetString();
        outputLog.Should().Contain("customValue123");
        outputLog.Should().Contain("Condition matched successfully");
    }

    [Test]
    public async Task YamlWorkflow_ConditionalBranchesAndComparisons_EvaluatesCorrectly()
    {
        var yamlCode = string.Join("\n", new[]
        {
            "name: Branching Test",
            "steps:",
            "  - name: True Equal",
            "    condition: '100 == 100'",
            "    actions:",
            "      - log: 'Equal passed'",
            "  - name: False Equal (Should Skip)",
            "    condition: '100 == 200'",
            "    actions:",
            "      - log: 'Should never run'",
            "  - name: Greater Than",
            "    condition: '50 > 10'",
            "    actions:",
            "      - log: 'GT passed'",
            "  - name: Less Than",
            "    condition: '5 < 10'",
            "    actions:",
            "      - log: 'LT passed'",
            "  - name: Greater Than Or Equal",
            "    condition: '25 >= 25'",
            "    actions:",
            "      - log: 'GTE passed'",
            "  - name: Less Than Or Equal",
            "    condition: '30 <= 40'",
            "    actions:",
            "      - log: 'LTE passed'",
            "  - name: Not Equal",
            "    condition: 'alpha != beta'",
            "    actions:",
            "      - log: 'NE passed'",
            "  - name: Boolean Negation",
            "    condition: '!false'",
            "    actions:",
            "      - log: 'NotFalse passed'",
        });

        var testReq = new
        {
            script = new
            {
                name = "Test Yaml Conditionals",
                language = 1,
                trigger = 0,
                code = yamlCode,
            },
        };

        var resp = await this.PostJsonAsync("/api/v1/automation/test", testReq);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var outputLog = doc.RootElement.GetProperty("outputLog").GetString();
        outputLog.Should().Contain("Equal passed");
        outputLog.Should().Contain("GT passed");
        outputLog.Should().Contain("LT passed");
        outputLog.Should().Contain("GTE passed");
        outputLog.Should().Contain("LTE passed");
        outputLog.Should().Contain("NE passed");
        outputLog.Should().Contain("NotFalse passed");
        outputLog.Should().NotContain("Should never run");
    }

    [Test]
    public async Task YamlWorkflow_TorrentActionsAndSystemCommands_ExecutesOnTorrent()
    {
        // Add a temporary torrent
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{TestHash}&dn=YamlAutomationTorrent",
            category = "initial-cat",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            var yamlCode = string.Join("\n", new[]
            {
                "name: Comprehensive Torrent Mutation Workflow",
                "steps:",
                "  - name: Mutate Torrent Properties",
                "    actions:",
                "      - addTag: auto-tagged",
                "      - removeTag: old-tag",
                "      - setCategory: automated-category",
                "      - setUploadLimit: 500",
                "      - setDownloadLimit: 2000",
                "      - setRatioLimit: 2.5",
                "      - setSeedingTimeLimit: 120",
                "      - setPriority: high",
                "      - setSequentialDownload: true",
                "      - setSuperSeeding: false",
                "      - pause: true",
                "      - resume: true",
                "      - recheck: true",
                "      - reannounce: true",
                "      - notifyArr:",
                "          appType: radarr",
                "          instanceId: 1",
                "      - sendNotification:",
                "          title: 'Yaml Automation Done'",
                "          message: 'Processed torrent ${torrent.name}'",
                "      - log: 'Finished mutator for ${torrent.name}'",
            });

            var testReq = new
            {
                script = new
                {
                    name = "Test Yaml Torrent Actions",
                    language = 1,
                    trigger = 0,
                    code = yamlCode,
                },
                torrentId = torrentId,
            };

            var resp = await this.PostJsonAsync("/api/v1/automation/test", testReq);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            var outputLog = doc.RootElement.GetProperty("outputLog").GetString();
            outputLog.Should().Contain("addTag: auto-tagged");
            outputLog.Should().Contain("setCategory: automated-category");
            outputLog.Should().Contain("setUploadLimit: 500 KB/s");
            outputLog.Should().Contain("setDownloadLimit: 2000 KB/s");
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }

    [Test]
    public async Task YamlWorkflow_StopPipelineAndEmptyWorkflows_HandledCleanly()
    {
        // 1. StopPipeline step stops subsequent steps
        var stopYaml = string.Join("\n", new[]
        {
            "name: Stop Pipeline Test",
            "steps:",
            "  - name: Step 1",
            "    actions:",
            "      - log: 'Step 1 Ran'",
            "      - stopPipeline: 'Reached stop condition'",
            "  - name: Step 2",
            "    actions:",
            "      - log: 'Step 2 Should Be Skipped'",
        });

        var stopReq = new
        {
            script = new
            {
                name = "Test Stop Pipeline",
                language = 1,
                trigger = 0,
                code = stopYaml,
            },
        };
        var stopResp = await this.PostJsonAsync("/api/v1/automation/test", stopReq);
        stopResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var stopDoc = JsonDocument.Parse(await stopResp.Content.ReadAsStringAsync());
        var stopLog = stopDoc.RootElement.GetProperty("outputLog").GetString();
        stopLog.Should().Contain("Step 1 Ran");
        stopLog.Should().NotContain("Step 2 Should Be Skipped");

        // 2. Empty YAML code
        var emptyReq = new
        {
            script = new
            {
                name = "Test Empty YAML",
                language = 1,
                trigger = 0,
                code = "",
            },
        };
        var emptyResp = await this.PostJsonAsync("/api/v1/automation/test", emptyReq);
        emptyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyDoc = JsonDocument.Parse(await emptyResp.Content.ReadAsStringAsync());
        emptyDoc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}

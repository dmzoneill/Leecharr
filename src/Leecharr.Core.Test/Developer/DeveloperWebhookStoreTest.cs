// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.System;
using Leecharr.Api.V1.Webhooks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Developer;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Developer;

[TestFixture]
public class DeveloperWebhookStoreTest
{
    private DeveloperWebhookStore store = null!;

    [SetUp]
    public void SetUp()
    {
        this.store = new DeveloperWebhookStore();
    }

    [Test]
    public void Record_WhenCalled_StoresEntryAndReturnsInRecent()
    {
        this.store.Record("Sonarr", "Grab", "192.168.1.100", "Sonarr/4.0.0", 200, "{\"title\":\"Test\"}", "Processed ok", true);

        var items = this.store.GetRecent(10);
        items.Should().HaveCount(1);
        var first = items[0];
        first.Source.Should().Be("Sonarr");
        first.EventType.Should().Be("Grab");
        first.SourceIp.Should().Be("192.168.1.100");
        first.UserAgent.Should().Be("Sonarr/4.0.0");
        first.StatusCode.Should().Be(200);
        first.RawPayload.Should().Be("{\"title\":\"Test\"}");
        first.ResultMessage.Should().Be("Processed ok");
        first.Success.Should().BeTrue();
    }

    [Test]
    public void Clear_WhenCalled_RemovesAllEntries()
    {
        this.store.Record("Sonarr", "Grab", "127.0.0.1", "Arr", 200, "{}", "ok", true);
        this.store.Record("Radarr", "MovieAdd", "127.0.0.1", "Arr", 200, "{}", "ok", true);

        var before = this.store.GetRecent(10);
        before.Should().HaveCount(2);

        this.store.Clear();

        var after = this.store.GetRecent(10);
        after.Should().BeEmpty();
    }

    [Test]
    public void Record_WhenExceedsMaxEntries_CapsEntries()
    {
        for (var i = 0; i < 110; i++)
        {
            this.store.Record("Sonarr", $"Event_{i}", "127.0.0.1", "Arr", 200, "{}", $"Message {i}", true);
        }

        var items = this.store.GetRecent(200);
        items.Count.Should().BeLessOrEqualTo(100);
    }
}

[TestFixture]
public class SystemDeveloperWebhookSimulateTest
{
    private IDeveloperWebhookStore webhookStore = null!;
    private ITorrentRepository torrentRepository = null!;
    private ArrWebhookController arrWebhookController = null!;
    private SystemDeveloperController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.webhookStore = Substitute.For<IDeveloperWebhookStore>();
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.arrWebhookController = new ArrWebhookController(
            this.torrentRepository,
            webhookStore: this.webhookStore);

        this.controller = new SystemDeveloperController(
            webhookStore: this.webhookStore,
            arrWebhookController: this.arrWebhookController);
    }

    [Test]
    public async Task SimulateWebhook_WhenPayloadNullOrWhitespace_ReturnsBadRequest()
    {
        var result = await this.controller.SimulateWebhook(null!);
        result.Result.Should().BeOfType<BadRequestObjectResult>();

        var emptyRequest = new DeveloperWebhookSimulateRequest { PayloadJson = "   " };
        var emptyResult = await this.controller.SimulateWebhook(emptyRequest);
        emptyResult.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public async Task SimulateWebhook_WhenInvalidJson_ReturnsBadRequestAndRecordsFailure()
    {
        var request = new DeveloperWebhookSimulateRequest
        {
            EventType = "Grab",
            PayloadJson = "{ not-valid-json",
        };

        var result = await this.controller.SimulateWebhook(request);
        result.Result.Should().BeOfType<BadRequestObjectResult>();

        var badRequest = (BadRequestObjectResult)result.Result!;
        var response = badRequest.Value as DeveloperWebhookSimulateResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeFalse();
        response.StatusCode.Should().Be(400);

        this.webhookStore.Received(1).Record(
            "Simulator",
            "Grab",
            "127.0.0.1",
            "Leecharr-Developer-Sandbox/1.0",
            400,
            "{ not-valid-json",
            Arg.Is<string>(m => m.Contains("Invalid JSON payload")),
            false);
    }

    [Test]
    public async Task SimulateWebhook_WhenValidRequestAndDispatchToPipeline_DispatchesToArrWebhookPipeline()
    {
        var payload = "{\"eventType\":\"Test\",\"instanceName\":\"Sonarr-Test\"}";
        var request = new DeveloperWebhookSimulateRequest
        {
            EventType = "Test",
            PayloadJson = payload,
            DispatchToPipeline = true,
        };

        var result = await this.controller.SimulateWebhook(request);
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as DeveloperWebhookSimulateResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.StatusCode.Should().Be(200);
        response.Message.Should().Contain("Webhook test received successfully.");
        response.TraceLogs.Should().Contain(log => log.Contains("Dispatching payload to ArrWebhookController pipeline"));

        this.webhookStore.Received(1).Record(
            "Simulator",
            "Test",
            "127.0.0.1",
            "Leecharr-Developer-Sandbox/1.0",
            200,
            payload,
            Arg.Is<string>(m => m.Contains("Webhook test received successfully.")),
            true);
    }

    [Test]
    public async Task SimulateWebhook_WhenDispatchToPipelineFalse_SkipsPipelineDispatch()
    {
        var payload = "{\"eventType\":\"Grab\",\"series\":{\"title\":\"Severance\"}}";
        var request = new DeveloperWebhookSimulateRequest
        {
            EventType = "Grab",
            PayloadJson = payload,
            DispatchToPipeline = false,
        };

        var result = await this.controller.SimulateWebhook(request);
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as DeveloperWebhookSimulateResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.TraceLogs.Should().Contain(log => log.Contains("Pipeline dispatch skipped"));

        this.webhookStore.Received(1).Record(
            "Simulator",
            "Grab",
            "127.0.0.1",
            "Leecharr-Developer-Sandbox/1.0",
            200,
            payload,
            Arg.Is<string>(m => m.Contains("simulation parsed and verified successfully")),
            true);
    }

    [Test]
    public async Task SimulateWebhook_WhenArrWebhookControllerNull_RunsStandaloneVerification()
    {
        var standaloneController = new SystemDeveloperController(webhookStore: this.webhookStore);

        var payload = "{\"eventType\":\"MovieAdd\",\"movie\":{\"title\":\"Inception\"}}";
        var request = new DeveloperWebhookSimulateRequest
        {
            EventType = "MovieAdd",
            PayloadJson = payload,
            DispatchToPipeline = true,
        };

        var result = await standaloneController.SimulateWebhook(request);
        result.Result.Should().BeOfType<OkObjectResult>();

        var okResult = (OkObjectResult)result.Result!;
        var response = okResult.Value as DeveloperWebhookSimulateResponse;
        response.Should().NotBeNull();
        response!.Success.Should().BeTrue();
        response.TraceLogs.Should().Contain(log => log.Contains("standalone verification mode"));

        this.webhookStore.Received(1).Record(
            "Simulator",
            "MovieAdd",
            "127.0.0.1",
            "Leecharr-Developer-Sandbox/1.0",
            200,
            payload,
            Arg.Is<string>(m => m.Contains("simulation parsed and verified successfully")),
            true);
    }
}

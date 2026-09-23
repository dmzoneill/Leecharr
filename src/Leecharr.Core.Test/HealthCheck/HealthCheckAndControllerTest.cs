// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Health;
using Leecharr.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.HealthCheck;

[TestFixture]
public class HealthCheckServiceTest
{
    private IEventAggregator eventAggregator = null!;

    [SetUp]
    public void SetUp()
    {
        this.eventAggregator = Substitute.For<IEventAggregator>();
    }

    [Test]
    public async Task PerformChecksAsync_WhenNoHealthChecksRegistered_ReturnsEmptyList()
    {
        using var service = new HealthCheckService(null!, this.eventAggregator);
        var results = await service.PerformChecksAsync();

        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [Test]
    public async Task PerformChecksAsync_WhenEmptyChecksCollection_ReturnsEmptyList()
    {
        using var service = new HealthCheckService(Array.Empty<IHealthCheck>(), this.eventAggregator);
        var results = await service.PerformChecksAsync();

        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [Test]
    public async Task PerformChecksAsync_WhenDisposed_ReturnsEmptyList()
    {
        var check = Substitute.For<IHealthCheck>();
        check.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Ok("ActiveCheck")));

        var service = new HealthCheckService(new[] { check }, this.eventAggregator);
        service.Dispose();

        var results = await service.PerformChecksAsync();

        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [Test]
    public async Task PerformChecksAsync_AggregatesResultsFromMultipleChecks()
    {
        var check1 = Substitute.For<IHealthCheck>();
        check1.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Ok("CheckOne")));

        var check2 = Substitute.For<IHealthCheck>();
        check2.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Notice("CheckTwo", "Informational notice")));

        var check3 = Substitute.For<IHealthCheck>();
        check3.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Warning("CheckThree", "Warning notice")));

        var check4 = Substitute.For<IHealthCheck>();
        check4.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Error("CheckFour", "Critical error")));

        using var service = new HealthCheckService(
            new[] { check1, check2, check3, check4 },
            this.eventAggregator);

        var results = await service.PerformChecksAsync();

        results.Should().HaveCount(4);
        results.Select(r => r.Source).Should().Contain(new[] { "CheckOne", "CheckTwo", "CheckThree", "CheckFour" });
        results.First(r => r.Source == "CheckOne").Type.Should().Be(HealthCheckResultType.Ok);
        results.First(r => r.Source == "CheckTwo").Type.Should().Be(HealthCheckResultType.Notice);
        results.First(r => r.Source == "CheckThree").Type.Should().Be(HealthCheckResultType.Warning);
        results.First(r => r.Source == "CheckFour").Type.Should().Be(HealthCheckResultType.Error);
    }

    [Test]
    public async Task PerformChecksAsync_WhenCheckReturnsNull_DefaultsToOkWithCheckTypeName()
    {
        var nullCheck = new NullReturningCheck();

        using var service = new HealthCheckService(new[] { nullCheck }, this.eventAggregator);
        var results = await service.PerformChecksAsync();

        results.Should().ContainSingle();
        results[0].Type.Should().Be(HealthCheckResultType.Ok);
        results[0].Source.Should().Be(nameof(NullReturningCheck));
    }

    [Test]
    public async Task PerformChecksAsync_WhenCheckThrowsUnhandledException_ReturnsErrorResultWithExceptionMessage()
    {
        var failingCheck = Substitute.For<IHealthCheck>();
        failingCheck.CheckAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("External service unreachable."));

        using var service = new HealthCheckService(new[] { failingCheck }, this.eventAggregator);
        var results = await service.PerformChecksAsync();

        results.Should().ContainSingle();
        results[0].Type.Should().Be(HealthCheckResultType.Error);
        results[0].Message.Should().Contain("Health check failed with exception: External service unreachable.");
    }

    [Test]
    public async Task PerformChecksAsync_WhenCheckTimesOut_ReturnsErrorResultWithTimeoutMessage()
    {
        var slowCheck = new SlowTestCheck();

        using var service = new HealthCheckService(
            new[] { slowCheck },
            this.eventAggregator,
            perCheckTimeout: TimeSpan.FromMilliseconds(50),
            periodicInterval: TimeSpan.FromMinutes(10));

        var results = await service.PerformChecksAsync();

        results.Should().ContainSingle();
        results[0].Type.Should().Be(HealthCheckResultType.Error);
        results[0].Source.Should().Be(nameof(SlowTestCheck));
        results[0].Message.Should().Contain("timed out after");
    }

    [Test]
    public async Task PerformChecksAsync_WhenCallerCancellationRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelingCheck = Substitute.For<IHealthCheck>();
        cancelingCheck.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<HealthCheckResult>(cts.Token));

        using var service = new HealthCheckService(new[] { cancelingCheck }, this.eventAggregator);

        Func<Task> action = async () => await service.PerformChecksAsync(cts.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task PerformChecksAsync_WhenDegradedCheckRunsFirstTime_PublishesHealthIssueEvent()
    {
        var degradedCheck = Substitute.For<IHealthCheck>();
        degradedCheck.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Warning("DiskSpace", "Disk space is low")));

        using var service = new HealthCheckService(new[] { degradedCheck }, this.eventAggregator);
        await service.PerformChecksAsync();

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "DiskSpace" &&
            e.Message == "Disk space is low" &&
            e.IsResolved == false));
    }

    [Test]
    public async Task PerformChecksAsync_WhenHealthyCheckRunsFirstTime_DoesNotPublishHealthIssueEvent()
    {
        var healthyCheck = Substitute.For<IHealthCheck>();
        healthyCheck.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Ok("NetworkBinding")));

        using var service = new HealthCheckService(new[] { healthyCheck }, this.eventAggregator);
        await service.PerformChecksAsync();

        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<HealthIssueEvent>());
    }

    [Test]
    public async Task PerformChecksAsync_WhenCheckTransitionsFromHealthyToDegraded_PublishesHealthIssueEvent()
    {
        var mutableCheck = Substitute.For<IHealthCheck>();
        mutableCheck.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(HealthCheckResult.Ok("VpnCheck")),
                Task.FromResult(HealthCheckResult.Error("VpnCheck", "VPN disconnected")));

        using var service = new HealthCheckService(new[] { mutableCheck }, this.eventAggregator);

        await service.PerformChecksAsync();
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<HealthIssueEvent>());

        await service.PerformChecksAsync();
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "VpnCheck" &&
            e.Message == "VPN disconnected" &&
            e.IsResolved == false));
    }

    [Test]
    public async Task PerformChecksAsync_WhenCheckTransitionsFromDegradedToHealthy_PublishesRestoredEvent()
    {
        var mutableCheck = Substitute.For<IHealthCheck>();
        mutableCheck.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(HealthCheckResult.Error("TorrentEngine", "Engine socket error")),
                Task.FromResult(HealthCheckResult.Ok("TorrentEngine")));

        using var service = new HealthCheckService(new[] { mutableCheck }, this.eventAggregator);

        await service.PerformChecksAsync();
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "TorrentEngine" &&
            e.IsResolved == false));

        this.eventAggregator.ClearReceivedCalls();

        await service.PerformChecksAsync();
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "TorrentEngine" &&
            e.Message == "TorrentEngine health restored" &&
            e.IsResolved == true));
    }

    [Test]
    public async Task PerformChecksAsync_WhenCheckRemainsDegraded_DoesNotPublishDuplicateEvent()
    {
        var degradedCheck = Substitute.For<IHealthCheck>();
        degradedCheck.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Warning("MemoryUsage", "Memory usage high")));

        using var service = new HealthCheckService(new[] { degradedCheck }, this.eventAggregator);

        await service.PerformChecksAsync();
        await service.PerformChecksAsync();
        await service.PerformChecksAsync();

        this.eventAggregator.Received(1).PublishEvent(Arg.Any<HealthIssueEvent>());
    }

    [Test]
    public async Task PerformChecksAsync_WhenEventAggregatorIsNull_DegradedCheckDoesNotThrow()
    {
        var degradedCheck = Substitute.For<IHealthCheck>();
        degradedCheck.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(HealthCheckResult.Error("Database", "Database connection lost")));

        using var service = new HealthCheckService(new[] { degradedCheck }, null!);

        var results = await service.PerformChecksAsync();

        results.Should().ContainSingle();
        results[0].Type.Should().Be(HealthCheckResultType.Error);
    }

    [Test]
    public async Task PerformChecksAsync_WhenCheckSourceIsEmpty_DefaultsSourceToSystem()
    {
        var emptySourceCheck = Substitute.For<IHealthCheck>();
        emptySourceCheck.CheckAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HealthCheckResult
            {
                Source = "   ",
                Type = HealthCheckResultType.Warning,
                Message = "Unspecified degraded condition",
            }));

        using var service = new HealthCheckService(new[] { emptySourceCheck }, this.eventAggregator);
        await service.PerformChecksAsync();

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "System" &&
            e.Message == "Unspecified degraded condition"));
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimesWithoutThrowing()
    {
        var service = new HealthCheckService(
            Array.Empty<IHealthCheck>(),
            this.eventAggregator,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30));

        var action = () =>
        {
            service.Dispose();
            service.Dispose();
        };

        action.Should().NotThrow();
    }

    private class NullReturningCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
        {
            return Task.FromResult<HealthCheckResult>(null!);
        }
    }

    private class SlowTestCheck : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return HealthCheckResult.Ok("SlowTestCheck");
        }
    }
}

[TestFixture]
public class HealthControllerTest
{
    private IHealthCheckService healthCheckService = null!;
    private HealthController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.healthCheckService = Substitute.For<IHealthCheckService>();
        this.controller = new HealthController(this.healthCheckService);
    }

    [Test]
    public async Task GetHealth_CallsPerformChecksAsync_AndReturnsOkWithResults()
    {
        var expectedResults = new List<HealthCheckResult>
        {
            HealthCheckResult.Ok("Indexer"),
            HealthCheckResult.Warning("DiskSpace", "Low disk space"),
            HealthCheckResult.Error("Vpn", "Killswitch activated"),
        };

        this.healthCheckService.PerformChecksAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResults));

        var actionResult = await this.controller.GetHealth(CancellationToken.None);

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var returnedResults = okResult.Value as List<HealthCheckResult>;

        returnedResults.Should().NotBeNull();
        returnedResults.Should().BeEquivalentTo(expectedResults);
    }

    [Test]
    public async Task GetHealth_PassesCancellationTokenToService()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        this.healthCheckService.PerformChecksAsync(token)
            .Returns(Task.FromResult(new List<HealthCheckResult>()));

        var actionResult = await this.controller.GetHealth(token);

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        await this.healthCheckService.Received(1).PerformChecksAsync(token);
    }

    [Test]
    public async Task GetHealth_WhenServiceReturnsEmptyList_ReturnsOkWithEmptyList()
    {
        this.healthCheckService.PerformChecksAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<HealthCheckResult>()));

        var actionResult = await this.controller.GetHealth();

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)actionResult.Result!;
        var returnedResults = okResult.Value as List<HealthCheckResult>;

        returnedResults.Should().NotBeNull();
        returnedResults.Should().BeEmpty();
    }

    [Test]
    public void HealthController_HasExpectedAttributes()
    {
        var type = typeof(HealthController);
        var apiAttribute = type.GetCustomAttribute<V1ApiControllerAttribute>();

        apiAttribute.Should().NotBeNull();
        apiAttribute!.Template.Should().Be("api/v1/health");

        var method = type.GetMethod(nameof(HealthController.GetHealth));
        method.Should().NotBeNull();

        var httpGetAttribute = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGetAttribute.Should().NotBeNull();
    }
}

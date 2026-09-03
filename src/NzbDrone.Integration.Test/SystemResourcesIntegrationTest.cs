// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Telemetry;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SystemResourcesIntegrationTest : IntegrationTestBase
{
    [Test]
    public async Task GetFullSystemResourcesSnapshot_ReturnsSuccessAndValidTelemetry()
    {
        using var response = await this.Client.GetAsync("/api/v1/system/resources");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await response.Content.ReadFromJsonAsync<SystemResourceTelemetrySnapshot>();

        snapshot.Should().NotBeNull();
        snapshot!.Host.Should().NotBeNull();
        snapshot.Host.CpuCores.Should().BeGreaterThan(0);
        snapshot.Host.WorkingSetBytes.Should().BeGreaterThan(0);

        snapshot.TorrentEngine.Should().NotBeNull();
        snapshot.TorrentEngine.EngineId.Should().Be("MonoTorrent");

        snapshot.Subsystems.Should().NotBeNull();
        snapshot.Subsystems.Should().HaveCount(9);
        snapshot.Subsystems.Should().Contain(s => s.SubsystemId == "bittorrent");
        snapshot.Subsystems.Should().Contain(s => s.SubsystemId == "mediainspector");
    }

    [Test]
    public async Task GetHostMetrics_ReturnsSuccessWithHardwareAndProcessDetails()
    {
        using var response = await this.Client.GetAsync("/api/v1/system/resources/host");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var host = await response.Content.ReadFromJsonAsync<HostProcessResourceMetrics>();

        host.Should().NotBeNull();
        host!.CpuCores.Should().BeGreaterThan(0);
        host.WorkingSetBytes.Should().BeGreaterThan(0);
        host.ThreadCount.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task GetEngineMetrics_ReturnsSuccessWithTorrentEngineDetails()
    {
        using var response = await this.Client.GetAsync("/api/v1/system/resources/engine");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var engine = await response.Content.ReadFromJsonAsync<TorrentEngineMetrics>();

        engine.Should().NotBeNull();
        engine!.EngineId.Should().Be("MonoTorrent");
        engine.DisplayName.Should().Contain("MonoTorrent");
        engine.DiskCacheCapacityBytes.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task GetSubsystemsTelemetry_ReturnsAllNineSubsystems()
    {
        using var response = await this.Client.GetAsync("/api/v1/system/resources/subsystems");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<SubsystemTelemetryReport>>();

        list.Should().NotBeNull();
        list.Should().HaveCount(9);
    }

    [Test]
    public async Task GetSubsystemsMetricsEndpoint_ReturnsSubsystemList()
    {
        using var response = await this.Client.GetAsync("/api/v1/subsystems/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<SubsystemTelemetryReport>>();

        list.Should().NotBeNull();
        list.Should().HaveCount(9);
    }

    [Test]
    public async Task GetSingleSubsystemMetricsEndpoint_WhenBittorrent_ReturnsTelemetry()
    {
        using var response = await this.Client.GetAsync("/api/v1/subsystems/bittorrent/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = await response.Content.ReadFromJsonAsync<SubsystemTelemetryReport>();

        report.Should().NotBeNull();
        report!.SubsystemId.Should().Be("bittorrent");
        report.SubsystemName.Should().Be("BitTorrent Engine");
        report.Metrics.Should().ContainKey("activeTorrents");
    }

    [Test]
    public async Task GetTorrentMetrics_WhenTorrentDoesNotExist_ReturnsNotFound()
    {
        using var response = await this.Client.GetAsync("/api/v1/system/resources/torrents/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

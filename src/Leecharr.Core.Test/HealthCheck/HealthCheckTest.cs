// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Network.Binding;

namespace Leecharr.Core.Test.HealthCheck;

[TestFixture]
public class HealthCheckTest
{
    private IArrConnectionRepository arrRepo;
    private IIndexerRepository indexerRepo;

    [SetUp]
    public void SetUp()
    {
        this.arrRepo = Substitute.For<IArrConnectionRepository>();
        this.indexerRepo = Substitute.For<IIndexerRepository>();
    }

    [Test]
    public async Task NoArrConnectionsCheck_ReturnsNotice_WhenNoConnectionsConfigured()
    {
        this.arrRepo.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var check = new NoArrConnectionsCheck(this.arrRepo);
        var result = await check.CheckAsync();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("NoArrConnections"));
        Assert.That(result.Message, Does.Contain("No *arr connections configured"));
    }

    [Test]
    public async Task NoArrConnectionsCheck_ReturnsOk_WhenConnectionsExist()
    {
        this.arrRepo.GetEnabled().Returns(new List<ArrConnectionDefinition>
        {
            new ArrConnectionDefinition { Id = 1, Name = "Sonarr", Enable = true },
        });

        var check = new NoArrConnectionsCheck(this.arrRepo);
        var result = await check.CheckAsync();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public async Task NoIndexersCheck_ReturnsNotice_WhenNoIndexersConfigured()
    {
        this.indexerRepo.GetEnabled().Returns(new List<IndexerDefinition>());

        var check = new NoIndexersCheck(this.indexerRepo);
        var result = await check.CheckAsync();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("NoIndexers"));
        Assert.That(result.Message, Does.Contain("No indexers configured"));
    }

    [Test]
    public async Task NoIndexersCheck_ReturnsOk_WhenIndexersExist()
    {
        this.indexerRepo.GetEnabled().Returns(new List<IndexerDefinition>
        {
            new IndexerDefinition { Id = 1, Name = "Prowlarr", Enable = true },
        });

        var check = new NoIndexersCheck(this.indexerRepo);
        var result = await check.CheckAsync();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public async Task EngineHealthCheck_ReturnsOk_WhenEngineIsHealthy()
    {
        var engine = Substitute.For<IDownloadEngine>();
        engine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new EngineHealthCheck(engine);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("EngineHealth");
    }

    [Test]
    public async Task EngineHealthCheck_ReturnsError_WhenEngineIsUnhealthy()
    {
        var engine = Substitute.For<IDownloadEngine>();
        engine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = false, StatusMessage = "Engine failed" }));

        var check = new EngineHealthCheck(engine);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Error);
        result.Message.Should().Contain("Engine failed");
    }

    [Test]
    public async Task NetworkBindingHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<INetworkBindingManager>();
        manager.ActiveProviderId.Returns("ManagedSocket");
        manager.ProbeProviderAsync("ManagedSocket").Returns(Task.FromResult(new NetworkBindingHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new NetworkBindingHealthCheck(manager);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("NetworkBindingHealth");
    }

    [Test]
    public async Task HttpTransportHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<IHttpTransportManager>();
        manager.ActiveProviderId.Returns("SocketsHttpHandler");
        manager.ProbeProviderAsync("SocketsHttpHandler").Returns(Task.FromResult(new HttpTransportHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new HttpTransportHealthCheck(manager);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("HttpTransportHealth");
    }

    [Test]
    public async Task ExtractorHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<IArchiveExtractorManager>();
        manager.ActiveProviderId.Returns("SharpCompress");
        manager.ProbeProviderAsync("SharpCompress", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ExtractorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new ExtractorHealthCheck(manager);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("ExtractorHealth");
    }

    [Test]
    public async Task MediaMetadataHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<IMediaMetadataManager>();
        manager.ActiveProviderId.Returns("ServarrSync");
        manager.ProbeProviderAsync("ServarrSync").Returns(Task.FromResult(new MediaMetadataHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new MediaMetadataHealthCheck(manager);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("MediaMetadataHealth");
    }

    [Test]
    public async Task MediaInspectorHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<IMediaInspectorManager>();
        manager.ActiveProviderId.Returns("TagLib");
        manager.ProbeProviderAsync("TagLib", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new MediaInspectorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new MediaInspectorHealthCheck(manager);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("MediaInspectorHealth");
    }

    [Test]
    public async Task DiskSpaceHealthCheck_ReturnsOk_WhenAdequateDiskSpace()
    {
        var diskService = Substitute.For<IDiskSpaceService>();
        diskService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo { Path = "/downloads", FreeSpace = 50L * 1024 * 1024 * 1024, TotalSpace = 100L * 1024 * 1024 * 1024 },
        });

        var check = new DiskSpaceHealthCheck(diskService);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("DiskSpace");
    }

    [Test]
    public async Task DiskSpaceHealthCheck_ReturnsWarning_WhenDiskSpaceLessThan5Gb()
    {
        var diskService = Substitute.For<IDiskSpaceService>();
        diskService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo { Path = "/downloads", FreeSpace = 3L * 1024 * 1024 * 1024, TotalSpace = 100L * 1024 * 1024 * 1024 },
        });

        var check = new DiskSpaceHealthCheck(diskService);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Warning);
        result.Source.Should().Be("DiskSpace");
        result.Message.Should().Contain("Low disk space");
    }

    [Test]
    public async Task DiskSpaceHealthCheck_ReturnsError_WhenDiskSpaceLessThan1Gb()
    {
        var diskService = Substitute.For<IDiskSpaceService>();
        diskService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new DiskSpaceInfo { Path = "/downloads", FreeSpace = 500L * 1024 * 1024, TotalSpace = 100L * 1024 * 1024 * 1024 },
        });

        var check = new DiskSpaceHealthCheck(diskService);
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Error);
        result.Source.Should().Be("DiskSpace");
        result.Message.Should().Contain("Critically low disk space");
    }

    [Test]
    public async Task MemoryHealthCheck_ReturnsOk_WhenMemoryNormal()
    {
        var check = new MemoryHealthCheck(() => (16L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024, 200L * 1024 * 1024));
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("Memory");
    }

    [Test]
    public async Task MemoryHealthCheck_ReturnsWarning_WhenMemoryExceeds90Percent()
    {
        var check = new MemoryHealthCheck(() => (10L * 1024 * 1024 * 1024, 92L * 1024 * 1024 * 102, 500L * 1024 * 1024));
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Warning);
        result.Source.Should().Be("Memory");
        result.Message.Should().Contain("High memory usage");
    }

    [Test]
    public async Task MemoryHealthCheck_ReturnsError_WhenMemoryCritical()
    {
        var check = new MemoryHealthCheck(() => (10L * 1024 * 1024 * 1024, 98L * 1024 * 1024 * 102, 900L * 1024 * 1024));
        var result = await check.CheckAsync();

        result.Type.Should().Be(HealthCheckResultType.Error);
        result.Source.Should().Be("Memory");
        result.Message.Should().Contain("Critical memory exhaustion");
    }

    [Test]
    public async Task HealthCheckService_ExecutesAllChecks()
    {
        var mockCheck1 = Substitute.For<IHealthCheck>();
        mockCheck1.CheckAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(HealthCheckResult.Ok("Check1")));

        var mockCheck2 = Substitute.For<IHealthCheck>();
        mockCheck2.CheckAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(HealthCheckResult.Warning("Check2", "Warning message")));

        var service = new HealthCheckService(new[] { mockCheck1, mockCheck2 });
        var results = await service.PerformChecksAsync();

        Assert.That(results.Count, Is.EqualTo(2));
        Assert.That(results[0].Source, Is.EqualTo("Check1"));
        Assert.That(results[1].Source, Is.EqualTo("Check2"));
        Assert.That(results[1].Type, Is.EqualTo(HealthCheckResultType.Warning));
    }
}

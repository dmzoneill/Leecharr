// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.BitTorrent;
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
    public void NoArrConnectionsCheck_ReturnsNotice_WhenNoConnectionsConfigured()
    {
        this.arrRepo.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var check = new NoArrConnectionsCheck(this.arrRepo);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("NoArrConnections"));
        Assert.That(result.Message, Does.Contain("No *arr connections configured"));
    }

    [Test]
    public void NoArrConnectionsCheck_ReturnsOk_WhenConnectionsExist()
    {
        this.arrRepo.GetEnabled().Returns(new List<ArrConnectionDefinition>
        {
            new ArrConnectionDefinition { Id = 1, Name = "Sonarr", Enable = true },
        });

        var check = new NoArrConnectionsCheck(this.arrRepo);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void NoIndexersCheck_ReturnsNotice_WhenNoIndexersConfigured()
    {
        this.indexerRepo.GetEnabled().Returns(new List<IndexerDefinition>());

        var check = new NoIndexersCheck(this.indexerRepo);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("NoIndexers"));
        Assert.That(result.Message, Does.Contain("No indexers configured"));
    }

    [Test]
    public void NoIndexersCheck_ReturnsOk_WhenIndexersExist()
    {
        this.indexerRepo.GetEnabled().Returns(new List<IndexerDefinition>
        {
            new IndexerDefinition { Id = 1, Name = "Prowlarr", Enable = true },
        });

        var check = new NoIndexersCheck(this.indexerRepo);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void EngineHealthCheck_ReturnsOk_WhenEngineIsHealthy()
    {
        var engine = Substitute.For<IDownloadEngine>();
        engine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new EngineHealthCheck(engine);
        var result = check.Check();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("EngineHealth");
    }

    [Test]
    public void EngineHealthCheck_ReturnsError_WhenEngineIsUnhealthy()
    {
        var engine = Substitute.For<IDownloadEngine>();
        engine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = false, StatusMessage = "Engine failed" }));

        var check = new EngineHealthCheck(engine);
        var result = check.Check();

        result.Type.Should().Be(HealthCheckResultType.Error);
        result.Message.Should().Contain("Engine failed");
    }

    [Test]
    public void NetworkBindingHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<INetworkBindingManager>();
        manager.ActiveProviderId.Returns("ManagedSocket");
        manager.ProbeProviderAsync("ManagedSocket").Returns(Task.FromResult(new NetworkBindingHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new NetworkBindingHealthCheck(manager);
        var result = check.Check();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("NetworkBindingHealth");
    }

    [Test]
    public void HttpTransportHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<IHttpTransportManager>();
        manager.ActiveProviderId.Returns("SocketsHttpHandler");
        manager.ProbeProviderAsync("SocketsHttpHandler").Returns(Task.FromResult(new HttpTransportHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new HttpTransportHealthCheck(manager);
        var result = check.Check();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("HttpTransportHealth");
    }

    [Test]
    public void ExtractorHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<IArchiveExtractorManager>();
        manager.ActiveProviderId.Returns("SharpCompress");
        manager.ProbeProviderAsync("SharpCompress", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ExtractorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new ExtractorHealthCheck(manager);
        var result = check.Check();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("ExtractorHealth");
    }

    [Test]
    public void MediaMetadataHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<IMediaMetadataManager>();
        manager.ActiveProviderId.Returns("ServarrSync");
        manager.ProbeProviderAsync("ServarrSync").Returns(Task.FromResult(new MediaMetadataHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new MediaMetadataHealthCheck(manager);
        var result = check.Check();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("MediaMetadataHealth");
    }

    [Test]
    public void MediaInspectorHealthCheck_ReturnsOk_WhenHealthy()
    {
        var manager = Substitute.For<IMediaInspectorManager>();
        manager.ActiveProviderId.Returns("TagLib");
        manager.ProbeProviderAsync("TagLib", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new MediaInspectorHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        var check = new MediaInspectorHealthCheck(manager);
        var result = check.Check();

        result.Type.Should().Be(HealthCheckResultType.Ok);
        result.Source.Should().Be("MediaInspectorHealth");
    }

    [Test]
    public void HealthCheckService_ExecutesAllChecks()
    {
        var mockCheck1 = Substitute.For<IHealthCheck>();
        mockCheck1.Check().Returns(HealthCheckResult.Ok("Check1"));

        var mockCheck2 = Substitute.For<IHealthCheck>();
        mockCheck2.Check().Returns(HealthCheckResult.Warning("Check2", "Warning message"));

        var service = new HealthCheckService(new[] { mockCheck1, mockCheck2 });
        var results = service.PerformChecks();

        Assert.That(results.Count, Is.EqualTo(2));
        Assert.That(results[0].Source, Is.EqualTo("Check1"));
        Assert.That(results[1].Source, Is.EqualTo("Check2"));
        Assert.That(results[1].Type, Is.EqualTo(HealthCheckResultType.Warning));
    }
}

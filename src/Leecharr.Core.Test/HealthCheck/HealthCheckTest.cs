using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Indexers;

namespace Leecharr.Core.Test.HealthCheck;

[TestFixture]
public class HealthCheckTest
{
    private IArrConnectionRepository _arrRepo;
    private IIndexerRepository _indexerRepo;

    [SetUp]
    public void SetUp()
    {
        _arrRepo = Substitute.For<IArrConnectionRepository>();
        _indexerRepo = Substitute.For<IIndexerRepository>();
    }

    [Test]
    public void NoArrConnectionsCheck_ReturnsWarning_WhenNoConnectionsConfigured()
    {
        _arrRepo.GetEnabled().Returns(new List<ArrConnectionDefinition>());

        var check = new NoArrConnectionsCheck(_arrRepo);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Source, Is.EqualTo("NoArrConnections"));
        Assert.That(result.Message, Does.Contain("No *arr connections configured"));
    }

    [Test]
    public void NoArrConnectionsCheck_ReturnsOk_WhenConnectionsExist()
    {
        _arrRepo.GetEnabled().Returns(new List<ArrConnectionDefinition>
        {
            new ArrConnectionDefinition { Id = 1, Name = "Sonarr", Enable = true }
        });

        var check = new NoArrConnectionsCheck(_arrRepo);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void NoIndexersCheck_ReturnsNotice_WhenNoIndexersConfigured()
    {
        _indexerRepo.GetEnabled().Returns(new List<IndexerDefinition>());

        var check = new NoIndexersCheck(_indexerRepo);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("NoIndexers"));
        Assert.That(result.Message, Does.Contain("No indexers configured"));
    }

    [Test]
    public void NoIndexersCheck_ReturnsOk_WhenIndexersExist()
    {
        _indexerRepo.GetEnabled().Returns(new List<IndexerDefinition>
        {
            new IndexerDefinition { Id = 1, Name = "Prowlarr", Enable = true }
        });

        var check = new NoIndexersCheck(_indexerRepo);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
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

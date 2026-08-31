using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class ProwlarrSyncServiceTest
{
    private IIndexerRepository _repository = null!;
    private ProwlarrSyncService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IIndexerRepository>();
        _service = new ProwlarrSyncService(_repository);
    }

    [Test]
    public async Task SyncFromProwlarrAsync_WhenUrlOrKeyEmpty_ReturnsZero()
    {
        var count = await _service.SyncFromProwlarrAsync("", "");
        count.Should().Be(0);
    }
}

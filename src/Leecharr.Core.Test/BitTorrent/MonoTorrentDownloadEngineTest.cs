using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class MonoTorrentDownloadEngineTest
{
    private IConfigService _configService = null!;
    private IStoragePathService _storagePathService = null!;
    private ICategoryService _categoryService = null!;
    private IDiskProvider _diskProvider = null!;
    private IEventAggregator _eventAggregator = null!;
    private MonoTorrentDownloadEngine _engine = null!;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.ListeningPort.Returns(0); // dynamic port
        _configService.UpnpEnabled.Returns(false);
        _configService.DiskWriteCacheSizeMb.Returns(128);

        _storagePathService = Substitute.For<IStoragePathService>();
        _storagePathService.GetIncompleteDirectory().Returns("/tmp/leecharr-test-incomplete");

        _categoryService = Substitute.For<ICategoryService>();
        _diskProvider = Substitute.For<IDiskProvider>();
        _eventAggregator = Substitute.For<IEventAggregator>();

        _engine = new MonoTorrentDownloadEngine(
            _configService,
            _storagePathService,
            _categoryService,
            _diskProvider,
            _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _engine?.Dispose();
    }

    [Test]
    public void ProtocolName_ReturnsBitTorrent()
    {
        _engine.ProtocolName.Should().Be("BitTorrent");
    }

    [Test]
    public async Task StartAndStop_ExecutesCleanly()
    {
        await _engine.StartAsync();
        await _engine.StopAsync();
    }

    [Test]
    public void GetTask_WhenNotFound_ReturnsNull()
    {
        var task = _engine.GetTask(9999);
        task.Should().BeNull();
    }

    [Test]
    public void GetAllTasks_WhenEmpty_ReturnsEmptyCollection()
    {
        var tasks = _engine.GetAllTasks();
        tasks.Should().BeEmpty();
    }

    [Test]
    public async Task PauseAndResume_NonExistentTorrent_DoesNotThrow()
    {
        var pauseAct = async () => await _engine.PauseTorrentAsync(9999);
        await pauseAct.Should().NotThrowAsync();

        var resumeAct = async () => await _engine.ResumeTorrentAsync(9999);
        await resumeAct.Should().NotThrowAsync();
    }

    [Test]
    public async Task ForceRecheckAndAnnounce_NonExistentTorrent_DoesNotThrow()
    {
        var recheckAct = async () => await _engine.ForceRecheckAsync(9999);
        await recheckAct.Should().NotThrowAsync();

        var announceAct = async () => await _engine.ForceAnnounceAsync(9999);
        await announceAct.Should().NotThrowAsync();
    }

    [Test]
    public async Task RemoveTorrent_NonExistent_DoesNotThrow()
    {
        var removeAct = async () => await _engine.RemoveTorrentAsync(9999, deleteFiles: false);
        await removeAct.Should().NotThrowAsync();
    }

    [Test]
    public async Task RemoveTorrent_WithDeleteFiles_NonExistent_DoesNotThrow()
    {
        var removeAct = async () => await _engine.RemoveTorrentAsync(9999, deleteFiles: true);
        await removeAct.Should().NotThrowAsync();
    }
}

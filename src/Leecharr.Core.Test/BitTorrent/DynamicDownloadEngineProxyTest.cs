using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class DynamicDownloadEngineProxyTest
{
    private ITorrentEngine _monoTorrentEngine = null!;
    private ITorrentEngine _libTorrentEngine = null!;
    private ITorrentEngine _transmissionEngine = null!;
    private IConfigService _configService = null!;
    private ITorrentRepository _torrentRepository = null!;
    private IEventAggregator _eventAggregator = null!;
    private DynamicDownloadEngineProxy _proxy = null!;

    [SetUp]
    public void SetUp()
    {
        _monoTorrentEngine = Substitute.For<ITorrentEngine>();
        _monoTorrentEngine.EngineId.Returns("MonoTorrent");
        _monoTorrentEngine.DisplayName.Returns("MonoTorrent (Pure .NET)");
        _monoTorrentEngine.ProtocolName.Returns("BitTorrent");
        _monoTorrentEngine.IsAvailable.Returns(true);
        _monoTorrentEngine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        _libTorrentEngine = Substitute.For<ITorrentEngine>();
        _libTorrentEngine.EngineId.Returns("LibTorrent");
        _libTorrentEngine.DisplayName.Returns("libtorrent (Rasterbar C++)");
        _libTorrentEngine.ProtocolName.Returns("BitTorrent");
        _libTorrentEngine.IsAvailable.Returns(true);
        _libTorrentEngine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        _transmissionEngine = Substitute.For<ITorrentEngine>();
        _transmissionEngine.EngineId.Returns("Transmission");
        _transmissionEngine.DisplayName.Returns("Transmission Daemon (Sidecar)");
        _transmissionEngine.ProtocolName.Returns("BitTorrent");
        _transmissionEngine.IsAvailable.Returns(true);
        _transmissionEngine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult { IsHealthy = true, StatusMessage = "OK" }));

        _configService = Substitute.For<IConfigService>();
        _configService.ActiveTorrentEngine.Returns("MonoTorrent");

        _torrentRepository = Substitute.For<ITorrentRepository>();
        _torrentRepository.All().Returns(new List<Torrent>
        {
            new() { Id = 1, Name = "Ubuntu ISO", InfoHash = "0123456789ABCDEF0123456789ABCDEF01234567", Status = TorrentStatus.Downloading },
            new() { Id = 2, Name = "Debian ISO", InfoHash = "FEDCBA9876543210FEDCBA9876543210FEDCBA98", Status = TorrentStatus.Paused }
        });

        _eventAggregator = Substitute.For<IEventAggregator>();

        var engines = new List<ITorrentEngine> { _monoTorrentEngine, _libTorrentEngine, _transmissionEngine };

        _proxy = new DynamicDownloadEngineProxy(
            engines,
            _configService,
            _torrentRepository,
            _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _proxy?.Dispose();
    }

    [Test]
    public void Constructor_InitializesWithConfiguredEngine()
    {
        _proxy.ActiveEngineId.Should().Be("MonoTorrent");
        _proxy.ActiveEngine.Should().BeSameAs(_monoTorrentEngine);
        _proxy.ProtocolName.Should().Be("BitTorrent");
    }

    [Test]
    public void GetEngines_ReturnsAllRegisteredEngines()
    {
        var engines = _proxy.GetEngines().ToList();
        engines.Should().HaveCount(3);
        engines.Select(e => e.EngineId).Should().Contain(new[] { "MonoTorrent", "LibTorrent", "Transmission" });
    }

    [Test]
    public void GetEngine_WithValidId_ReturnsMatchingEngine()
    {
        var engine = _proxy.GetEngine("libtorrent");
        engine.Should().NotBeNull();
        engine!.EngineId.Should().Be("LibTorrent");
    }

    [Test]
    public void GetEngine_WithInvalidId_ReturnsNull()
    {
        var engine = _proxy.GetEngine("NonExistentEngine");
        engine.Should().BeNull();
    }

    [Test]
    public async Task ProbeEngineAsync_WithValidEngine_ReturnsHealthResult()
    {
        var probe = await _proxy.ProbeEngineAsync("LibTorrent");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeTrue();
        probe.StatusMessage.Should().Be("OK");
    }

    [Test]
    public async Task ProbeEngineAsync_WithInvalidEngine_ReturnsUnhealthy()
    {
        var probe = await _proxy.ProbeEngineAsync("InvalidEngine");
        probe.Should().NotBeNull();
        probe.IsHealthy.Should().BeFalse();
        probe.StatusMessage.Should().Contain("not recognized");
    }

    [Test]
    public async Task SwitchEngineAsync_SwitchesActiveEngineAndMigratesTorrents()
    {
        var result = await _proxy.SwitchEngineAsync("LibTorrent", preserveTransfers: true);

        result.Success.Should().BeTrue();
        result.PreviousEngine.Should().Be("MonoTorrent");
        result.ActiveEngine.Should().Be("LibTorrent");
        result.TorrentsMigrated.Should().Be(2);

        _proxy.ActiveEngineId.Should().Be("LibTorrent");
        _proxy.ActiveEngine.Should().BeSameAs(_libTorrentEngine);

        await _monoTorrentEngine.Received(1).StopAsync();
        await _libTorrentEngine.Received(1).StartAsync();
        await _libTorrentEngine.Received(2).AddTorrentAsync(Arg.Any<Torrent>(), null, Arg.Any<string>());
        await _libTorrentEngine.Received(1).PauseTorrentAsync(2);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (string)d["ActiveTorrentEngine"] == "LibTorrent"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentEngineSwitchedEvent>(e => e.PreviousEngine == "MonoTorrent" && e.NewEngine == "LibTorrent" && e.TorrentsMigrated == 2));
    }

    [Test]
    public async Task SwitchEngineAsync_WhenTargetAlreadyActive_ReturnsSuccessWithoutWork()
    {
        var result = await _proxy.SwitchEngineAsync("MonoTorrent");

        result.Success.Should().BeTrue();
        result.ActiveEngine.Should().Be("MonoTorrent");
        result.TorrentsMigrated.Should().Be(0);

        await _monoTorrentEngine.DidNotReceive().StopAsync();
    }

    [Test]
    public async Task SwitchEngineAsync_WithUnknownEngine_ReturnsFailure()
    {
        var result = await _proxy.SwitchEngineAsync("UnknownEngine");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not registered");
        _proxy.ActiveEngineId.Should().Be("MonoTorrent");
    }

    [Test]
    public async Task SwitchEngineAsync_WhenTargetUnhealthy_AbortsSwitch()
    {
        _libTorrentEngine.ProbeHealthAsync().Returns(Task.FromResult(new EngineHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "Missing native shared library"
        }));

        var result = await _proxy.SwitchEngineAsync("LibTorrent");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("health check failed");
        _proxy.ActiveEngineId.Should().Be("MonoTorrent");
        await _monoTorrentEngine.DidNotReceive().StopAsync();
    }

    [Test]
    public async Task Delegation_ForwardsCallsToActiveEngine()
    {
        await _proxy.StartAsync();
        await _monoTorrentEngine.Received(1).StartAsync();

        await _proxy.StopAsync();
        await _monoTorrentEngine.Received(1).StopAsync();

        var torrent = new Torrent { Id = 10, InfoHash = "HASH" };
        await _proxy.AddTorrentAsync(torrent, null, "magnet:?");
        await _monoTorrentEngine.Received(1).AddTorrentAsync(torrent, null, "magnet:?");

        await _proxy.PauseTorrentAsync(10);
        await _monoTorrentEngine.Received(1).PauseTorrentAsync(10);

        await _proxy.ResumeTorrentAsync(10);
        await _monoTorrentEngine.Received(1).ResumeTorrentAsync(10);

        await _proxy.ForceRecheckAsync(10);
        await _monoTorrentEngine.Received(1).ForceRecheckAsync(10);

        await _proxy.ForceAnnounceAsync(10);
        await _monoTorrentEngine.Received(1).ForceAnnounceAsync(10);

        await _proxy.RemoveTorrentAsync(10, true);
        await _monoTorrentEngine.Received(1).RemoveTorrentAsync(10, true);
    }
}

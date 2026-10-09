using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentProgressMilestoneServiceTest
{
    private IEventAggregator _eventAggregator;
    private TorrentProgressMilestoneService _service;

    [SetUp]
    public void SetUp()
    {
        _eventAggregator = Substitute.For<IEventAggregator>();
        _service = new TorrentProgressMilestoneService(_eventAggregator);
    }

    [Test]
    public void CheckProgressMilestones_WhenCrossing25Percent_PublishesOnce()
    {
        var torrent = new Torrent { Id = 7, Name = "Test" };

        _service.CheckProgressMilestones(torrent, 0.10, 0.30);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentProgressMilestoneEvent>(e =>
            e.Torrent == torrent && e.MilestonePercent == 25));
    }

    [Test]
    public void CheckProgressMilestones_WhenJumpingPastMultipleMilestones_PublishesEachOnce()
    {
        var torrent = new Torrent { Id = 8, Name = "Test" };

        _service.CheckProgressMilestones(torrent, 0.0, 0.76);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentProgressMilestoneEvent>(e => e.MilestonePercent == 25));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentProgressMilestoneEvent>(e => e.MilestonePercent == 50));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentProgressMilestoneEvent>(e => e.MilestonePercent == 75));
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Is<TorrentProgressMilestoneEvent>(e => e.MilestonePercent == 100));
    }

    [Test]
    public void CheckProgressMilestones_WhenAlreadyReported_DoesNotRepublishSameMilestone()
    {
        var torrent = new Torrent { Id = 9, Name = "Test" };

        _service.CheckProgressMilestones(torrent, 0.0, 0.30);
        _service.CheckProgressMilestones(torrent, 0.30, 0.40);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentProgressMilestoneEvent>(e => e.MilestonePercent == 25));
    }

    [Test]
    public void CheckProgressMilestones_At100Percent_Publishes100Milestone()
    {
        var torrent = new Torrent { Id = 10, Name = "Test" };

        _service.CheckProgressMilestones(torrent, 0.95, 1.0);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentProgressMilestoneEvent>(e => e.MilestonePercent == 100));
    }

    [Test]
    public void ClearTorrent_AllowsMilestonesToPublishAgainAfterReAdd()
    {
        var torrent = new Torrent { Id = 11, Name = "Test" };

        _service.CheckProgressMilestones(torrent, 0.0, 0.30);
        _service.ClearTorrent(torrent.Id);
        _service.CheckProgressMilestones(torrent, 0.0, 0.30);

        _eventAggregator.Received(2).PublishEvent(Arg.Is<TorrentProgressMilestoneEvent>(e => e.MilestonePercent == 25));
    }
}

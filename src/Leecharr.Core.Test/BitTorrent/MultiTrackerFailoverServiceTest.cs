using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class MultiTrackerFailoverServiceTest
{
    [Test]
    public void ShouldSkipAnnounce_WhenTrackerIsInBackoff()
    {
        var config = Substitute.For<IConfigService>();
        config.MultiTrackerEnabled.Returns(true);
        config.MultiTrackerFailoverEnabled.Returns(true);
        config.FailoverMaxConsecutiveFailures.Returns(1);
        config.FailoverBackoffBaseSeconds.Returns(120);
        config.FailoverMaxBackoffSeconds.Returns(1800);

        var utc = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var service = new MultiTrackerFailoverService(config, () => utc);
        var tracker = Substitute.For<MonoTorrent.Trackers.ITracker>();
        tracker.Uri.Returns(new Uri("http://tracker.example/announce"));

        service.RecordAnnounceOutcome(42, 0, tracker, false, utc);

        service.ShouldSkipAnnounce(42, 0, tracker.Uri, utc.AddSeconds(30)).Should().BeTrue();
        service.ShouldSkipAnnounce(42, 0, tracker.Uri, utc.AddSeconds(130)).Should().BeFalse();
    }

    [Test]
    public void RecordAnnounceOutcome_RotatesTierTrackerAfterMaxFailures()
    {
        var config = Substitute.For<IConfigService>();
        config.MultiTrackerEnabled.Returns(true);
        config.MultiTrackerFailoverEnabled.Returns(true);
        config.FailoverMaxConsecutiveFailures.Returns(2);
        config.FailoverBackoffBaseSeconds.Returns(10);
        config.FailoverMaxBackoffSeconds.Returns(100);

        var utc = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var service = new MultiTrackerFailoverService(config, () => utc);

        var trackerA = Substitute.For<MonoTorrent.Trackers.ITracker>();
        var trackerB = Substitute.For<MonoTorrent.Trackers.ITracker>();
        trackerA.Uri.Returns(new Uri("http://a.example/announce"));
        trackerB.Uri.Returns(new Uri("http://b.example/announce"));
        var trackers = new[] { trackerA, trackerB };

        service.RecordAnnounceOutcome(7, 0, trackerA, false, utc);
        service.RecordAnnounceOutcome(7, 0, trackerA, false, utc.AddSeconds(20));

        var selected = service.SelectTrackerForTier(7, 0, trackers, trackerA, utc.AddSeconds(20));
        selected.Uri.Should().Be(trackerB.Uri);
    }
}

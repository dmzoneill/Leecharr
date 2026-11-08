// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Metrics;

namespace Leecharr.Core.Test.Trackers;

[TestFixture]
public class TrackerMetricServiceDisposeTest
{
    private TrackerMetricService subject;

    [SetUp]
    public void SetUp()
    {
        var metricRepository = Substitute.For<ITrackerMetricRepository>();
        var snapshotRepository = Substitute.For<ITrackerMetricSnapshotRepository>();
        var trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        var torrentRepository = Substitute.For<ITorrentRepository>();
        var eventAggregator = Substitute.For<IEventAggregator>();

        trackerEntryRepository.All().Returns(new List<TrackerEntry>());
        torrentRepository.All().Returns(new List<Torrent>());

        this.subject = new TrackerMetricService(
            metricRepository,
            snapshotRepository,
            trackerEntryRepository,
            torrentRepository,
            eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        this.subject?.Dispose();
    }

    [Test]
    public async Task DisposeAsync_releases_flush_signal()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var flushField = typeof(TrackerMetricService).GetField("flushSignalCts", flags);
        Assert.That(flushField.GetValue(this.subject), Is.Not.Null);

        var disposeBool = typeof(TrackerMetricService).GetMethod(
            "Dispose",
            flags,
            null,
            new[] { typeof(bool) },
            null);
        var original = flushField.GetValue(this.subject);
        disposeBool.Invoke(this.subject, new object[] { false });
        Assert.That(flushField.GetValue(this.subject), Is.SameAs(original));

        await this.subject.DisposeAsync();
        Assert.That(flushField.GetValue(this.subject), Is.Null);
        this.subject = null;
    }
}

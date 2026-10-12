// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using Leecharr.Api.V1.DownloadClients;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;

namespace Leecharr.Core.Test.DownloadClients;

[TestFixture]
public class UtWebUiAvailabilityHelperTest
{
    [Test]
    public void EncodeAvailabilityUnits_WithNullTask_ReturnsZero()
    {
        UtWebUiAvailabilityHelper.EncodeAvailabilityUnits(null).Should().Be(0);
    }

    [Test]
    public void EncodeAvailabilityUnits_WithZeroSwarm_ReturnsZero()
    {
        var task = Substitute.For<IDownloadTask>();
        task.PieceAvailability.Returns(new[] { 0, 0, 0 });

        UtWebUiAvailabilityHelper.EncodeAvailabilityUnits(task).Should().Be(0);
    }

    [Test]
    public void EncodeAvailabilityUnits_WithOneFullCopy_Returns65536()
    {
        var task = Substitute.For<IDownloadTask>();
        task.GetResourceMetrics().Returns(new TorrentResourceMetrics { SwarmAvailability = 1.0 });

        UtWebUiAvailabilityHelper.EncodeAvailabilityUnits(task).Should().Be(UtWebUiAvailabilityHelper.UnitsPerFullCopy);
    }

    [Test]
    public void EncodeAvailabilityUnits_WithPieceAvailability_ComputesAverageInWebUiUnits()
    {
        var task = Substitute.For<IDownloadTask>();
        task.PieceAvailability.Returns(new[] { 2, 2, 4, 4 });

        UtWebUiAvailabilityHelper.EncodeAvailabilityUnits(task).Should().Be(3 * UtWebUiAvailabilityHelper.UnitsPerFullCopy);
    }

    [Test]
    public void EncodeAvailabilityUnits_WithFractionalCopy_RoundsToNearestUnit()
    {
        var task = Substitute.For<IDownloadTask>();
        task.GetResourceMetrics().Returns(new TorrentResourceMetrics { SwarmAvailability = 0.5 });

        UtWebUiAvailabilityHelper.EncodeAvailabilityUnits(task).Should().Be(32768);
    }
}

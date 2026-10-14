// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Bandwidth.Distribution;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Bandwidth;

[TestFixture]
public class BandwidthDistributionServiceTest
{
    [Test]
    public async Task ApplyPerTorrentLimitsAsync_AppliesDistributedUploadLimitsToSeedingTorrents()
    {
        var downloadEngine = Substitute.For<IDownloadEngine>();
        var torrentRepository = Substitute.For<ITorrentRepository>();
        var categoryService = Substitute.For<ICategoryService>();
        var distributionManager = Substitute.For<ISpeedDistributionManager>();
        var stopSelection = Substitute.For<IBandwidthStopSelection>();
        var configService = Substitute.For<IConfigService>();

        configService.SpeedVariationMin.Returns(1.0);
        configService.SpeedVariationMax.Returns(1.0);

        var torrents = new List<Torrent>
        {
            new() { Id = 1, Status = TorrentStatus.Seeding, Priority = 1 },
            new() { Id = 2, Status = TorrentStatus.Seeding, Priority = 1 },
        };

        torrentRepository.All().Returns(torrents);

        var task1 = Substitute.For<IDownloadTask>();
        task1.TorrentId.Returns(1);
        task1.Status.Returns(TorrentStatus.Seeding);

        var task2 = Substitute.For<IDownloadTask>();
        task2.TorrentId.Returns(2);
        task2.Status.Returns(TorrentStatus.Seeding);

        downloadEngine.GetAllTasks().Returns(new[] { task1, task2 });

        stopSelection.SelectUploadStoppedIndices(Arg.Any<IReadOnlyList<Torrent>>())
            .Returns(new HashSet<int>());

        distributionManager.DistributeUploadSpeeds(2, 2048L * 1024, Arg.Any<double[]>(), Arg.Any<long[]>())
            .Returns(new long[] { 1024L * 1024, 1024L * 1024 });

        var service = new BandwidthDistributionService(
            downloadEngine,
            torrentRepository,
            categoryService,
            distributionManager,
            stopSelection,
            configService,
            () => 0.5);

        await service.ApplyPerTorrentLimitsAsync(new EffectiveSpeedLimits
        {
            MaxUploadSpeedKbps = 2048,
            MaxDownloadSpeedKbps = 0,
        });

        await downloadEngine.Received(1).SetTorrentRateLimitsAsync(1, 0, 1024);
        await downloadEngine.Received(1).SetTorrentRateLimitsAsync(2, 0, 1024);
        distributionManager.Received(1).DistributeUploadSpeeds(
            2,
            2048L * 1024,
            Arg.Is<double[]>(w => w.Length == 2),
            Arg.Is<long[]>(c => c.Length == 2));
    }

    [Test]
    public void SelectStoppedIndices_RespectsForceStart()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, ForceStart = true },
            new() { Id = 2, ForceStart = false },
            new() { Id = 3, ForceStart = false },
        };

        var stopped = BandwidthStopSelection.SelectStoppedIndices(torrents, 100, 100, () => 0.0);

        stopped.Should().ContainSingle();
        stopped.Should().NotContain(0);
    }

    [Test]
    public void SelectStoppedIndices_UsesTheDefaultRollWhenNoneIsInjected()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, ForceStart = false },
            new() { Id = 2, ForceStart = false },
        };

        var stopped = BandwidthStopSelection.SelectStoppedIndices(torrents, 50, 50);

        stopped.Count.Should().BeLessThanOrEqualTo(1);
    }

    [Test]
    public void Constructor_UsesTheCryptographicRollByDefault()
    {
        var config = Substitute.For<IConfigService>();
        config.UploadStoppedMinPercentage.Returns(0);
        config.UploadStoppedMaxPercentage.Returns(0);

        var selection = new BandwidthStopSelection(config);
        var stopped = selection.SelectUploadStoppedIndices(new List<Torrent>
        {
            new() { Id = 1, ForceStart = false },
            new() { Id = 2, ForceStart = false },
        });

        stopped.Should().BeEmpty();
    }
}

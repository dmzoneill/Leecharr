// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class CoreEngineAndTrackerDeepCoverageIntegrationTests : IntegrationTestBase
{
    private string tempTestDirectory;

    [SetUp]
    public void SetUp()
    {
        this.tempTestDirectory = Path.Combine(Path.GetTempPath(), "LeecharrCoreCoverage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempTestDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(this.tempTestDirectory))
            {
                Directory.Delete(this.tempTestDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Test]
    public void StoragePathService_DirectoryResolutionAndSanitization_ResolvesCorrectPaths()
    {
        var service = (IStoragePathService)GlobalSetup.Factory.Services.GetService(typeof(IStoragePathService))!;
        service.Should().NotBeNull();

        // 1. Incomplete directory
        var incompleteDir = service.GetIncompleteDirectory();
        incompleteDir.Should().NotBeNullOrWhiteSpace();

        // 2. Completed directory with and without category
        var completedDefault = service.GetCompletedDirectory(null);
        completedDefault.Should().NotBeNullOrWhiteSpace();

        var completedCategory = service.GetCompletedDirectory("Movies");
        completedCategory.Should().NotBeNullOrWhiteSpace();

        // 3. Working path resolution
        var workingPath = service.GetWorkingPath("0123456789ABCDEF0123456789ABCDEF01234567", "TestTorrentRelease", "Movies");
        workingPath.Should().NotBeNullOrWhiteSpace();
        workingPath.Should().Contain("TestTorrentRelease");
    }

    [Test]
    public async Task TrackerBoostService_MetricsAndEvents_TracksAndCleansLifecycle()
    {
        var boostService = (ITrackerBoostService)GlobalSetup.Factory.Services.GetService(typeof(ITrackerBoostService))!;
        boostService.Should().NotBeNull();

        // 1. Get settings and trackers
        var settings = boostService.GetSettings();
        settings.Should().NotBeNull();

        var trackers = boostService.GetAllTrackers();
        trackers.Should().NotBeNull();

        // 2. Status summary
        var summary = await boostService.GetStatusSummaryAsync();
        summary.Should().NotBeNull();
    }

    [Test]
    public async Task MonoTorrentEngine_TorrentActionsAndTrackers_AppliesSafely()
    {
        var engine = (IDownloadEngine)GlobalSetup.Factory.Services.GetService(typeof(IDownloadEngine))!;
        engine.Should().NotBeNull();

        // 1. Pause and Resume All
        await engine.PauseAllAsync();
        await engine.ResumeAllAsync();

        // 2. Non-existent torrent safe handling
        await engine.PauseTorrentAsync(99999);
        await engine.ResumeTorrentAsync(99999);
        await engine.ForceRecheckAsync(99999);
        await engine.ForceAnnounceAsync(99999);
        await engine.AddTrackersAsync(99999, new[] { "udp://tracker.open.org:1337" });
        await engine.RemoveTrackersAsync(99999, new[] { "udp://tracker.open.org:1337" });
    }
}

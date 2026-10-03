// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.FileBrowser;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.SystemServices;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class CoreSystemAndMediaServicesComprehensiveIntegrationTests : IntegrationTestBase
{
    private string tempDirectory;

    [SetUp]
    public void SetUp()
    {
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "LeecharrCoreSystemTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(this.tempDirectory))
            {
                Directory.Delete(this.tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore directory cleanup failure
        }
    }

    [Test]
    public async Task RuleHeuristicAiProvider_ParseReleaseAsync_ExtractsAllComponents()
    {
        var provider = new RuleHeuristicAiProvider();

        // 1. Probe health
        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
        health.ModelName.Should().NotBeNullOrWhiteSpace();

        // 2. Scene TV release
        var releaseTv = "Breaking.Bad.S05E16.Felina.1080p.BluRay.x264-ROVERS";
        var parsedTv = await provider.ParseReleaseAsync(releaseTv);
        parsedTv.Should().NotBeNull();
        parsedTv.Season.Should().Be(5);
        parsedTv.Episode.Should().Be(16);
        parsedTv.Quality.Should().Contain("1080p");
        parsedTv.ReleaseGroup.Should().Be("ROVERS");

        // 3. Movie release with Proper and Remux
        var releaseMovie = "Inception.2010.PROPER.REMUX.2160p.UHD.HDR.HEVC.TrueHD.Atmos.7.1-FGT";
        var parsedMovie = await provider.ParseReleaseAsync(releaseMovie);
        parsedMovie.Should().NotBeNull();
        parsedMovie.Year.Should().Be(2010);
        parsedMovie.IsProper.Should().BeTrue();
        parsedMovie.IsRemux.Should().BeTrue();
        parsedMovie.Resolution.Should().Be("2160p");

        // 4. Repack release
        var releaseRepack = "Game.of.Thrones.S01E01.REPACK.720p.HDTV.x264-CTU";
        var parsedRepack = await provider.ParseReleaseAsync(releaseRepack);
        parsedRepack.IsRepack.Should().BeTrue();

        // 5. Empty or whitespace title
        var parsedEmpty = await provider.ParseReleaseAsync(string.Empty);
        parsedEmpty.CleanTitle.Should().BeEmpty();
    }

    [Test]
    public async Task RuleHeuristicAiProvider_NaturalLanguageAndDiagnostics_OperatesCorrectly()
    {
        var provider = new RuleHeuristicAiProvider();

        // 1. Natural language search query translation
        var userQuery = "Download latest 4K movies directed by Christopher Nolan in HDR";
        var searchParams = await provider.ProcessNaturalLanguageSearchAsync(userQuery);
        searchParams.Should().NotBeNull();

        // 2. Swarm health diagnosis
        var activeTorrent = new Torrent
        {
            Id = 42,
            Name = "ActiveLinuxKernel.iso",
            Status = TorrentStatus.Downloading,
            DownloadSpeed = 10_000,
        };

        var diag = await provider.DiagnoseTorrentHealthAsync(activeTorrent, System.Array.Empty<PeerInfo>(), System.Array.Empty<TrackerEntry>());
        diag.Should().NotBeNull();

        // 3. Malware anomaly detection
        var suspiciousFiles = new List<TorrentFile>
        {
            new TorrentFile { Path = "setup.exe", Size = 1_000_000 },
            new TorrentFile { Path = "instructions.scr", Size = 50_000 },
        };

        var malwareReport = await provider.AnalyzeMalwareRiskAsync("SuspiciousTorrent", suspiciousFiles);
        malwareReport.Should().NotBeNull();
        malwareReport.RiskScore.Should().BeGreaterThan(0);

        // 4. Chat copilot response
        var chatResp = await provider.GenerateChatResponseAsync("How can I improve swarm speeds?");
        chatResp.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void PowerManagementService_InhibitionTokens_TakesAndReleases()
    {
        var powerService = (IPowerManagementService)GlobalSetup.Factory.Services.GetService(typeof(IPowerManagementService))!;
        powerService.Should().NotBeNull();

        // 1. Inhibit sleep lease
        using (var lease = powerService.InhibitSleep("Active Torrent Seeding Session"))
        {
            lease.Should().NotBeNull();
            powerService.IsSleepInhibited.Should().BeTrue();
            powerService.ActiveSleepInhibitionLeases.Should().BeGreaterThanOrEqualTo(1);
        }

        // 2. SetSleepInhibited direct toggle
        powerService.SetSleepInhibited(true, "Direct Toggle Test");
        powerService.SetSleepInhibited(false, "Direct Toggle End");
    }

    [Test]
    public void FileBrowserService_DirectoryBrowsing_ListsEntriesCorrectly()
    {
        var fileBrowser = (IFileBrowserService)GlobalSetup.Factory.Services.GetService(typeof(IFileBrowserService))!;
        fileBrowser.Should().NotBeNull();

        // 1. Create temporary files and directories
        var testFolder = Path.Combine(this.tempDirectory, "BrowserFolder");
        fileBrowser.CreateDirectory(testFolder);

        var testFile = Path.Combine(testFolder, "sample_document.txt");
        File.WriteAllText(testFile, "FileBrowser integration test contents");

        // 2. Browse directory
        var listing = fileBrowser.ListDirectory(testFolder);
        listing.Should().NotBeNull();
        listing.Entries.Should().NotBeEmpty();
        listing.Entries.Should().Contain(e => e.Name == "sample_document.txt");

        // 3. Resolve and parent path
        var parent = fileBrowser.GetParentPath(testFolder);
        parent.Should().NotBeNullOrWhiteSpace();

        var resolved = fileBrowser.ResolvePath(testFolder);
        resolved.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void MediaEnrichmentService_MetadataAndCache_HandlesOperationsSafely()
    {
        var mediaService = (IMediaEnrichmentService)GlobalSetup.Factory.Services.GetService(typeof(IMediaEnrichmentService))!;
        mediaService.Should().NotBeNull();

        // 1. Get metadata for non-existent torrent
        var metadata = mediaService.GetMetadata(99999);
        metadata.Should().BeNull();

        // 2. Get all metadata
        var all = mediaService.GetAllMetadata();
        all.Should().NotBeNull();

        // 3. Cleanup and cache operations
        mediaService.DeleteMetadata(99999);
        mediaService.CleanupTorrentCache(99999);
        mediaService.DeleteMediaCache(99999);
    }
}

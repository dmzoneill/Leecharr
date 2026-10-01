// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MonoTorrent.BEncoding;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class WatchFolderComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public void WatchFolder_CategoryMatching_DetectsReleaseTypes()
    {
        var services = GlobalSetup.Factory.Services;
        var watchFolderService = services.GetService(typeof(IWatchFolderService)) as IWatchFolderService;
        watchFolderService.Should().NotBeNull();

        // 1. Anime groups / patterns
        var animeCategory = watchFolderService.MatchCategoryFromReleaseName("[SubsPlease] Frieren - 28 (1080p) [ABCD1234].mkv");
        animeCategory.Should().Be("anime");

        var animeCategory2 = watchFolderService.MatchCategoryFromReleaseName("HorribleSubs.One.Piece.1000.1080p.mkv");
        animeCategory2.Should().Be("anime");

        // 2. TV patterns
        var tvCategory = watchFolderService.MatchCategoryFromReleaseName("Severance.S01E01.2160p.WEB-DL.mkv");
        tvCategory.Should().Be("tv");

        var tvCategory2 = watchFolderService.MatchCategoryFromReleaseName("Stranger.Things.4x01.1080p.mkv");
        tvCategory2.Should().Be("tv");

        // 3. Movie patterns
        var movieCategory = watchFolderService.MatchCategoryFromReleaseName("Dune.Part.Two.2024.2160p.UHD.Remux.mkv");
        movieCategory.Should().Be("movies");

        var movieCategory2 = watchFolderService.MatchCategoryFromReleaseName("Oppenheimer.2023.1080p.BluRay.x264.mkv");
        movieCategory2.Should().Be("movies");

        // 4. Music / Audio patterns
        var musicCategory = watchFolderService.MatchCategoryFromReleaseName("Daft.Punk.-.Discovery.(2001).[FLAC]");
        musicCategory.Should().Be("music");
    }

    [Test]
    public async Task WatchFolder_IsFileReadyAndStabilized_EvaluatesFileAccess()
    {
        var services = GlobalSetup.Factory.Services;
        var watchFolderService = services.GetService(typeof(IWatchFolderService)) as IWatchFolderService;
        watchFolderService.Should().NotBeNull();

        // 1. Non-existent file
        watchFolderService.IsFileReady("/path/to/non_existent_file_999.torrent").Should().BeFalse();
        var nonExistentStabilized = await watchFolderService.IsFileStabilizedAsync("/path/to/non_existent_file_999.torrent", TimeSpan.FromMilliseconds(50));
        nonExistentStabilized.Should().BeFalse();

        // 2. Real file stabilization check
        var tempFile = Path.Combine(Path.GetTempPath(), $"leecharr_stabilized_{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempFile, "sample data to verify file ready status");

        try
        {
            watchFolderService.IsFileReady(tempFile).Should().BeTrue();
            var stabilized = await watchFolderService.IsFileStabilizedAsync(tempFile, TimeSpan.FromMilliseconds(50));
            stabilized.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task WatchFolder_ProcessFile_AddsTorrentFromTorrentFile()
    {
        var services = GlobalSetup.Factory.Services;
        var watchFolderService = services.GetService(typeof(IWatchFolderService)) as IWatchFolderService;
        var torrentService = services.GetService(typeof(ITorrentService)) as ITorrentService;

        watchFolderService.Should().NotBeNull();
        torrentService.Should().NotBeNull();

        var pieces = new byte[20];
        for (var i = 0; i < pieces.Length; i++)
        {
            pieces[i] = (byte)(i + 1);
        }

        var infoDict = new BEncodedDictionary
        {
            { "name", new BEncodedString("WatchFolderAutoImportMovie.2024.1080p.mkv") },
            { "piece length", new BEncodedNumber(16384) },
            { "pieces", new BEncodedString(pieces) },
            { "length", new BEncodedNumber(16384) },
        };

        var rootDict = new BEncodedDictionary
        {
            { "announce", new BEncodedString("http://tracker.watchfolder.org/announce") },
            { "info", infoDict },
        };

        var tempTorrentFile = Path.Combine(Path.GetTempPath(), $"import_{Guid.NewGuid():N}.torrent");
        await File.WriteAllBytesAsync(tempTorrentFile, rootDict.Encode());

        try
        {
            var processed = await watchFolderService.ProcessFileAsync(tempTorrentFile);
            processed.Should().BeTrue();

            // Verify torrent was added
            var all = torrentService.GetAll();
            var matched = all.FirstOrDefault(t => t.Name.Contains("WatchFolderAutoImportMovie"));
            matched.Should().NotBeNull();

            // Clean up added torrent
            await torrentService.DeleteAsync(matched.Id, true);
        }
        finally
        {
            if (File.Exists(tempTorrentFile))
            {
                File.Delete(tempTorrentFile);
            }
        }
    }

    [Test]
    public async Task WatchFolder_CommandsAndConfigEvents_HandleLifecycle()
    {
        var services = GlobalSetup.Factory.Services;
        var watchFolderService = services.GetService(typeof(IWatchFolderService)) as WatchFolderService;
        watchFolderService.Should().NotBeNull();

        // 1. Scan command synchronous
        watchFolderService.Execute(new WatchFolderScanCommand());

        // 2. Scan command asynchronous
        await watchFolderService.ExecuteAsync(new WatchFolderScanCommand());

        // 3. Config saved event handlers
        watchFolderService.Handle(new ConfigSavedEvent());
        watchFolderService.Handle(new ConfigFileSavedEvent());

        // 4. Start and Stop watcher
        watchFolderService.StartWatcher();
        watchFolderService.StopWatcher();
    }
}

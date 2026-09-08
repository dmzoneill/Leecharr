// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;

namespace Leecharr.Core.Test.WatchFolder;

[TestFixture]
public class WatchFolderServiceTest
{
    private IConfigService configService = null!;
    private ITorrentService torrentService = null!;
    private ITorrentFileParser torrentFileParser = null!;
    private ICategoryService categoryService = null!;
    private IDiskProvider diskProvider = null!;
    private WatchFolderService service = null!;
    private string tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "leecharr_watch_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDirectory);

        this.configService = Substitute.For<IConfigService>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileParser = Substitute.For<ITorrentFileParser>();
        this.categoryService = Substitute.For<ICategoryService>();
        this.diskProvider = Substitute.For<IDiskProvider>();

        this.configService.DefaultCategory.Returns("default");
        this.configService.WatchFolderEnabled.Returns(true);
        this.configService.WatchFolderPath.Returns(this.tempDirectory);
        this.configService.WatchFolderAutoStartTorrents.Returns(true);
        this.configService.WatchFolderDeleteAddedTorrents.Returns(true);
        this.configService.WatchFolderDebounceMilliseconds.Returns(10);

        this.diskProvider.FolderExists(this.tempDirectory).Returns(true);

        this.service = new WatchFolderService(
            this.configService,
            this.torrentService,
            this.torrentFileParser,
            this.categoryService,
            this.diskProvider);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDirectory))
        {
            try
            {
                Directory.Delete(this.tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Scene Release Regex Categorization

    [TestCase("Severance.S02E01.1080p.WEB-DL.x265", "tv")]
    [TestCase("Severance.S01E01-E04.1080p.WEB-DL", "tv")]
    [TestCase("The.Office.S01E01E02.1080p.BluRay", "tv")]
    [TestCase("Doctor.Who.1x01.Rose.720p.HDTV", "tv")]
    [TestCase("Doctor.Who.1x101.720p.HDTV", "tv")]
    [TestCase("Doctor.Who.1x1001.720p.HDTV", "tv")]
    [TestCase("One.Piece.S01E101.1080p", "tv")]
    [TestCase("One.Piece.S01E1001.1080p", "tv")]
    [TestCase("Show.Title.E102.720p.HDTV", "tv")]
    [TestCase("Show.Title.E1001.1080p.WEB-DL", "tv")]
    [TestCase("Show.Title.EP102.720p.HDTV", "tv")]
    [TestCase("Show.Title.EP1001.1080p.WEB-DL", "tv")]
    [TestCase("The.Daily.Show.2024.01.15.1080p.WEB-DL", "tv")]
    [TestCase("Late.Night.2024-03-20.720p.HDTV", "tv")]
    [TestCase("The.Tonight.Show.2024_05_12.1080p.HDTV", "tv")]
    [TestCase("Daily.News.2024.11.05.720p.HDTV", "tv")]
    [TestCase("Daily.News.2024-11-05.720p.HDTV", "tv")]
    [TestCase("Daily.News.2024_11_05.720p.HDTV", "tv")]
    [TestCase("Breaking.Bad.Season.1.Complete", "tv")]
    [TestCase("Game.of.Thrones.S08E06.2160p", "tv")]
    [TestCase("House.Complete.Series", "tv")]
    [TestCase("Loki.Season.2.1080p", "tv")]
    [TestCase("[TGx] Breaking Bad S01", "tv")]
    [TestCase("[EZTV] The Office", "tv")]
    [TestCase("[EZTV] The Office S02E05", "tv")]
    [TestCase("[YTS] Dune 2", "movies")]
    [TestCase("[YTS] Dune.Part.Two.2024.1080p", "movies")]
    [TestCase("[RARBG] The Matrix 1999 1080p BluRay", "movies")]
    [TestCase("[SubsPlease] Frieren - 28 (1080p) [12345678].mkv", "anime")]
    [TestCase("[Erai-raws] One Piece - 1100 [1080p]", "anime")]
    [TestCase("[HorribleSubs] Bleach - 366 [720p].mkv", "anime")]
    [TestCase("[Judas] Demon Slayer S03 [1080p]", "anime")]
    [TestCase("[MTBB] Bleach - Thousand-Year Blood War - 15 [1080p]", "anime")]
    [TestCase("[Kaleido] Jujutsu Kaisen - 24 [1080p]", "anime")]
    [TestCase("[Moozzi2] Attack on Titan - 01", "anime")]
    [TestCase("[Yameii] Dandadan - 01 [1080p]", "anime")]
    [TestCase("[Beatrice-Raws] KonoSuba - 01 [BDRip 1920x1080]", "anime")]
    [TestCase("[ReinForce] Sousou no Frieren - 01 [BDRip]", "anime")]
    [TestCase("[AnimeRG] Naruto Shippuden - 500 [720p]", "anime")]
    [TestCase("[NC-Raws] Solo Leveling - 01 (B-Global 1920x1080 HEVC AAC MKV)", "anime")]
    [TestCase("[SomeFansub] Generic Show Title - 04 [1080p]", "anime")]
    [TestCase("[Erai-raws] Demon Slayer - 01 [1080p][HEVC].mkv", "anime")]
    [TestCase("[SubsPlease] Bleach - 12 (1080p) [ABCD1234].mkv", "anime")]
    [TestCase("[HorribleSubs] Fairy Tail - 150 [720p].mkv", "anime")]
    [TestCase("[DKB] Jujutsu Kaisen 2023 1080p Batch Dual Audio", "anime")]
    [TestCase("Sousou no Frieren 2023 1080p Batch Subs", "anime")]
    [TestCase("Bleach 2022 Complete 1080p Dual Audio", "anime")]
    [TestCase("Naruto.Batch.1080p.Dual.Audio.FLAC", "anime")]
    [TestCase("Dune.Part.Two.2024.2160p.UHD.BluRay.x265-FLUX", "movies")]
    [TestCase("Oppenheimer.2023.1080p.Remux", "movies")]
    [TestCase("The.Matrix.1999.2160p.WEB-DL", "movies")]
    [TestCase("Interstellar.2014.720p.BluRay", "movies")]
    [TestCase("Avatar.The.Way.of.Water.2022.IMAX.1080p.WEBRip", "movies")]
    [TestCase("Top.Gun.Maverick.2022.DV.HDR.2160p", "movies")]
    [TestCase("Gladiator.2000.HDTV.720p", "movies")]
    [TestCase("Classic.Movie.1985.DVDRip.x264", "movies")]
    [TestCase("Film.Title.2010.BDRip.x264", "movies")]
    [TestCase("Pink.Floyd-The.Dark.Side.Of.The.Moon.1973.FLAC.Lossless", "music")]
    [TestCase("Taylor.Swift-1989.MP3.320kbps", "music")]
    [TestCase("Daft.Punk-Discovery.Vinyl", "music")]
    [TestCase("Beatles.Discography.CD.Album", "music")]
    [TestCase("Dua.Lipa-Radical.Optimism.2024.ALAC", "music")]
    [TestCase("Billie.Eilish-Hit.Me.Hard.And.Soft.AAC", "music")]
    [TestCase("Hans.Zimmer-Dune.Part.Two.Soundtrack.WAV", "music")]
    [TestCase("Inception.OST.FLAC.24bit.Hi-Res", "music")]
    [TestCase("Artist.Album.Opus", "music")]
    [TestCase("Radiohead-OK.Computer.AIFF", "music")]
    [TestCase("Podcast.Episode.OGG", "music")]
    [TestCase("Random.Document.v1.0.pdf", "default")]
    [TestCase("Application.Setup.exe", "default")]
    [TestCase("", "default")]
    [TestCase(null, "default")]
    public void MatchCategoryFromReleaseName_ClassifiesSceneReleasesCorrectly(string releaseName, string expectedCategory)
    {
        var category = this.service.MatchCategoryFromReleaseName(releaseName);
        category.Should().Be(expectedCategory);
    }

    #endregion

    #region Auto-Add Processing and Deletion / Moving

    [Test]
    public async Task ScanWatchFolderAsync_WhenAutoDeleteEnabled_AddsTorrentAndDeletesFile()
    {
        var torrentFile = Path.Combine(this.tempDirectory, "test.torrent");
        await File.WriteAllBytesAsync(torrentFile, new byte[] { 1, 2, 3 });

        this.diskProvider.GetFiles(this.tempDirectory, false).Returns(new[] { torrentFile });

        var parsedTorrent = new ParsedTorrent
        {
            Name = "Dune.Part.Two.2024.2160p.UHD.BluRay.x265-FLUX",
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            TotalSize = 40000000000,
        };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsedTorrent);
        this.configService.WatchFolderDeleteAddedTorrents.Returns(true);
        this.configService.WatchFolderAutoStartTorrents.Returns(true);

        await this.service.ScanWatchFolderAsync();

        await this.torrentService.Received(1).AddFromParsedTorrentAsync(
            parsedTorrent,
            category: "movies",
            startPaused: false,
            rawBytes: Arg.Any<byte[]>());

        this.diskProvider.Received(1).DeleteFile(torrentFile);
    }

    [Test]
    public async Task ScanWatchFolderAsync_WhenAutoDeleteDisabled_MovesFileToLoadedDirectory()
    {
        var torrentFile = Path.Combine(this.tempDirectory, "show.torrent");
        await File.WriteAllBytesAsync(torrentFile, new byte[] { 1, 2, 3 });

        this.diskProvider.GetFiles(this.tempDirectory, false).Returns(new[] { torrentFile });

        var parsedTorrent = new ParsedTorrent
        {
            Name = "Severance.S02E01.1080p.WEB-DL.x265",
            InfoHash = "abcdef0123456789abcdef0123456789abcdef01",
            TotalSize = 2000000000,
        };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsedTorrent);
        this.configService.WatchFolderDeleteAddedTorrents.Returns(false);
        this.configService.WatchFolderAutoStartTorrents.Returns(false);

        await this.service.ScanWatchFolderAsync();

        await this.torrentService.Received(1).AddFromParsedTorrentAsync(
            parsedTorrent,
            category: "tv",
            startPaused: true,
            rawBytes: Arg.Any<byte[]>());

        this.diskProvider.Received(1).EnsureFolder(Path.Combine(this.tempDirectory, "loaded"));
        this.diskProvider.Received(1).MoveFile(
            torrentFile,
            Path.Combine(this.tempDirectory, "loaded", "show.torrent"),
            true);
    }

    #endregion

    #region File Lock Resilience During In-Flight Copying

    [Test]
    public async Task ScanWatchFolderAsync_WhenFileIsLockedDuringInFlightCopy_LogsAndContinuesRemainingFiles()
    {
        var lockedFile = Path.Combine(this.tempDirectory, "locked_in_flight.torrent");
        var validFile = Path.Combine(this.tempDirectory, "valid.torrent");

        await File.WriteAllBytesAsync(validFile, new byte[] { 1, 2, 3 });

        this.diskProvider.GetFiles(this.tempDirectory, false).Returns(new[] { lockedFile, validFile });

        var parsedTorrent = new ParsedTorrent
        {
            Name = "Oppenheimer.2023.1080p.Remux",
            InfoHash = "1234567890123456789012345678901234567890",
        };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsedTorrent);

        // lockedFile does not exist or cannot be read (throwing IOException), but validFile will succeed
        var act = async () => await this.service.ScanWatchFolderAsync();

        // Must not throw exception
        await act.Should().NotThrowAsync();

        // Valid file should still be processed and added
        await this.torrentService.Received(1).AddFromParsedTorrentAsync(
            parsedTorrent,
            category: "movies",
            startPaused: false,
            rawBytes: Arg.Any<byte[]>());

        this.diskProvider.Received(1).DeleteFile(validFile);
    }

    [Test]
    public async Task ScanWatchFolderAsync_WhenDisabled_DoesNotScan()
    {
        this.configService.WatchFolderEnabled.Returns(false);

        await this.service.ScanWatchFolderAsync();

        this.diskProvider.DidNotReceive().GetFiles(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public async Task ScanWatchFolderAsync_WhenFolderDoesNotExist_DoesNotScan()
    {
        this.diskProvider.FolderExists(this.tempDirectory).Returns(false);

        await this.service.ScanWatchFolderAsync();

        this.diskProvider.DidNotReceive().GetFiles(Arg.Any<string>(), Arg.Any<bool>());
    }

    #endregion

    #region IsFileReady Tests

    [Test]
    public void IsFileReady_WhenFileDoesNotExist_ReturnsFalse()
    {
        var nonExistent = Path.Combine(this.tempDirectory, "non_existent.torrent");

        this.service.IsFileReady(nonExistent).Should().BeFalse();
    }

    [Test]
    public void IsFileReady_WhenFileIsEmpty_ReturnsFalse()
    {
        var emptyFile = Path.Combine(this.tempDirectory, "empty.torrent");
        File.WriteAllBytes(emptyFile, Array.Empty<byte>());

        this.service.IsFileReady(emptyFile).Should().BeFalse();
    }

    [Test]
    public void IsFileReady_WhenFileHasContentAndIsUnlocked_ReturnsTrue()
    {
        var readyFile = Path.Combine(this.tempDirectory, "ready.torrent");
        File.WriteAllBytes(readyFile, new byte[] { 1, 2, 3 });

        this.service.IsFileReady(readyFile).Should().BeTrue();
    }

    [Test]
    public void IsFileReady_WhenFileIsLockedByAnotherProcess_ReturnsFalse()
    {
        var lockedFile = Path.Combine(this.tempDirectory, "locked.torrent");
        File.WriteAllBytes(lockedFile, new byte[] { 1, 2, 3 });

        using var lockStream = File.Open(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        this.service.IsFileReady(lockedFile).Should().BeFalse();
    }

    [Test]
    public async Task IsFileStabilizedAsync_WhenFileDoesNotExist_ReturnsFalse()
    {
        var nonExistent = Path.Combine(this.tempDirectory, "missing.torrent");
        var result = await this.service.IsFileStabilizedAsync(nonExistent, TimeSpan.FromMilliseconds(10));
        result.Should().BeFalse();
    }

    [Test]
    public async Task IsFileStabilizedAsync_WhenFileIsEmpty_ReturnsFalse()
    {
        var emptyFile = Path.Combine(this.tempDirectory, "empty_stabilize.torrent");
        await File.WriteAllBytesAsync(emptyFile, Array.Empty<byte>());

        var result = await this.service.IsFileStabilizedAsync(emptyFile, TimeSpan.FromMilliseconds(10));
        result.Should().BeFalse();
    }

    [Test]
    public async Task IsFileStabilizedAsync_WhenFileIsLocked_ReturnsFalse()
    {
        var lockedFile = Path.Combine(this.tempDirectory, "locked_stabilize.torrent");
        await File.WriteAllBytesAsync(lockedFile, new byte[] { 1, 2, 3 });

        using var lockStream = File.Open(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await this.service.IsFileStabilizedAsync(lockedFile, TimeSpan.FromMilliseconds(10));
        result.Should().BeFalse();
    }

    [Test]
    public async Task IsFileStabilizedAsync_WhenFileSizeChangesDuringDebounce_ReturnsFalse()
    {
        var growingFile = Path.Combine(this.tempDirectory, "growing.torrent");
        await File.WriteAllBytesAsync(growingFile, new byte[] { 1, 2, 3 });

        var checkTask = this.service.IsFileStabilizedAsync(growingFile, TimeSpan.FromMilliseconds(100));

        await Task.Delay(30);
        await File.WriteAllBytesAsync(growingFile, new byte[] { 1, 2, 3, 4, 5, 6 });

        var result = await checkTask;
        result.Should().BeFalse();
    }

    [Test]
    public async Task IsFileStabilizedAsync_WhenFileSizeIsStableAndUnlocked_ReturnsTrue()
    {
        var stableFile = Path.Combine(this.tempDirectory, "stable.torrent");
        await File.WriteAllBytesAsync(stableFile, new byte[] { 1, 2, 3, 4 });

        var result = await this.service.IsFileStabilizedAsync(stableFile, TimeSpan.FromMilliseconds(20));
        result.Should().BeTrue();
    }

    #endregion

    #region Quarantine Tests

    [Test]
    public async Task ScanWatchFolderAsync_WhenTorrentFailsParsingThreeTimes_QuarantinesToFailedDirectory()
    {
        var corruptFile = Path.Combine(this.tempDirectory, "corrupt.torrent");
        await File.WriteAllBytesAsync(corruptFile, new byte[] { 0x64, 0x30, 0x65 });

        this.diskProvider.GetFiles(this.tempDirectory, false).Returns(new[] { corruptFile });
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(_ => throw new InvalidTorrentFileException("Corrupt Bencode"));

        // Scan 1 - attempt 1: should not quarantine
        await this.service.ScanWatchFolderAsync();
        this.diskProvider.DidNotReceive().MoveFile(corruptFile, Arg.Any<string>(), Arg.Any<bool>());

        // Scan 2 - attempt 2: should not quarantine
        await this.service.ScanWatchFolderAsync();
        this.diskProvider.DidNotReceive().MoveFile(corruptFile, Arg.Any<string>(), Arg.Any<bool>());

        // Scan 3 - attempt 3: should quarantine to failed/
        await this.service.ScanWatchFolderAsync();

        var expectedFailedDir = Path.Combine(this.tempDirectory, "failed");
        var expectedDest = Path.Combine(expectedFailedDir, "corrupt.torrent");

        this.diskProvider.Received(1).EnsureFolder(expectedFailedDir);
        this.diskProvider.Received(1).MoveFile(corruptFile, expectedDest, true);
    }

    [Test]
    public async Task ProcessFileAsync_WhenRelativeAndAbsolutePathsFail_SharesFailureCounterAndQuarantinesOnThirdAttempt()
    {
        var fullPath = Path.Combine(this.tempDirectory, "corrupt_shared.torrent");
        await File.WriteAllBytesAsync(fullPath, new byte[] { 0x64, 0x30, 0x65 });

        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath);
        var dotSegmentPath = Path.Combine(this.tempDirectory, ".", "corrupt_shared.torrent");

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(_ => throw new InvalidTorrentFileException("Corrupt Bencode"));

        // Attempt 1: via relative path
        var result1 = await this.service.ProcessFileAsync(relativePath, this.tempDirectory);
        result1.Should().BeFalse();
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());

        // Attempt 2: via dot segment path
        var result2 = await this.service.ProcessFileAsync(dotSegmentPath, this.tempDirectory);
        result2.Should().BeFalse();
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());

        // Attempt 3: via absolute path
        var result3 = await this.service.ProcessFileAsync(fullPath, this.tempDirectory);
        result3.Should().BeFalse();

        var expectedFailedDir = Path.Combine(this.tempDirectory, "failed");
        var expectedDest = Path.Combine(expectedFailedDir, "corrupt_shared.torrent");

        this.diskProvider.Received(1).EnsureFolder(expectedFailedDir);
        this.diskProvider.Received(1).MoveFile(fullPath, expectedDest, true);
    }

    [Test]
    public async Task ProcessFileAsync_WhenRelativePathFailsAndAbsolutePathSucceeds_ClearsFailureCounter()
    {
        var fullPath = Path.Combine(this.tempDirectory, "recoverable.torrent");
        await File.WriteAllBytesAsync(fullPath, new byte[] { 0x64, 0x31, 0x65 });

        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath);

        var parsed = new ParsedTorrent
        {
            Name = "Recovered.Movie.2024.1080p",
            InfoHash = "1122334455667788990011223344556677889900",
            TotalSize = 1024,
        };

        // Two failures via relative path, then success via absolute path, then one failure via relative path
        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(
            _ => throw new InvalidTorrentFileException("Attempt 1 failure"),
            _ => throw new InvalidTorrentFileException("Attempt 2 failure"),
            _ => parsed,
            _ => throw new InvalidTorrentFileException("Fresh attempt 1 failure"));

        // Attempt 1 (relative path): fails
        var result1 = await this.service.ProcessFileAsync(relativePath, this.tempDirectory);
        result1.Should().BeFalse();

        // Attempt 2 (relative path): fails
        var result2 = await this.service.ProcessFileAsync(relativePath, this.tempDirectory);
        result2.Should().BeFalse();

        // Attempt 3 (absolute path): succeeds and clears failedAttempts counter
        var result3 = await this.service.ProcessFileAsync(fullPath, this.tempDirectory);
        result3.Should().BeTrue();

        // Ensure file exists for 4th invocation (since DeleteFile mock doesn't delete from disk)
        if (!File.Exists(fullPath))
        {
            await File.WriteAllBytesAsync(fullPath, new byte[] { 0x64, 0x31, 0x65 });
        }

        // Attempt 4 (relative path): fails again. Because counter was cleared, this is attempt 1, not attempt 3 (should not quarantine)
        var result4 = await this.service.ProcessFileAsync(relativePath, this.tempDirectory);
        result4.Should().BeFalse();

        var expectedFailedDir = Path.Combine(this.tempDirectory, "failed");
        var expectedDest = Path.Combine(expectedFailedDir, "recoverable.torrent");

        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), expectedDest, Arg.Any<bool>());
    }

    #endregion

    #region Category Cross-Referencing

    [Test]
    public void MatchCategoryFromReleaseName_WhenConfiguredCategoryMatches_ResolvesToConfiguredCategoryName()
    {
        this.categoryService.GetAll().Returns(new[]
        {
            new Category { Name = "TV Shows" },
            new Category { Name = "Feature Films" },
        });

        var resultTv = this.service.MatchCategoryFromReleaseName("Breaking.Bad.S01E01.1080p");
        resultTv.Should().Be("TV Shows");
    }

    #endregion

    #region FileSystemWatcher and Async Void Reliability Tests

    [Test]
    public async Task OnFileSystemWatcherCreated_WhenFileThrowsUnexpectedException_CatchesAndDoesNotCrashProcess()
    {
        var testFile = Path.Combine(this.tempDirectory, "throwing.torrent");
        await File.WriteAllBytesAsync(testFile, new byte[] { 0x64, 0x31, 0x30, 0x65 });

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(_ => throw new InvalidOperationException("Fatal parser explosion"));

        var action = () =>
        {
            this.service.OnFileSystemWatcherCreated(this, new FileSystemEventArgs(WatcherChangeTypes.Created, this.tempDirectory, "throwing.torrent"));
        };

        action.Should().NotThrow();

        await Task.Delay(150);

        await this.torrentService.DidNotReceive().AddFromParsedTorrentAsync(
            Arg.Any<ParsedTorrent>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<byte[]>());
    }

    [Test]
    public async Task OnFileSystemWatcherCreated_WhenValidTorrent_ProcessesSuccessfully()
    {
        var testFile = Path.Combine(this.tempDirectory, "valid.torrent");
        await File.WriteAllBytesAsync(testFile, new byte[] { 0x64, 0x32, 0x30, 0x65 });

        var parsed = new ParsedTorrent
        {
            Name = "Valid.Movie.2024.1080p",
            InfoHash = "1234567890123456789012345678901234567890",
            TotalSize = 1024,
        };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);

        this.service.OnFileSystemWatcherCreated(this, new FileSystemEventArgs(WatcherChangeTypes.Created, this.tempDirectory, "valid.torrent"));

        await Task.Delay(150);

        await this.torrentService.Received(1).AddFromParsedTorrentAsync(
            parsed,
            category: "movies",
            savePath: null,
            startPaused: false,
            rawBytes: Arg.Any<byte[]>());
    }

    [Test]
    public void StartWatcher_AndStopWatcher_DoNotThrowExceptions()
    {
        var startAction = () => this.service.StartWatcher();
        startAction.Should().NotThrow();

        var stopAction = () => this.service.StopWatcher();
        stopAction.Should().NotThrow();

        var disposeAction = () => this.service.Dispose();
        disposeAction.Should().NotThrow();
    }

    #endregion
    [Test]
    public async Task ProcessFileAsync_WhenCalledConcurrentlyForSameFile_OnlyProcessesOnce()
    {
        var testFile = Path.Combine(this.tempDirectory, "concurrent.torrent");
        await File.WriteAllBytesAsync(testFile, new byte[] { 0x64, 0x33, 0x30, 0x65 });

        var parsed = new ParsedTorrent
        {
            Name = "Concurrent.Movie.2024.1080p",
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            TotalSize = 2048,
        };

        this.torrentFileParser.Parse(Arg.Any<byte[]>()).Returns(parsed);

        var task1 = this.service.ProcessFileAsync(testFile);
        var task2 = this.service.ProcessFileAsync(testFile);

        var results = await Task.WhenAll(task1, task2);

        // One must succeed, one must be skipped (return false due to duplicate guard)
        results.Should().ContainSingle(r => r == true);
        results.Should().ContainSingle(r => r == false);

        await this.torrentService.Received(1).AddFromParsedTorrentAsync(
            parsed,
            category: Arg.Any<string>(),
            startPaused: Arg.Any<bool>(),
            rawBytes: Arg.Any<byte[]>());
    }
}

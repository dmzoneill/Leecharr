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
    [TestCase("Breaking.Bad.Season.1.Complete", "tv")]
    [TestCase("Game.of.Thrones.S08E06.2160p", "tv")]
    [TestCase("House.Complete.Series", "tv")]
    [TestCase("Loki.Season.2.1080p", "tv")]
    [TestCase("[SubsPlease] Frieren - 28 (1080p) [12345678].mkv", "anime")]
    [TestCase("[Erai-raws] One Piece - 1100 [1080p]", "anime")]
    [TestCase("[HorribleSubs] Bleach - 366 [720p].mkv", "anime")]
    [TestCase("[Judas] Demon Slayer S03 [1080p]", "anime")]
    [TestCase("Naruto.Batch.1080p.Dual.Audio.FLAC", "anime")]
    [TestCase("Dune.Part.Two.2024.2160p.UHD.BluRay.x265-FLUX", "movies")]
    [TestCase("Oppenheimer.2023.1080p.Remux", "movies")]
    [TestCase("The.Matrix.1999.2160p.WEB-DL", "movies")]
    [TestCase("Interstellar.2014.720p.BluRay", "movies")]
    [TestCase("Pink.Floyd-The.Dark.Side.Of.The.Moon.1973.FLAC.Lossless", "music")]
    [TestCase("Taylor.Swift-1989.MP3.320kbps", "music")]
    [TestCase("Daft.Punk-Discovery.Vinyl", "music")]
    [TestCase("Beatles.Discography.CD.Album", "music")]
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
}
